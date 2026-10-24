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
/// <see cref="Design"/> (file-archive) response. Relationships (pin rosters,
/// pin→net connections, net members) are read from the server-owned
/// <c>Design.connectivity</c> contract; geometry, placement, and attributes come
/// from <c>Design.FileModel</c> (EDA data + per-layer components/tools files), so
/// no normalized lists or extra REST calls are needed — and no client-side join.
/// </summary>
/// <param name="Components">Placed components with their server-published pin rosters and net summaries.</param>
/// <param name="Nets">Nets with their member connections (from <c>netMembers</c>).</param>
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
/// Reads the ODB++ product model out of a gRPC <see cref="Design"/> response:
/// relationships from its always-on <c>connectivity</c> block, geometry and
/// attributes from its <c>FileModel</c>. This is the shared entry point both
/// clients use against the server's connectivity contract.
/// </summary>
public class ProductModelReader
{
    private readonly ComponentDetailBuilder _builder;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductModelReader"/> class.
    /// </summary>
    /// <param name="loggerFactory">Factory for this reader's and the builder's loggers.</param>
    public ProductModelReader(ILoggerFactory loggerFactory)
    {
        // ILogger<T> categories are not interchangeable: a cast between closed
        // generic logger types always fails, so create each category from the factory.
        _builder = new ComponentDetailBuilder(loggerFactory.CreateLogger<ComponentDetailBuilder>());
    }

    /// <summary>
    /// Builds the full product model for one step of a design.
    /// </summary>
    /// <param name="design">The gRPC design response (its <c>FileModel</c> is read).</param>
    /// <param name="stepName">The step to read (case-sensitive server key).</param>
    /// <returns>
    /// The product model; <see cref="DesignProductModel.Empty"/> when data is absent —
    /// including when the response carries no <c>connectivity</c> block (a server
    /// predating the contract): the content tabs degrade honestly to empty rather
    /// than half-serving a joined view the client no longer has.
    /// </returns>
    public DesignProductModel Read(Design? design, string stepName)
    {
        if (design?.FileModel is null ||
            design.Connectivity is null ||
            !design.FileModel.StepsByName.TryGetValue(stepName, out var stepDir))
        {
            return DesignProductModel.Empty;
        }

        var topComponents = GetComponentsFile(stepDir, BoardSide.Top);
        var bottomComponents = GetComponentsFile(stepDir, BoardSide.Bottom);
        var edaData = stepDir.Edadatafile;

        // Components + nets come from the server-owned Connectivity contract: the
        // builder only groups and indexes what Connectivity publishes (pin
        // rosters, connections, netMembers), enriching rows with fileModel
        // placement/attribute/BOM data keyed on published identity fields. The
        // component list is every placed component, in server order.
        var index = _builder.Build(topComponents, bottomComponents, edaData, design.Connectivity);
        var components = index.Components.ToList();

        var packages = BuildPackages(edaData, UnitsHelper.UnitsToMmScale(edaData?.Units));
        var parts = BuildParts(components);
        var drillTools = BuildDrillTools(stepDir);
        var nets = index.Nets;

        return new DesignProductModel(components, nets, packages, parts, drillTools);
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
                    // Via tools drill plated via holes: only NonPlated is unplated.
                    type != ToolsFile.Types.ToolsRecord.Types.Type.NonPlated,
                    toolsFile.Units ?? string.Empty));
            }
        }

        return tools.OrderBy(t => t.ToolNumber).ToList();
    }
}
