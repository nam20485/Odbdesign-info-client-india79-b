using Odb.Lib.Protobuf;
using OdbDesign.ProductModel.Models;

namespace OdbDesign.ProductModel;

/// <summary>
/// Result of building the product model for one design/step from the server-owned
/// <c>Connectivity</c> contract: the per-component detail lookup plus the full net
/// list. Built from the single gRPC <c>GetDesign</c> response — no additional server
/// round trips, and no client-side join (the pin→net truth is the server's; the
/// client only groups and indexes what the contract publishes).
/// </summary>
/// <param name="Components">
/// Every placed component of the design, one row per <c>Connectivity.components</c>
/// entry, in server order.
/// </param>
/// <param name="ByRefDes">
/// Components keyed on refDes (<c>Connectivity.components[].refDes</c> — the
/// authoritative component key, spec p.152). The comparer is
/// <see cref="StringComparer.Ordinal"/>: refDes case is significant, and a
/// case-insensitive index would silently merge <c>R1</c>/<c>r1</c> on case-differing
/// designs. Case-insensitive matching belongs to UI search filtering only
/// (component-connectivity.md §8 hazard 3).
/// </param>
/// <param name="Nets">The full net list, one row per <c>Connectivity.nets</c> entry, with its members from <c>netMembers</c>.</param>
public sealed record ComponentDetailIndex(
    IReadOnlyList<ComponentDetail> Components,
    IReadOnlyDictionary<string, ComponentDetail> ByRefDes,
    IReadOnlyList<NetDetail> Nets)
{
    /// <summary>An empty index used when the server carries no Connectivity block.</summary>
    public static readonly ComponentDetailIndex Empty = new(
        Array.Empty<ComponentDetail>(),
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

    /// <summary>
    /// Finds a component detail by refDes (exact, case-sensitive match against the
    /// server's refDes — the authoritative key), or null.
    /// </summary>
    public ComponentDetail? FindByRefDes(string refDes) =>
        ByRefDes.TryGetValue(refDes, out var detail) ? detail : null;
}
