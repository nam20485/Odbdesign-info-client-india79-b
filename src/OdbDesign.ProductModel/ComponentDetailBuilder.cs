using Microsoft.Extensions.Logging;
using Odb.Lib.Protobuf;
using Odb.Lib.Protobuf.ProductModel;
using OdbDesign.ProductModel.Models;

namespace OdbDesign.ProductModel;

/// <summary>
/// Builds <see cref="ComponentDetail"/> records for one design/step from the
/// server-owned <c>Connectivity</c> message of a gRPC <c>GetDesign</c> response —
/// no additional server round trips and, by contract, no client-side join.
///
/// Division of truth (component-connectivity.md §6/§7): <b>relationships</b>
/// (which pins a component has, and through which pins it reaches which net) are
/// the server's — this builder reads them straight off
/// <see cref="Connectivity.Types.PlacedComponent.Pins"/> and
/// <c>netMembers</c>. <b>Geometry, placement, and attributes</b> stay in the
/// <c>FileModel</c> (components files, EDA data) and are attached here through
/// local indexes over <i>published identity fields</i> only (conformance rule 1):
/// refDes → component record, package name → package record, pin number →
/// toeprint geometry. No positional reference (componentNumber, toeprintNumber,
/// netNumber) is ever resolved against another collection (rule 2), and no format
/// sentinel (<c>-1</c>, <c>$NONE$</c>) is ever interpreted — the server absorbed
/// those into <see cref="Connectivity.Types.Connection.Types.Kind"/> and resolved
/// pin numbers.
///
/// Every lookup degrades to null/empty when the data does not line up; the caller
/// simply shows less rather than failing the load.
/// </summary>
/// <remarks>
/// The server derives <c>Connectivity</c> from the first step of its product
/// model (<c>Design::Build</c> walks <c>steps.begin()</c>); designs served today
/// are single-step, so the block matches the step the reader picked. A multi-step
/// archive would already disagree with the server's whole product model, not just
/// this view.
/// </remarks>
public class ComponentDetailBuilder
{
    private readonly ILogger<ComponentDetailBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentDetailBuilder"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public ComponentDetailBuilder(ILogger<ComponentDetailBuilder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds the component/net product-model index for one step from the design's
    /// server-owned <c>Connectivity</c> block, enriched with fileModel placement,
    /// package, attribute, and BOM data.
    /// </summary>
    /// <param name="topComponents">The top-side components file (may be null).</param>
    /// <param name="bottomComponents">The bottom-side components file (may be null).</param>
    /// <param name="edaData">The step's EDA data (packages, net attributes; may be null).</param>
    /// <param name="connectivity">The server-derived connectivity contract (may be null on servers predating it).</param>
    /// <returns>The component/net index; <see cref="ComponentDetailIndex.Empty"/> when Connectivity is absent.</returns>
    public ComponentDetailIndex Build(
        ComponentsFile? topComponents,
        ComponentsFile? bottomComponents,
        EdaDataFile? edaData,
        Connectivity? connectivity)
    {
        if (connectivity == null)
        {
            return ComponentDetailIndex.Empty;
        }

        // Placement coordinates live in the components files, package geometry in
        // EdaDataFile.PackageRecords — the two headers can declare different units,
        // so each scales by its own. Package geometry is name-keyed (the contract's
        // `packages`/`PlacedComponent.package` identity), never by the positional
        // PkgRef index.
        var packageScale = UnitsHelper.UnitsToMmScale(edaData?.Units);
        var packagesByName = new Dictionary<string, PackageDetail>(StringComparer.Ordinal);
        if (edaData != null)
        {
            foreach (var pkg in edaData.PackageRecords)
            {
                packagesByName.TryAdd(pkg.Name, new PackageDetail(
                    pkg.Name,
                    pkg.Pitch > 0 ? pkg.Pitch * packageScale : null,
                    pkg.XMin * packageScale,
                    pkg.YMin * packageScale,
                    pkg.XMax * packageScale,
                    pkg.YMax * packageScale,
                    pkg.PinRecords.Count,
                    pkg.OutlineRecords.Count));
            }
        }

        // refDes → component record, over published identity fields only. The
        // contract makes refDes the authoritative component key (spec p.152,
        // case-significant — Ordinal, never OrdinalIgnoreCase, which would merge
        // R1/r1 on case-differing designs; §8 hazard 3). First record wins a
        // duplicate refDes: a design with two same-named records violates the
        // identity rule, and last-wins would silently swap placement data.
        var recordByRefDes = new Dictionary<string, (ComponentsFile.Types.ComponentRecord Record, ComponentsFile File)>(
            StringComparer.Ordinal);
        AddRecordsByRefDes(topComponents, recordByRefDes);
        AddRecordsByRefDes(bottomComponents, recordByRefDes);

        // Per-component pin rows and net summaries, then the net-tab rows from
        // netMembers. refDes is unique per the identity rule; on a violating
        // message the first entry wins and the duplicate is logged, not thrown
        // (degrade-never-fail).
        var components = new List<ComponentDetail>(connectivity.Components.Count);
        var byRefDes = new Dictionary<string, ComponentDetail>(StringComparer.Ordinal);
        var sideByRefDes = new Dictionary<string, BoardSide>(StringComparer.Ordinal);
        var unenriched = 0;

        foreach (var placed in connectivity.Components)
        {
            sideByRefDes.TryAdd(placed.RefDes, placed.Side);
            recordByRefDes.TryGetValue(placed.RefDes, out var entry);
            if (entry.Record is null)
            {
                unenriched++;
            }

            var detail = BuildDetail(placed, entry, packagesByName, connectivity.Nets);
            if (byRefDes.TryAdd(placed.RefDes, detail))
            {
                components.Add(detail);
            }
            else
            {
                _logger.LogDebug(
                    "Duplicate refDes '{RefDes}' in Connectivity components; keeping the first entry",
                    placed.RefDes);
            }
        }

        var nets = BuildNetDetails(connectivity, edaData, sideByRefDes);

        _logger.LogDebug(
            "Built component detail index: {Components} components, {Nets} nets, {Unenriched} components without fileModel placement data",
            components.Count, nets.Count, unenriched);

        return new ComponentDetailIndex(components, byRefDes, nets);
    }

