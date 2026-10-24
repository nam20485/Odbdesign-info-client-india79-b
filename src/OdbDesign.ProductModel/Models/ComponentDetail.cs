using Odb.Lib.Protobuf;

namespace OdbDesign.ProductModel.Models;

/// <summary>Generic name/value pair as shown in inspector lists (attributes, properties).</summary>
public sealed record NameValue(string Name, string? Value);

/// <summary>
/// Everything a client can show about one placed component: identity, pins, and
/// net relationships from the server-owned <c>Connectivity</c> contract; placement
/// coordinates (converted to millimeters using the components file's units),
/// attributes, and BOM data from the <c>FileModel</c> (no extra server round
/// trips).
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

/// <summary>
/// One placed pin of a component: identity (number, name) and net connection from
/// the <c>Connectivity</c> roster; component-local toeprint geometry (relative to
/// the component origin, before the placement transform), attached by pin-number
/// identity, when the fileModel carries it.
/// </summary>
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
/// <param name="Name">The net's display name (roster name, "#ordinal" fallback for unnamed nets).</param>
/// <param name="Index">The net's ordinal in the <c>Connectivity.nets</c> roster — the net's identity.</param>
/// <param name="SubnetCount">Total number of subnet records on the net's eda-data NET record (all types).</param>
/// <param name="Connections">The net's member connections (component pins), from <c>netMembers</c>.</param>
/// <param name="Attributes">The net record's resolved attributes.</param>
/// <param name="ViaCount">Number of VIA subnets on the net's eda-data NET record.</param>
public sealed record NetDetail(
    string Name,
    uint Index,
    int SubnetCount,
    IReadOnlyList<NetConnection> Connections,
    IReadOnlyList<NameValue> Attributes,
    int ViaCount = 0);

/// <summary>One member connection of a net: a component pin on a board side.</summary>
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
