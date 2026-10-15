using Odb.Lib.Protobuf;

namespace OdbDesign.ProductModel.Models;

/// <summary>Generic name/value pair as shown in inspector lists (attributes, properties).</summary>
public sealed record NameValue(string Name, string? Value);

/// <summary>
/// Everything a client can show about one placed component, resolved from the
/// <c>ComponentsFile</c>/<c>EdaDataFile</c> protobufs at load time (no extra server round
/// trips). Positions are converted to millimeters using the components file's units.
/// </summary>
public sealed record ComponentDetail(
    string Name,
    uint Id,
    string? PartName,
    string? Value,
    BoardSide Side,
    double PositionX,
    double PositionY,
    double Rotation,
    bool Mirror,
    float HeightMm,
    string HeightSource,
    string? PkgRef,
    PackageDetail? Package,
    BomDetail? Bom,
    IReadOnlyList<NameValue> Attributes,
    IReadOnlyList<PinDetail> Pins,
    IReadOnlyList<NetSummary> Nets);

/// <summary>Package footprint summary from the EDA data's package records.</summary>
public sealed record PackageDetail(
    string Name,
    double? Pitch,
    double XMin,
    double YMin,
    double XMax,
    double YMax,
    int PinCount,
    int OutlineCount);

/// <summary>One placed pin of a component with its resolved net, when known.</summary>
public sealed record PinDetail(
    uint PinNumber,
    string? Name,
    double X,
    double Y,
    double Rotation,
    uint NetNumber,
    string? NetName);

/// <summary>A net attached to a component, as listed on the component inspector tab.</summary>
public sealed record NetSummary(
    string Name,
    uint Index,
    int PinCount);

/// <summary>Full net information shown on the net inspector tab.</summary>
/// <param name="Name">The net's display name (raw name, "#Index" fallback for unnamed nets).</param>
/// <param name="Index">The net record's index field.</param>
/// <param name="SubnetCount">Total number of subnet records on the net (all types).</param>
/// <param name="Connections">The toeprint connections (component pins) of the net.</param>
/// <param name="Attributes">The net record's resolved attributes.</param>
/// <param name="ViaCount">
/// Number of VIA subnets on the net record. Additive field (defaults to 0): it is
/// projected onto the builder's output by <see cref="ProductModelReader"/> from the
/// same EDA net records, without touching the toeprint join.
/// </param>
public sealed record NetDetail(
    string Name,
    uint Index,
    int SubnetCount,
    IReadOnlyList<NetConnection> Connections,
    IReadOnlyList<NameValue> Attributes,
    int ViaCount = 0);

/// <summary>One toeprint connection of a net: a component pin on a board side.</summary>
public sealed record NetConnection(
    string ComponentName,
    uint PinNumber,
    BoardSide Side);

/// <summary>BOM description fields joined via the component's BOM/CPN attribute.</summary>
public sealed record BomDetail(
    string? Cpn,
    string? Pkg,
    string? Ipn,
    IReadOnlyList<string> Descriptions,
    string? Vnd,
    string? Mpn);