    private ComponentDetail BuildDetail(
        Connectivity.Types.PlacedComponent placed,
        (ComponentsFile.Types.ComponentRecord Record, ComponentsFile File) entry,
        Dictionary<string, PackageDetail> packagesByName,
        IReadOnlyList<Connectivity.Types.NetEntry> netEntries)
    {
        // The record may be absent (server/filemodel disagreement): placement,
        // attributes, and BOM degrade to empty; the pin roster and net truth stay.
        var record = entry.Record;
        var placementScale = UnitsHelper.UnitsToMmScale(entry.File?.Units);

        // Toeprint geometry for the pin rows, keyed on the toeprint's published
        // pin number — the same identity space the server resolved
        // ComponentPin.number from. This is an identity match on a published
        // field (rule 1), not a toeprintNumber reference resolution (rule 2).
        Dictionary<uint, ComponentsFile.Types.ComponentRecord.Types.ToeprintRecord>? toeprintsByNumber = null;
        if (record != null && record.ToeprintRecords.Count > 0)
        {
            toeprintsByNumber = new Dictionary<uint, ComponentsFile.Types.ComponentRecord.Types.ToeprintRecord>();
            foreach (var toeprint in record.ToeprintRecords)
            {
                toeprintsByNumber.TryAdd(toeprint.PinNumber, toeprint);
            }
        }

        var pins = new List<PinDetail>(placed.Pins.Count);
        var pinsByNet = new Dictionary<uint, int>();
        foreach (var pin in placed.Pins)
        {
            ComponentsFile.Types.ComponentRecord.Types.ToeprintRecord? toeprint = null;
            toeprintsByNumber?.TryGetValue(pin.Number, out toeprint);
            var connected = pin.Connection.Kind == Connectivity.Types.Connection.Types.Kind.Connected;

            // Unconnected pins come from the roster with their net unset
            // (Kind.UNCONNECTED absorbs netNumber -1 and $NONE$ membership
            // server-side) — never reconstructed from the net lists.
            var netOrdinal = connected ? pin.Connection.Net : 0;
            string? netName = connected ? NetNameOf(netEntries, pin.Connection.Net) : null;

            pins.Add(new PinDetail(
                pin.Number,
                string.IsNullOrEmpty(pin.Name) ? null : pin.Name,
                (toeprint?.LocationX ?? 0) * placementScale,
                (toeprint?.LocationY ?? 0) * placementScale,
                toeprint?.Rotation ?? 0,
                netOrdinal,
                netName));

            if (connected)
            {
                pinsByNet[netOrdinal] = pinsByNet.TryGetValue(netOrdinal, out var count) ? count + 1 : 1;
            }
        }

        // One summary row per (component, net) — the component can touch the same
        // net through several pins. Ordered by net ordinal, matching the roster.
        var netSummaries = new List<NetSummary>(pinsByNet.Count);
        foreach (var (netOrdinal, pinCount) in pinsByNet.OrderBy(kv => kv.Key))
        {
            netSummaries.Add(new NetSummary(
                NetDisplayName(netEntries, netOrdinal),
                netOrdinal,
                pinCount));
        }

        // Component height is a 3D-rendering concern resolved from BOM/footprint
        // attributes; data viewers do not surface it, so it is reported as
        // unavailable rather than pulling in the render pipeline.
        const float heightMm = 0f;
        const string heightSource = "unavailable";

        var packageName = string.IsNullOrEmpty(placed.Package) ? null : placed.Package;
        return new ComponentDetail(
            placed.RefDes,
            placed.HasId ? placed.Id : 0,
            string.IsNullOrEmpty(record?.PartName) ? null : record!.PartName,
            record == null ? null : ReadValueProperty(record),
            placed.Side,
            (record?.LocationX ?? 0) * placementScale,
            (record?.LocationY ?? 0) * placementScale,
            record?.Rotation ?? 0,
            record?.Mirror ?? false,
            heightMm,
            heightSource,
            packageName,
            packageName != null && packagesByName.TryGetValue(packageName, out var package) ? package : null,
            entry.File == null || record == null ? null : ResolveBom(record, entry.File),
            entry.File == null || record == null
                ? Array.Empty<NameValue>()
                : BuildAttributes(record, entry.File.AttributeNames),
            pins,
            netSummaries);
    }

