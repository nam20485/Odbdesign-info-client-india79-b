using Microsoft.Extensions.Logging;
using Odb.Lib.Protobuf;
using Odb.Lib.Protobuf.ProductModel;
using OdbDesign.ProductModel.Models;

namespace OdbDesign.ProductModel;

/// <summary>A distinct part definition with how many placed components use it.</summary>
/// <param name="Name">The part name/number from the library.</param>
/// <param name="UsageCount">How many placed components reference this part.</param>
public sealed record PartInfo(string Name, int UsageCount);

/// <summary>A drill tool definition read from a step's tools file.</summary>
/// <param name="ToolNumber">The tool number.</param>
/// <param name="DrillSizeMm">The drill bit size in millimeters.</param>
/// <param name="FinishSizeMm">The finished hole size in millimeters.</param>
/// <param name="ToolType">The tool type name (Plated, NonPlated, Via).</param>
/// <param name="IsPlated">Whether the tool drills plated holes.</param>
/// <param name="Units">The raw unit name from the tools file header.</param>
public sealed record DrillToolInfo(
    int ToolNumber,
    double DrillSizeMm,
    double FinishSizeMm,
    string ToolType,
    bool IsPlated,
    string Units);

/// <summary>
/// The complete product model for one design/step, derived from a single gRPC
/// <see cref="Design"/> (file-archive) response. Mirrors how the 3D client builds its
/// component/net/pin graph: everything is read from <c>Design.FileModel</c> (EDA data +
/// per-layer components/tools files), so no normalized lists or extra REST calls are needed.
/// </summary>
/// <param name="Components">Components joined with their pins and net summaries.</param>
/// <param name="Nets">Nets joined with their toeprint connections.</param>
/// <param name="Packages">Distinct package (footprint) definitions.</param>
/// <param name="Parts">Distinct part definitions with usage counts.</param>
/// <param name="DrillTools">Drill tool definitions from the step's tools files.</param>
public sealed record DesignProductModel(
    IReadOnlyList<ComponentDetail> Components,
    IReadOnlyList<NetDetail> Nets,
    IReadOnlyList<PackageDetail> Packages,
    IReadOnlyList<PartInfo> Parts,
    IReadOnlyList<DrillToolInfo> DrillTools)
{
    /// <summary>An empty model used when the design or step has no file data.</summary>
    public static readonly DesignProductModel Empty = new(
        Array.Empty<ComponentDetail>(),
        Array.Empty<NetDetail>(),
        Array.Empty<PackageDetail>(),
        Array.Empty<PartInfo>(),
        Array.Empty<DrillToolInfo>());
}

/// <summary>
/// Reads the ODB++ product model out of a gRPC <see cref="Design"/> response's
/// <c>FileModel</c>. This is the shared entry point that both the info client and (later)
/// the 3D client can use to avoid re-implementing the EDA-data joins.
/// </summary>
public class ProductModelReader
{
    private readonly ComponentDetailBuilder _builder;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductModelReader"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public ProductModelReader(ILogger<ProductModelReader> logger)
    {
        _builder = new ComponentDetailBuilder(
            logger as ILogger<ComponentDetailBuilder>
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ComponentDetailBuilder>.Instance);
    }

    /// <summary>
    /// Builds the full product model for one step of a design.
    /// </summary>
    /// <param name="design">The gRPC design response (its <c>FileModel</c> is read).</param>
    /// <param name="stepName">The step to read (case-sensitive server key).</param>
    /// <returns>The product model; <see cref="DesignProductModel.Empty"/> when data is absent.</returns>
    public DesignProductModel Read(Design? design, string stepName)
    {
        if (design?.FileModel is null ||
            !design.FileModel.StepsByName.TryGetValue(stepName, out var stepDir))
        {
            return DesignProductModel.Empty;
        }

        var topComponents = GetComponentsFile(stepDir, BoardSide.Top);
        var bottomComponents = GetComponentsFile(stepDir, BoardSide.Bottom);
        var edaData = stepDir.Edadatafile;

        var scale = UnitsHelper.UnitsToMmScale(topComponents?.Units ?? bottomComponents?.Units);

        // Components + nets come from the shared builder (joins toeprints → nets).
        // ByKey is the complete component list: it keys every record on
        // (side, per-side ordinal) — the canonical component identity that the
        // eda_data netlist joins on — so it holds exactly one entry per placed
        // component, in stable side/ordinal order. refDes (ByName) is unique too,
        // but keys display names.
        var index = _builder.Build(topComponents, bottomComponents, edaData);
        var components = index.ByKey.Values.ToList();


        var packages = BuildPackages(edaData, scale);
        var parts = BuildParts(components);
        var drillTools = BuildDrillTools(stepDir);
        var nets = ApplyViaCounts(index.Nets, edaData);

        return new DesignProductModel(components, nets, packages, parts, drillTools);
    }

