using Odb.Lib.Protobuf;
using OdbDesign.ProductModel.Models;

namespace OdbDesign.ProductModel;

/// <summary>
/// Result of building the product model for one design/step: the per-component detail
/// lookup plus the full net list. Populated from the already-fetched components/EDA-data
/// protobufs — no additional server round trips.
/// </summary>
/// <param name="ByKey">
/// Components keyed on (board side, per-side ordinal) — the canonical netlist foreign
/// key (eda_data <c>SubnetRecord.ComponentNumber</c>); one entry per component record.
/// Proto <c>ComponentRecord.id</c> is never populated and is not a key.
/// </param>
/// <param name="ByName">Components keyed on refDes (<c>CompName</c>), which is unique.</param>
/// <param name="Nets">The full net list with toeprint connections.</param>
public sealed record ComponentDetailIndex(
    IReadOnlyDictionary<(BoardSide Side, uint Index), ComponentDetail> ByKey,
    IReadOnlyDictionary<string, ComponentDetail> ByName,
    IReadOnlyList<NetDetail> Nets)
{
    /// <summary>An empty index used when EDA data is absent.</summary>
    public static readonly ComponentDetailIndex Empty = new(
        new Dictionary<(BoardSide Side, uint Index), ComponentDetail>(),
        new Dictionary<string, ComponentDetail>(StringComparer.Ordinal),
        Array.Empty<NetDetail>());

    /// <summary>Name-keyed net lookup (duplicate-tolerant: first wins).</summary>
    public IReadOnlyDictionary<string, NetDetail> NetsByName { get; } = BuildNetNameIndex(Nets);

    /// <summary>
    /// Duplicate-tolerant name index (first wins): unnamed nets collapse to
    /// "#0"-style fallback names, and a throwing lookup here would abort the
    /// load for the whole design, against the builder's degrade-never-fail
    /// contract.
    /// </summary>
    private static Dictionary<string, NetDetail> BuildNetNameIndex(IReadOnlyList<NetDetail> nets)
    {
        var index = new Dictionary<string, NetDetail>(StringComparer.Ordinal);
        foreach (var net in nets)
        {
            index.TryAdd(net.Name, net);
        }

        return index;
    }

    /// <summary>Finds a component detail by refDes name, or null.</summary>
    public ComponentDetail? FindByName(string name) =>
        ByName.TryGetValue(name, out var detail) ? detail : null;
}