    /// <summary>
    /// The net-tab rows: one per <c>Connectivity.nets</c> roster entry (dense over
    /// the eda-data NET-record sequence, $NONE$ included with its literal name),
    /// with the CONNECTED edges from <c>netMembers</c> — the same edges as the
    /// per-component pin connections, pre-inverted server-side, so the client
    /// inverts nothing.
    /// </summary>
    private List<NetDetail> BuildNetDetails(
        Connectivity connectivity,
        EdaDataFile? edaData,
        IReadOnlyDictionary<string, BoardSide> sideByRefDes)
    {
        var membersByNet = new Dictionary<uint, Connectivity.Types.NetMembers>();
        foreach (var netMembers in connectivity.NetMembers)
        {
            membersByNet.TryAdd(netMembers.Net, netMembers);
        }

        var nets = new List<NetDetail>(connectivity.Nets.Count);
        foreach (var netEntry in connectivity.Nets)
        {
            var connections = new List<NetConnection>();
            if (membersByNet.TryGetValue(netEntry.Ordinal, out var netMembers))
            {
                foreach (var member in netMembers.Members)
                {
                    var side = sideByRefDes.TryGetValue(member.RefDes, out var memberSide)
                        ? memberSide
                        : BoardSide.BsNone;
                    foreach (var pinNumber in member.Pin)
                    {
                        connections.Add(new NetConnection(member.RefDes, pinNumber, side));
                    }
                }
            }

            // The net ordinal names the net (spec p.153: the NET record sequence),
            // so fileModel facts about the net — its subnet census and its
            // attributes — are addressed by ordinal, not joined by reference.
            EdaDataFile.Types.NetRecord? netRecord = null;
            if (edaData != null && netEntry.Ordinal < (uint)edaData.NetRecords.Count)
            {
                netRecord = edaData.NetRecords[(int)netEntry.Ordinal];
            }

            nets.Add(new NetDetail(
                string.IsNullOrEmpty(netEntry.Name) ? $"#{netEntry.Ordinal}" : netEntry.Name,
                netEntry.Ordinal,
                netRecord?.SubnetRecords.Count ?? 0,
                connections,
                netRecord == null ? Array.Empty<NameValue>() : BuildNetAttributes(netRecord, edaData!.AttributeNames),
                CountSubnets(netRecord, EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Via)));
        }

        return nets;
    }