    /// <summary>
    /// Projects per-net VIA subnet counts onto the builder's net details. The builder's
    /// existing toeprint join is untouched: this additive pass walks the same
    /// <see cref="EdaDataFile.NetRecords"/> list positionally (the builder emits exactly
    /// one <see cref="NetDetail"/> per record, in order) and counts VIA subnets into the
    /// additive <see cref="NetDetail.ViaCount"/> field. Nets without VIA subnets keep
    /// their original instance.
    /// </summary>
    private static IReadOnlyList<NetDetail> ApplyViaCounts(IReadOnlyList<NetDetail> nets, EdaDataFile? edaData)
    {
        if (edaData is null || nets.Count == 0)
        {
            return nets;
        }

        var result = new List<NetDetail>(nets.Count);
        for (var i = 0; i < nets.Count && i < edaData.NetRecords.Count; i++)
        {
            var viaCount = 0;
            foreach (var subnet in edaData.NetRecords[i].SubnetRecords)
            {
                if (subnet.Type == EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Via)
                {
                    viaCount++;
                }
            }

            result.Add(viaCount > 0 ? nets[i] with { ViaCount = viaCount } : nets[i]);
        }

        return result;
    }

    /// <summary>
    /// Gets the components file for a board side. The server keys component layers
    /// "comp_+_top" / "comp_+_bot"; falls back to a case-insensitive name match.
    /// </summary>
    private static ComponentsFile? GetComponentsFile(StepDirectory stepDir, BoardSide side)
    {
        var preferred = side == BoardSide.Top ? "comp_+_top" : "comp_+_bot";
        if (stepDir.LayersByName.TryGetValue(preferred, out var layer) && layer.Components is not null)
        {
            return layer.Components;
        }

        foreach (var entry in stepDir.LayersByName)
        {
            if (string.Equals(entry.Key, preferred, StringComparison.OrdinalIgnoreCase) &&
                entry.Value.Components is not null)
            {
                return entry.Value.Components;
            }
        }

        return null;
    }

    private static List<PackageDetail> BuildPackages(EdaDataFile? edaData, float scale)
    {
        if (edaData is null)
        {
            return [];
        }

        var packages = new List<PackageDetail>(edaData.PackageRecords.Count);
        foreach (var pkg in edaData.PackageRecords)
        {
            packages.Add(new PackageDetail(
                pkg.Name,
                pkg.Pitch > 0 ? pkg.Pitch * scale : null,
                pkg.XMin * scale,
                pkg.YMin * scale,
                pkg.XMax * scale,
                pkg.YMax * scale,
                pkg.PinRecords.Count,
                pkg.OutlineRecords.Count));
        }

        return packages;
    }

    private static List<PartInfo> BuildParts(IReadOnlyList<ComponentDetail> components)
    {
        return components
            .Where(c => !string.IsNullOrEmpty(c.PartName))
            .GroupBy(c => c.PartName!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PartInfo(g.Key, g.Count()))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<DrillToolInfo> BuildDrillTools(StepDirectory stepDir)
    {
        var tools = new List<DrillToolInfo>();
        foreach (var layer in stepDir.LayersByName.Values)
        {
            var toolsFile = layer.ToolFile;
            if (toolsFile is null)
            {
                continue;
            }

            var scale = UnitsHelper.UnitsToMmScale(toolsFile.Units);
            foreach (var record in toolsFile.Tools.Values)
            {
                var type = record.Type;
                tools.Add(new DrillToolInfo(
                    (int)record.ToolNum,
                    record.DrillSize * scale,
                    record.FinishSize * scale,
                    type.ToString(),
                    type == ToolsFile.Types.ToolsRecord.Types.Type.Plated,
                    toolsFile.Units ?? string.Empty));
            }
        }

        return tools.OrderBy(t => t.ToolNumber).ToList();
    }
}
