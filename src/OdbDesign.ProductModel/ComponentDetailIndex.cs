using Odb.Lib.Protobuf;
using OdbDesign.ProductModel.Models;

namespace OdbDesign.ProductModel;

/// <summary>
/// Result of building the product model for one design/step: the per-component detail
/// lookup plus the full net list. Populated from the already-fetched components/EDA-data
/// protobufs — no additional server round trips.
/// </summary>
public sealed record ComponentDetailIndex(
    IReadOnlyDictionary<(BoardSide Side, uint Id), ComponentDetail> ByKey,
    IReadOnlyDictionary<string, ComponentDetail> ByName,
    IReadOnlyList<NetDetail> Nets)
{
    /// <summary>An empty index used when EDA data is absent.</summary>
    public static readonly ComponentDetailIndex Empty = new(
        new Dictionary<(BoardSide, uint), ComponentDetail>(),
        new Dictionary<string, ComponentDetail>(StringComparer.OrdinalIgnoreCase),
        Array.Empty<NetDetail>());

    /// <summary>Name-keyed net lookup (duplicate-tolerant: first wins).</summary>
    public IReadOnlyDictionary<string, NetDetail> NetsByName { get; } = BuildNetNameIndex(Nets);

    /// <summary>
    /// Duplicate-tolerant name index (first wins): net names can collide — unnamed
    /// nets collapse to "#0"-style fallback names, and OrdinalIgnoreCase folds case
    /// variants — and a throwing lookup here would abort the load for the whole design,
    /// against the builder's degrade-never-fail contract.
    /// </summary>
    private static Dictionary<string, NetDetail> BuildNetNameIndex(IReadOnlyList<NetDetail> nets)
    {
        var index = new Dictionary<string, NetDetail>(StringComparer.OrdinalIgnoreCase);
        foreach (var net in nets)
        {
            index.TryAdd(net.Name, net);
        }

        return index;
    }

    /// <summary>Finds a component detail by name (case-insensitive), or null.</summary>
    public ComponentDetail? FindByName(string name) =>
        ByName.TryGetValue(name, out var detail) ? detail : null;
}
