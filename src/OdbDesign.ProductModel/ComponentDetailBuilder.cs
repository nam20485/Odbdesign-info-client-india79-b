using Microsoft.Extensions.Logging;
using Odb.Lib.Protobuf;
using OdbDesign.ProductModel.Models;

namespace OdbDesign.ProductModel;

/// <summary>
/// Builds <see cref="ComponentDetail"/> records from the components/EDA-data protobufs of a
/// design's <c>FileModel</c> — no additional server round trips.
///
/// Join notes (verified against the ODB++ structures the server ships):
/// - <c>ComponentRecord.PkgRef</c> is a list index into <c>EdaDataFile.PackageRecords</c>.
/// - Component pin → net: <c>ToeprintRecord.NetNumber</c> resolves against
///   <c>NetRecord.Index</c>, falling back to the record's ordinal position.
/// - Net → component: TOEPRINT <c>SubnetRecord.ComponentNumber</c> resolves against
///   <c>ComponentRecord.Id</c>, falling back to its list index, within the subnet's
///   board side (side-blind fallback when the side field is unset); the subnet's
///   <c>ToeprintNumber</c> indexes the component's toeprint records for the pin number.
/// - BOM: the component's BOM/CPN attribute value keys into
///   <c>bomDescriptionRecordsByCpn</c>.
/// Every join degrades to null/empty when the data does not line up; the caller simply
/// shows less rather than failing the load.
/// </summary>
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
    /// Builds the component/net product-model index for one step from its top and bottom
    /// components files and EDA data.
    /// </summary>
    /// <param name="topComponents">The top-side components file (may be null).</param>
    /// <param name="bottomComponents">The bottom-side components file (may be null).</param>
    /// <param name="edaData">The step's EDA data (nets, packages, attributes).</param>
    /// <returns>The component/net index; <see cref="ComponentDetailIndex.Empty"/> when EDA data is absent.</returns>
    public ComponentDetailIndex Build(
        ComponentsFile? topComponents,
        ComponentsFile? bottomComponents,
        EdaDataFile? edaData)
    {
        if (edaData == null)
        {
            return ComponentDetailIndex.Empty;
        }

        // (side, componentNumber) → component record, resolving Id first then
        // list index. Component numbers are per-side data (each board side has
        // its own components file), so the lookup keys include the side: with
        // both sides merged, an Id present on top and bottom would look
        // ambiguous and the join would degrade for every two-sided board. The
        // side-blind (number-only) views exist for servers that omit the
        // subnet side field — an unset proto3 optional reads as BsNone.
        var byId = new Dictionary<(BoardSide Side, uint Number), List<(ComponentsFile.Types.ComponentRecord Comp, BoardSide Side)>>();
        var byIndex = new Dictionary<(BoardSide Side, uint Number), List<(ComponentsFile.Types.ComponentRecord Comp, BoardSide Side)>>();
        var byIdAnySide = new Dictionary<uint, List<(ComponentsFile.Types.ComponentRecord Comp, BoardSide Side)>>();
        var byIndexAnySide = new Dictionary<uint, List<(ComponentsFile.Types.ComponentRecord Comp, BoardSide Side)>>();
        AddComponentLookups(topComponents, BoardSide.Top, byId, byIndex, byIdAnySide, byIndexAnySide);
        AddComponentLookups(bottomComponents, BoardSide.Bottom, byId, byIndex, byIdAnySide, byIndexAnySide);

        var scale = UnitsHelper.UnitsToMmScale(topComponents?.Units ?? bottomComponents?.Units);

        // One pass over the nets builds both the net details (net tab) and the
        // per-component net summaries (component tab), keeping pin counts exact.
        var netDetails = new List<NetDetail>(edaData.NetRecords.Count);
        var netsByComponent = new Dictionary<(BoardSide Side, uint Id), List<NetSummary>>();
        // Dedupe and pin counts key on the net record's list ordinal — the only
        // stable net identity. Raw names collide across distinct unnamed nets
        // (""), and display names ("#Index"-style fallbacks) never equal the raw
        // name they would be deduped against, so name-keyed bookkeeping either
        // merges distinct nets or never dedupes at all.
        var pinCounts = new Dictionary<(int Net, BoardSide Side, uint Id), int>();
        var summarized = new HashSet<(BoardSide Side, uint Id, int Net)>();

        for (var netOrdinal = 0; netOrdinal < edaData.NetRecords.Count; netOrdinal++)
        {
            var net = edaData.NetRecords[netOrdinal];
            var connections = new List<NetConnection>();
            var connectedKeys = new List<(BoardSide Side, uint Id)>();

            foreach (var subnet in net.SubnetRecords)
            {
                if (subnet.Type != EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Toeprint)
                {
                    continue;
                }

                var comp = ResolveComponent(subnet.ComponentNumber, subnet.Side, byId, byIndex, byIdAnySide, byIndexAnySide);
                if (comp == null)
                {
                    // Unresolvable reference: keep the raw number so the net tab can
                    // still show something instead of silently dropping a connection.
                    connections.Add(new NetConnection(
                        $"#{subnet.ComponentNumber}",
                        ResolvePinNumber(subnet.ToeprintNumber, null),
                        subnet.Side));
                    continue;
                }

                var key = (Side: comp.Value.Side, Id: comp.Value.Comp.Id);
                connections.Add(new NetConnection(
                    ComponentName(comp.Value.Comp),
                    ResolvePinNumber(subnet.ToeprintNumber, comp.Value.Comp),
                    subnet.Side));
                connectedKeys.Add(key);

                var pinKey = (netOrdinal, key.Side, key.Id);
                pinCounts[pinKey] = pinCounts.TryGetValue(pinKey, out var count) ? count + 1 : 1;
            }

            netDetails.Add(new NetDetail(
                string.IsNullOrEmpty(net.Name) ? $"#{net.Index}" : net.Name,
                net.Index,
                net.SubnetRecords.Count,
                connections,
                BuildNetAttributes(net, edaData.AttributeNames)));

            foreach (var key in connectedKeys)
            {
                if (!netsByComponent.TryGetValue(key, out var list))
                {
                    list = new List<NetSummary>();
                    netsByComponent[key] = list;
                }

                // One summary row per (component, net) pair — the component can
                // touch the same net through many toeprints.
                if (summarized.Add((key.Side, key.Id, netOrdinal)))
                {
                    list.Add(new NetSummary(
                        string.IsNullOrEmpty(net.Name) ? $"#{net.Index}" : net.Name,
                        net.Index,
                        pinCounts.TryGetValue((netOrdinal, key.Side, key.Id), out var pins) ? pins : 0));
                }
            }
        }

        var byKey = new Dictionary<(BoardSide, uint), ComponentDetail>();
        var byName = new Dictionary<string, ComponentDetail>(StringComparer.OrdinalIgnoreCase);
        BuildSide(topComponents, BoardSide.Top, edaData, scale, netsByComponent, byKey, byName);
        BuildSide(bottomComponents, BoardSide.Bottom, edaData, scale, netsByComponent, byKey, byName);

        var unresolved = netDetails.Sum(n => n.Connections.Count(c => c.ComponentName.StartsWith('#')));
        _logger.LogDebug(
            "Built component detail index: {Components} components, {Nets} nets, {WithNets} components with net links, {Unresolved} unresolved net references",
            byKey.Count, netDetails.Count, netsByComponent.Count, unresolved);

        return new ComponentDetailIndex(byKey, byName, netDetails);
    }

    private void BuildSide(
        ComponentsFile? components,
        BoardSide side,
        EdaDataFile edaData,
        double scale,
        Dictionary<(BoardSide Side, uint Id), List<NetSummary>> netsByComponent,
        Dictionary<(BoardSide, uint), ComponentDetail> byKey,
        Dictionary<string, ComponentDetail> byName)
    {
        if (components == null)
        {
            return;
        }

        var netNamesByNumber = BuildNetNameLookup(edaData);

        foreach (var comp in components.ComponentRecords)
        {
            var package = ResolvePackage(comp, edaData, scale, out var pkgName);
            var pins = BuildPins(comp, netNamesByNumber, scale);
            netsByComponent.TryGetValue((side, comp.Id), out var nets);

            // Component height is a 3D-rendering concern resolved from BOM/footprint
            // attributes; data viewers do not surface it, so it is reported as
            // unavailable rather than pulling in the render pipeline.
            const float heightMm = 0f;
            const string heightSource = "unavailable";

            var name = ComponentName(comp);
            var detail = new ComponentDetail(
                name,
                comp.Id,
                string.IsNullOrEmpty(comp.PartName) ? null : comp.PartName,
                ReadValueProperty(comp),
                side,
                comp.LocationX * scale,
                comp.LocationY * scale,
                comp.Rotation,
                comp.Mirror,
                heightMm,
                heightSource,
                pkgName,
                package,
                ResolveBom(comp, components),
                BuildAttributes(comp, components.AttributeNames),
                pins,
                (IReadOnlyList<NetSummary>?)nets ?? Array.Empty<NetSummary>());

            byKey[(side, comp.Id)] = detail;
            byName[name] = detail;
        }
    }

    private static string ComponentName(ComponentsFile.Types.ComponentRecord comp) =>
        string.IsNullOrEmpty(comp.CompName) ? $"#{comp.Id}" : comp.CompName;

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

    private static PackageDetail? ResolvePackage(
        ComponentsFile.Types.ComponentRecord comp,
        EdaDataFile edaData,
        double scale,
        out string? pkgName)
    {
        if (comp.PkgRef >= edaData.PackageRecords.Count)
        {
            pkgName = null;
            return null;
        }

        var pkg = edaData.PackageRecords[(int)comp.PkgRef];
        pkgName = pkg.Name;
        return new PackageDetail(
            pkg.Name,
            pkg.Pitch > 0 ? pkg.Pitch * scale : null,
            pkg.XMin * scale,
            pkg.YMin * scale,
            pkg.XMax * scale,
            pkg.YMax * scale,
            pkg.PinRecords.Count,
            pkg.OutlineRecords.Count);
    }

    /// <summary>
    /// Net-number → name lookup for toeprint resolution. Toeprint numbers are
    /// resolved against two overlapping number spaces — a net record's populated
    /// <c>Index</c> field, or its ordinal position in the net list — so both are
    /// claimed here, in a defined order: every named net first claims its
    /// ordinal, then a second pass claims its <c>Index</c> only where that key
    /// is still free. Ordinal precedence removes the list-order dependence of a
    /// single pass: visited inline, a net whose populated <c>Index</c> equals a
    /// later net's ordinal would steal that ordinal (first-wins) and misname
    /// the later net's toeprints. Unnamed nets claim nothing; first-wins on
    /// remaining collisions (two unset Index values both read 0): overwriting
    /// the earlier claim would silently re-point every toeprint of the first
    /// net at the second one.
    /// </summary>
    private static Dictionary<uint, string> BuildNetNameLookup(EdaDataFile edaData)
    {
        var lookup = new Dictionary<uint, string>();
        for (var i = 0; i < edaData.NetRecords.Count; i++)
        {
            if (string.IsNullOrEmpty(edaData.NetRecords[i].Name))
            {
                continue;
            }

            lookup.TryAdd((uint)i, edaData.NetRecords[i].Name);
        }

        for (var i = 0; i < edaData.NetRecords.Count; i++)
        {
            var net = edaData.NetRecords[i];
            if (string.IsNullOrEmpty(net.Name))
            {
                continue;
            }

            lookup.TryAdd(net.Index, net.Name);
        }

        return lookup;
    }

    /// <summary>
    /// Pin rows for the inspector. Toeprint locations are component-local —
    /// relative to the component origin, before the placement transform — and
    /// are presented as such, not run through the render path's mirror → rotate →
    /// translate to board space.
    /// </summary>
    private static List<PinDetail> BuildPins(
        ComponentsFile.Types.ComponentRecord comp,
        Dictionary<uint, string> netNamesByNumber,
        double scale)
    {
        var pins = new List<PinDetail>(comp.ToeprintRecords.Count);
        foreach (var toeprint in comp.ToeprintRecords)
        {
            pins.Add(new PinDetail(
                toeprint.PinNumber,
                string.IsNullOrEmpty(toeprint.Name) ? null : toeprint.Name,
                toeprint.LocationX * scale,
                toeprint.LocationY * scale,
                toeprint.Rotation,
                toeprint.NetNumber,
                netNamesByNumber.TryGetValue(toeprint.NetNumber, out var netName) ? netName : null));
        }

        return pins;
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

    private static void AddComponentLookups(
        ComponentsFile? components,
        BoardSide side,
        Dictionary<(BoardSide Side, uint Number), List<(ComponentsFile.Types.ComponentRecord, BoardSide)>> byId,
        Dictionary<(BoardSide Side, uint Number), List<(ComponentsFile.Types.ComponentRecord, BoardSide)>> byIndex,
        Dictionary<uint, List<(ComponentsFile.Types.ComponentRecord, BoardSide)>> byIdAnySide,
        Dictionary<uint, List<(ComponentsFile.Types.ComponentRecord, BoardSide)>> byIndexAnySide)
    {
        if (components == null)
        {
            return;
        }

        for (var i = 0; i < components.ComponentRecords.Count; i++)
        {
            var comp = components.ComponentRecords[i];
            AddLookup(byId, (side, comp.Id), comp, side);
            AddLookup(byIndex, (side, (uint)i), comp, side);
            AddLookupAnySide(byIdAnySide, comp.Id, comp, side);
            AddLookupAnySide(byIndexAnySide, (uint)i, comp, side);
        }
    }

    private static void AddLookup(
        Dictionary<(BoardSide Side, uint Number), List<(ComponentsFile.Types.ComponentRecord, BoardSide)>> map,
        (BoardSide Side, uint Number) key,
        ComponentsFile.Types.ComponentRecord comp,
        BoardSide side)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<(ComponentsFile.Types.ComponentRecord, BoardSide)>();
            map[key] = list;
        }

        list.Add((comp, side));
    }

    private static void AddLookupAnySide(
        Dictionary<uint, List<(ComponentsFile.Types.ComponentRecord, BoardSide)>> map,
        uint key,
        ComponentsFile.Types.ComponentRecord comp,
        BoardSide side)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<(ComponentsFile.Types.ComponentRecord, BoardSide)>();
            map[key] = list;
        }

        list.Add((comp, side));
    }

    /// <summary>
    /// Resolves an EDA-data component number to a component record on the subnet's
    /// board side (component numbers are per-side), Id first then list index. A
    /// reference resolves to nothing only when both the Id and ordinal lookups
    /// fail or are ambiguous — the Id lookup falling through to a unique ordinal
    /// match is intended, since guessing beyond that would silently mislabel the
    /// net tab's connections. A subnet whose side field is unset (proto3 optional
    /// reads as <see cref="BoardSide.BsNone"/>, e.g. a server predating the field)
    /// falls back to the side-blind number-only view, resolving only when unique
    /// across both sides.
    /// </summary>
    private static (ComponentsFile.Types.ComponentRecord Comp, BoardSide Side)? ResolveComponent(
        uint componentNumber,
        BoardSide side,
        Dictionary<(BoardSide Side, uint Number), List<(ComponentsFile.Types.ComponentRecord, BoardSide)>> byId,
        Dictionary<(BoardSide Side, uint Number), List<(ComponentsFile.Types.ComponentRecord, BoardSide)>> byIndex,
        Dictionary<uint, List<(ComponentsFile.Types.ComponentRecord, BoardSide)>> byIdAnySide,
        Dictionary<uint, List<(ComponentsFile.Types.ComponentRecord, BoardSide)>> byIndexAnySide)
    {
        if (byId.TryGetValue((side, componentNumber), out var byIdList) && byIdList.Count == 1)
        {
            return byIdList[0];
        }

        if (byIndex.TryGetValue((side, componentNumber), out var byIndexList) && byIndexList.Count == 1)
        {
            return byIndexList[0];
        }

        if (side == BoardSide.BsNone)
        {
            if (byIdAnySide.TryGetValue(componentNumber, out var anyIdList) && anyIdList.Count == 1)
            {
                return anyIdList[0];
            }

            if (byIndexAnySide.TryGetValue(componentNumber, out var anyIndexList) && anyIndexList.Count == 1)
            {
                return anyIndexList[0];
            }
        }

        return null;
    }

    /// <summary>
    /// The subnet's toeprint number indexes the component's toeprint records when
    /// in range (the record's own pin number is authoritative); otherwise the
    /// number itself is the pin number.
    /// </summary>
    private static uint ResolvePinNumber(uint toeprintNumber, ComponentsFile.Types.ComponentRecord? comp)
    {
        if (comp != null && toeprintNumber < (uint)comp.ToeprintRecords.Count)
        {
            return comp.ToeprintRecords[(int)toeprintNumber].PinNumber;
        }

        return toeprintNumber;
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
