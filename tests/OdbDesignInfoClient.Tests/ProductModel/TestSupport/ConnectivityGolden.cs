using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Tests.ProductModel.TestSupport;

/// <summary>
/// The golden-file view of the fixture design: server-derived ground truth for the
/// refDes → (pin, net) relationship, generated in the OdbDesign repo by
/// <c>scripts/gen-connectivity-golden.py</c> straight from the raw ODB++ component
/// files (merged via OdbDesign PR #595; see TestData/Connectivity/README.md). Field
/// semantics: a component's <see cref="ConnectivityGoldenComponent.Index"/> is the
/// 0-based CMP-record ordinal within its own board side's file; a pin's
/// <see cref="ConnectivityGoldenPin.Ordinal"/> is its TOP-record ordinal;
/// <see cref="ConnectivityGoldenPin.NetOrdinal"/> is the TOP record's
/// <c>net_num</c> — an eda/data NET-record ordinal — with <c>null</c> meaning
/// unconnected. The golden predates the Connectivity contract and speaks the raw
/// format's terms; the contract (component-connectivity.md §6) absorbs two of them
/// server-side, and the harness translates accordingly: a <c>null</c> netOrdinal
/// (the -1 sentinel in an unsigned field) is <c>Kind.UNCONNECTED</c>, and so is
/// membership of the net named <c>$NONE$</c> — the magic net name the server maps
/// to a Kind instead of leaking it as a real net.
/// </summary>
public sealed record ConnectivityGolden(
    [property: JsonPropertyName("design")] string Design,
    [property: JsonPropertyName("step")] string Step,
    [property: JsonPropertyName("fidelity")] string Fidelity,
    [property: JsonPropertyName("aggregates")] ConnectivityGoldenAggregates Aggregates,
    [property: JsonPropertyName("nets")] List<ConnectivityGoldenNet> Nets,
    [property: JsonPropertyName("components")] List<ConnectivityGoldenComponent> Components,
    [property: JsonPropertyName("nets_included")] int? NetsIncluded)
{
    /// <summary>"full": every component and net is listed; "sampled": aggregates plus a deterministic subset.</summary>
    public bool IsFullFidelity => Fidelity == "full";

    /// <summary>Net ordinals below this value have a golden name; above it, only ordinals are pinned.</summary>
    public int NetNameCoverage => NetsIncluded ?? Nets.Count;
}

/// <summary>Whole-design counts pinned by the golden, independent of fidelity.</summary>
public sealed record ConnectivityGoldenAggregates(
    [property: JsonPropertyName("components_total")] int ComponentsTotal,
    [property: JsonPropertyName("components_by_side")] Dictionary<string, int> ComponentsBySide,
    [property: JsonPropertyName("pins_total")] int PinsTotal,
    [property: JsonPropertyName("pins_connected")] int PinsConnected,
    [property: JsonPropertyName("pins_unconnected")] int PinsUnconnected,
    [property: JsonPropertyName("nets_total")] int NetsTotal,
    [property: JsonPropertyName("uids_present")] int UidsPresent,
    [property: JsonPropertyName("ordinal_uid_collisions")] int OrdinalUidCollisions);

/// <summary>One net row: identity is the 0-based NET-record ordinal; the name may be empty.</summary>
public sealed record ConnectivityGoldenNet(
    [property: JsonPropertyName("ordinal")] int Ordinal,
    [property: JsonPropertyName("name")] string Name);

/// <summary>One component with its pin rows, identified by board side and per-side ordinal.</summary>
public sealed record ConnectivityGoldenComponent(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("refDes")] string RefDes,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("uid")] int? Uid,
    [property: JsonPropertyName("pins")] List<ConnectivityGoldenPin> Pins);

/// <summary>One pin row: toeprint ordinal/number and the connected net's ordinal (null = unconnected).</summary>
public sealed record ConnectivityGoldenPin(
    [property: JsonPropertyName("ordinal")] int Ordinal,
    [property: JsonPropertyName("pinNumber")] uint PinNumber,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("netOrdinal")] int? NetOrdinal,
    [property: JsonPropertyName("subnetOrdinal")] int? SubnetOrdinal);

/// <summary>The committed capture manifest accompanying the .binpb fixtures.</summary>
public sealed record ConnectivityCaptureManifest(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("designName")] string DesignName,
    [property: JsonPropertyName("step")] string Step,
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("capturedUtc")] string CapturedUtc,
    [property: JsonPropertyName("sha256TopComponents")] string Sha256TopComponents,
    [property: JsonPropertyName("sha256BottomComponents")] string Sha256BottomComponents,
    [property: JsonPropertyName("sha256EdaData")] string Sha256EdaData,
    [property: JsonPropertyName("sha256Connectivity")] string? Sha256Connectivity);