    private static int CountSubnets(EdaDataFile.Types.NetRecord? net, EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type type)
    {
        var count = 0;
        if (net != null)
        {
            foreach (var subnet in net.SubnetRecords)
            {
                if (subnet.Type == type)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>The roster name of a net, or null when the entry is unnamed.</summary>
    private static string? NetNameOf(IReadOnlyList<Connectivity.Types.NetEntry> netEntries, uint ordinal) =>
        ordinal < (uint)netEntries.Count && !string.IsNullOrEmpty(netEntries[(int)ordinal].Name)
            ? netEntries[(int)ordinal].Name
            : null;

    /// <summary>The roster display name of a net: its name, or "#ordinal" when unnamed.</summary>
    private static string NetDisplayName(IReadOnlyList<Connectivity.Types.NetEntry> netEntries, uint ordinal) =>
        ordinal < (uint)netEntries.Count && !string.IsNullOrEmpty(netEntries[(int)ordinal].Name)
            ? netEntries[(int)ordinal].Name
            : $"#{ordinal}";

    private static void AddRecordsByRefDes(
        ComponentsFile? components,
        Dictionary<string, (ComponentsFile.Types.ComponentRecord, ComponentsFile)> map)
    {
        if (components == null)
        {
            return;
        }

        foreach (var record in components.ComponentRecords)
        {
            if (!string.IsNullOrEmpty(record.CompName))
            {
                map.TryAdd(record.CompName, (record, components));
            }
        }
    }

    private static string? ReadValueProperty(ComponentsFile.Types.ComponentRecord comp)
    {
        foreach (var property in comp.PropertyRecords)
        {
            if (string.Equals(property.Name, "VALUE", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(property.Value))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static List<NameValue> BuildNetAttributes(
        EdaDataFile.Types.NetRecord net,
        IList<string> edaAttributeNames)
    {
        var attributes = new List<NameValue>();
        foreach (var property in net.PropertyRecords)
        {
            attributes.Add(new NameValue(property.Name, property.Value));
        }

        foreach (var lookup in net.AttributeLookupTable)
        {
            var attrName = int.TryParse(lookup.Key, out var index) && index >= 0 && index < edaAttributeNames.Count
                ? edaAttributeNames[index]
                : lookup.Key;
            attributes.Add(new NameValue(attrName, lookup.Value));
        }

        return attributes;
    }

    private static List<NameValue> BuildAttributes(
        ComponentsFile.Types.ComponentRecord comp,
        IList<string> attributeNames)
    {
        var attributes = new List<NameValue>();

        foreach (var lookup in comp.AttributeLookupTable)
        {
            var name = int.TryParse(lookup.Key, out var index) && index >= 0 && index < attributeNames.Count
                ? attributeNames[index]
                : lookup.Key;
            attributes.Add(new NameValue(name, lookup.Value));
        }

        foreach (var property in comp.PropertyRecords)
        {
            attributes.Add(new NameValue(property.Name, property.Value));
        }

        return attributes;
    }

    /// <summary>
    /// Joins the component's BOM/CPN attribute to its BOM description record. ODB++
    /// comps files expose the CPN either as a <c>BOM</c> attribute or a
    /// <c>.cpn</c>/<c>cpn</c> property; anything else leaves BOM unset.
    /// </summary>
    private static BomDetail? ResolveBom(ComponentsFile.Types.ComponentRecord comp, ComponentsFile components)
    {
        if (components.BomDescriptionRecordsByCpn.Count == 0)
        {
            return null;
        }

        string? cpn = null;

        // Attribute lookup table: attribute index (from the file's AN list) → value.
        for (var i = 0; i < components.AttributeNames.Count && cpn == null; i++)
        {
            if (!IsCpnAttributeName(components.AttributeNames[i]))
            {
                continue;
            }

            if (comp.AttributeLookupTable.TryGetValue(i.ToString(), out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                cpn = value;
            }
        }

        if (cpn == null)
        {
            foreach (var property in comp.PropertyRecords)
            {
                if (IsCpnAttributeName(property.Name) && !string.IsNullOrWhiteSpace(property.Value))
                {
                    cpn = property.Value;
                    break;
                }
            }
        }

        if (cpn == null || !components.BomDescriptionRecordsByCpn.TryGetValue(cpn, out var bom))
        {
            return null;
        }

        return new BomDetail(bom.Cpn, bom.Pkg, bom.Ipn, bom.Descriptions.ToList(), bom.Vnd, bom.Mpn);
    }

    private static bool IsCpnAttributeName(string name) =>
        string.Equals(name, "BOM", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, ".cpn", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "cpn", StringComparison.OrdinalIgnoreCase);
}
