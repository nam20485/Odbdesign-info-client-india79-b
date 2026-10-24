using System.Security.Cryptography;
using System.Text.Json;
using Odb.Lib.Protobuf;
using Odb.Lib.Protobuf.ProductModel;
using OdbDesign.ProductModel;
using OdbDesignInfoClient.Tests.ProductModel.TestSupport;
using Xunit;

namespace OdbDesignInfoClient.Tests.ProductModel;

/// <summary>
/// M3.3 conformance harness (OdbDesign component-connectivity plan, handoff §5c /
/// followup F2/F4): pins this client's refDes → (pin, net) view — the
/// <see cref="ProductModelReader"/> output the tabs serve — to the server-derived
/// golden file over <b>real captured server data</b> (the ComponentsFile pair,
/// EdaDataFile, and the always-on <c>Connectivity</c> block of sample_design,
/// serialized to .binpb test data; see TestData/Connectivity/README.md).
///
/// What must never regress — the harness's whole purpose — is a client-side join
/// drifting from server-owned semantics: re-introducing a toeprint/netlist join,
/// resolving identities by anything but the published keys (refDes, pin number,
/// net ordinal), or re-interpreting format sentinels the server already absorbed
/// (<c>-1</c> net numbers, the <c>$NONE$</c> net) instead of reading
/// <c>Connection.Kind</c>. Any of those makes these tests fail. A disagreement
/// between the captured Connectivity block and the golden is a server bug, by
/// construction — this client only groups and indexes what the contract publishes.
///
/// Scoped to the sample_design golden (public domain; this repo's test data rule):
/// the zero-connectivity controls of handoff §8b run in the 3D client's harness.
/// </summary>
public class ProductModelConformanceTests
{
    private static ConnectivityConformanceFixture Fixture => ConnectivityConformanceFixture.Load();

    private static Odb.Lib.Protobuf.BoardSide BoardSideOf(ConnectivityGoldenComponent component) =>
        component.Side switch
        {
            "Top" => Odb.Lib.Protobuf.BoardSide.Top,
            "Bottom" => Odb.Lib.Protobuf.BoardSide.Bottom,
            _ => throw new InvalidOperationException($"golden side '{component.Side}' is not a board side"),
        };

    /// <summary>
    /// The display rule for the net tab and per-component summaries: an unnamed
    /// NET record shows as "#<ordinal>". Pin rows carry a name exactly when the
    /// roster net is named. Names are opaque strings — "$NONE$" included — and
    /// never branched on by the client.
    /// </summary>
    private static string ExpectedNetDisplayName(ConnectivityGolden golden, int netOrdinal)
    {
        var name = golden.Nets[netOrdinal].Name;
        return string.IsNullOrEmpty(name) ? $"#{netOrdinal}" : name;
    }

    /// <summary>
    /// Contract translation (§6): a golden pin is expected <b>unconnected</b> when
    /// its net ordinal is null (the -1 sentinel) or names the net the golden roster
    /// calls "$NONE$" — the server maps both to <c>Kind.UNCONNECTED</c> with the
    /// net unset, so the client never sees the sentinel at all.
    /// </summary>
    private static bool GoldenPinIsUnconnected(ConnectivityGolden golden, ConnectivityGoldenPin pin) =>
        pin.NetOrdinal is not int netOrdinal || golden.Nets[netOrdinal].Name == "$NONE$";

    private static string ConnectionSignature(OdbDesign.ProductModel.Models.NetConnection connection) =>
        $"{connection.Side}|{connection.ComponentName}|{connection.PinNumber}";

    // ───────────────────────────────────────────────────────────────────────────
    // Fixture integrity — the data the conformance rests on
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GoldenFileIsWellFormed()
    {
        var golden = Fixture.Golden;

        Assert.True(golden.Fidelity is "full" or "sampled",
            $"fidelity drives which assertions apply; unknown value '{golden.Fidelity}' must stop the harness");
        Assert.Equal(golden.Aggregates.ComponentsBySide.Values.Sum(), golden.Aggregates.ComponentsTotal);
        Assert.Equal(golden.Aggregates.PinsConnected + golden.Aggregates.PinsUnconnected, golden.Aggregates.PinsTotal);
        Assert.True(golden.Aggregates.NetsTotal >= golden.NetNameCoverage);
        Assert.NotEmpty(golden.Components);

        if (golden.IsFullFidelity)
        {
            Assert.Equal(golden.Aggregates.ComponentsTotal, golden.Components.Count);
            Assert.Equal(golden.Aggregates.NetsTotal, golden.Nets.Count);
        }
        else
        {
            Assert.Equal(golden.Nets.Count, golden.NetsIncluded);
            Assert.True(golden.Components.Count <= golden.Aggregates.ComponentsTotal);
        }

        foreach (var component in golden.Components)
        {
            Assert.False(string.IsNullOrEmpty(component.RefDes), "spec p.152: comp_name identifies the component");
            Assert.Contains(component.Side, new[] { "Top", "Bottom" });
            var ordinals = component.Pins.Select(p => p.Ordinal).ToList();
            Assert.Equal(ordinals.Distinct().Count(), ordinals.Count);
            Assert.Equal(ordinals.OrderBy(o => o), ordinals);
            foreach (var pin in component.Pins)
            {
                if (pin.NetOrdinal is int netOrdinal)
                {
                    Assert.True(netOrdinal >= 0 && netOrdinal < golden.Aggregates.NetsTotal,
                        "a connected pin names an eda/data NET-record ordinal; null is the unconnected case");
                }
            }
        }

        Assert.True(golden.NetNameCoverage > 0);
        var netOrdinals = golden.Nets.Select(n => n.Ordinal).ToList();
        Assert.Equal(netOrdinals.Distinct().Count(), netOrdinals.Count);
        for (var i = 0; i < golden.Nets.Count; i++)
        {
            Assert.True(golden.Nets[i].Ordinal == i,
                $"net {i}'s roster position must equal its ordinal (got {golden.Nets[i].Ordinal})");
        }

        // The view addresses the step's eda-data NET records by net ordinal (subnet
        // census, via counts, attributes), so the captured roster must be the dense
        // sequence the contract promises: one net per NET record, in order.
        var edaNets = Fixture.EdaData.NetRecords;
        var roster = Fixture.Connectivity.Nets;
        Assert.Equal(edaNets.Count, roster.Count);
        for (var i = 0; i < roster.Count; i++)
        {
            Assert.True(roster[i].Ordinal == (uint)i,
                $"connectivity net {i} carries ordinal {roster[i].Ordinal} — the roster must be dense");
        }
    }

    [Fact]
    public void CaptureManifest_MatchesCommittedBytes()
    {
        // Guard the fixtures against silent corruption: the manifest's sha256 pins
        // the exact captured bytes the conformance runs over, and its provenance
        // fields must agree with the golden it is paired with.
        var manifest = Fixture.Manifest;
        var golden = Fixture.Golden;
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Connectivity", manifest.Slug);

        Assert.Equal(golden.Design, manifest.DesignName);
        Assert.Equal(golden.Step, manifest.Step);
        Assert.Equal(manifest.Sha256TopComponents, Sha256Of(Path.Combine(dir, "top-components.binpb")));
        Assert.Equal(manifest.Sha256BottomComponents, Sha256Of(Path.Combine(dir, "bottom-components.binpb")));
        Assert.Equal(manifest.Sha256EdaData, Sha256Of(Path.Combine(dir, "eda-data.binpb")));
        Assert.NotNull(manifest.Sha256Connectivity);
        Assert.Equal(manifest.Sha256Connectivity, Sha256Of(Path.Combine(dir, "connectivity.binpb")));
    }

    private static string Sha256Of(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    // ───────────────────────────────────────────────────────────────────────────
    // refDes → (pin, net) view vs golden
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConformsToGolden_ComponentAndPinView()
    {
        var fixture = Fixture;
        var golden = fixture.Golden;
        var model = fixture.Model;

        // Aggregates: the reader's component list is the Connectivity roster —
        // every placed component of both sides, one pin row per roster pin, one
        // net row per roster net.
        Assert.Equal(golden.Aggregates.ComponentsTotal, model.Components.Count);
        Assert.Equal(golden.Aggregates.ComponentsBySide["Top"],
            model.Components.Count(c => c.Side == Odb.Lib.Protobuf.BoardSide.Top));
        Assert.Equal(golden.Aggregates.ComponentsBySide["Bottom"],
            model.Components.Count(c => c.Side == Odb.Lib.Protobuf.BoardSide.Bottom));
        Assert.Equal(golden.Aggregates.NetsTotal, model.Nets.Count);
        Assert.Equal(golden.Aggregates.PinsTotal, model.Components.Sum(c => c.Pins.Count));

        AssertPinViewMatchesGolden(golden, fixture.Index);
    }

    /// <summary>
    /// The golden's view: for every (golden) component, the refDes-keyed detail
    /// row exists with the same side and pin roster — pin numbers equal (and
    /// roster-ascending, per the contract's as-built), and a net exactly when the
    /// golden says so (up to the documented <c>$NONE$</c> absorption): the net's
    /// ordinal through <c>PinDetail.NetNumber</c>, its resolved name through
    /// <c>PinDetail.NetName</c> (up to the golden's name coverage). Unconnected
    /// pins present no net — never a sentinel number, never a reconstructed name.
    /// </summary>
    private static void AssertPinViewMatchesGolden(ConnectivityGolden golden, ComponentDetailIndex index)
    {
        var mismatches = new List<string>();
        foreach (var component in golden.Components)
        {
            var detail = index.FindByRefDes(component.RefDes);
            if (detail == null)
            {
                mismatches.Add($"{component.Side} #{component.Index} ({component.RefDes}): no detail row");
                continue;
            }

            if (detail.Side != BoardSideOf(component))
            {
                mismatches.Add($"{component.RefDes}: side {detail.Side} != golden {component.Side}");
            }

            if (detail.Pins.Count != component.Pins.Count)
            {
                mismatches.Add($"{component.RefDes}: {detail.Pins.Count} pin rows, golden has {component.Pins.Count}");
                continue;
            }

            // The roster emits in pin-number-ascending order (contract as-built);
            // compare against the golden's pins grouped by pin number.
            var rosterNumbers = detail.Pins.Select(p => p.PinNumber).ToList();
            Assert.Equal(rosterNumbers.OrderBy(n => n), rosterNumbers);

            foreach (var expected in component.Pins)
            {
                var pin = detail.Pins.FirstOrDefault(p => p.PinNumber == expected.PinNumber);
                if (pin == null)
                {
                    mismatches.Add($"{component.RefDes}: roster has no pin number {expected.PinNumber}");
                    continue;
                }

                var where = $"{component.RefDes} pin {expected.PinNumber}";

                if (GoldenPinIsUnconnected(golden, expected))
                {
                    if (pin.NetName != null || pin.NetNumber != 0)
                    {
                        mismatches.Add($"{where}: golden says unconnected but the view shows net '{pin.NetName}' #{pin.NetNumber}");
                    }
                }
                else
                {
                    var netOrdinal = expected.NetOrdinal!.Value;
                    if (pin.NetNumber != (uint)netOrdinal)
                    {
                        mismatches.Add($"{where}: netNumber {pin.NetNumber} != golden ordinal {netOrdinal}");
                    }

                    if (netOrdinal < golden.NetNameCoverage)
                    {
                        var expectedNetName = ExpectedPinNetName(golden, netOrdinal);
                        if (pin.NetName != expectedNetName)
                        {
                            mismatches.Add($"{where}: net '{pin.NetName}' != golden '{expectedNetName}'");
                        }
                    }
                }
            }
        }

        Assert.True(mismatches.Count == 0,
            $"the pin view must equal the golden exactly: {string.Join("; ", mismatches.Take(10))}" +
            (mismatches.Count > 10 ? $" (+{mismatches.Count - 10} more)" : string.Empty));
    }

    /// <summary>The pin-level expectation: null when the golden net is unnamed.</summary>
    private static string? ExpectedPinNetName(ConnectivityGolden golden, int netOrdinal) =>
        string.IsNullOrEmpty(golden.Nets[netOrdinal].Name)
            ? null
            : golden.Nets[netOrdinal].Name;

    [Fact]
    public void ConformsToGolden_NetTab()
    {
        var fixture = Fixture;
        var golden = fixture.Golden;
        var model = fixture.Model;
        var totalConnections = 0;

        // The inverted view: every covered golden net's connections are the golden
        // (refDes, pinNumber, side) edges of its non-$NONE$ members, all resolved —
        // the server pre-inverts them into netMembers, so the client inverts nothing.
        // The $NONE$ net itself is excluded from netMembers by the contract (its
        // members are UNCONNECTED pins, not edges) and must show no connections.
        Assert.True(golden.Aggregates.PinsConnected > 0);

        foreach (var net in golden.Nets)
        {
            var detail = model.Nets[net.Ordinal];
            Assert.Equal(ExpectedNetDisplayName(golden, net.Ordinal), detail.Name);

            var expected = golden.Components
                .SelectMany(c => c.Pins
                    .Where(p => p.NetOrdinal == net.Ordinal && !GoldenPinIsUnconnected(golden, p))
                    .Select(p => $"{BoardSideOf(c)}|{c.RefDes}|{p.PinNumber}"))
                .OrderBy(s => s)
                .ToList();

            var actual = detail.Connections.Select(ConnectionSignature).OrderBy(s => s).ToList();
            totalConnections += actual.Count;

            Assert.True(actual.SequenceEqual(expected),
                $"net {net.Ordinal} ({net.Name}) must hold exactly the golden edges" +
                (actual.Count != expected.Count ? $" (view has {actual.Count}, golden {expected.Count})" : string.Empty));
        }

        // Each connected (non-$NONE$) pin is exactly one net-tab edge; the golden's
        // pins_connected counts $NONE$ members as connected, so subtract them.
        var noneMemberPins = golden.Components
            .SelectMany(c => c.Pins)
            .Count(p => GoldenPinIsUnconnected(golden, p));
        Assert.Equal(golden.Aggregates.PinsConnected - noneMemberPins, totalConnections);
    }

    [Fact]
    public void ConformsToGolden_ComponentNetSummaries()
    {
        var fixture = Fixture;
        var golden = fixture.Golden;
        var mismatches = new List<string>();

        foreach (var component in golden.Components)
        {
            var expected = component.Pins
                .Where(p => !GoldenPinIsUnconnected(golden, p))
                .GroupBy(p => p.NetOrdinal!.Value)
                .Select(g => (Index: (uint)g.Key, PinCount: g.Count(), Name: ExpectedNetDisplayName(golden, g.Key)))
                .OrderBy(r => r.Index)
                .ToList();

            var detail = fixture.Index.FindByRefDes(component.RefDes);
            if (detail == null)
            {
                continue; // already reported by the pin-view pass
            }

            var actual = detail.Nets
                .Select(r => (r.Index, r.PinCount, r.Name))
                .OrderBy(r => r.Index)
                .ToList();

            if (!actual.SequenceEqual(expected))
            {
                mismatches.Add(
                    $"{component.RefDes}: [{string.Join(",", actual.Select(r => $"{r.Name}#{r.Index}x{r.PinCount}"))}] " +
                    $"!= golden [{string.Join(",", expected.Select(r => $"{r.Name}#{r.Index}x{r.PinCount}"))}]");
            }
        }

        Assert.True(mismatches.Count == 0,
            $"per-component net summaries must equal the golden grouping: {string.Join("; ", mismatches.Take(10))}");
    }

    [Fact]
    public void UnconnectedPinsComeFromTheRoster_NeverReconstructedFromNets()
    {
        // Contract §6/M2.2: the $NONE$ members this design carries (86 of them on
        // sample_design) surface as Kind.UNCONNECTED with the net unset — not as
        // members of a magic net, and not reconstructed from the net tab. The
        // control is verified from the view, not the capture: every pin with no
        // net name is exactly a golden $NONE$/null-net pin, and vice versa.
        var fixture = Fixture;
        var golden = fixture.Golden;

        var expectedUnconnected = golden.Components
            .SelectMany(c => c.Pins.Where(p => GoldenPinIsUnconnected(golden, p)).Select(p => (c.RefDes, p.PinNumber)))
            .ToHashSet();
        Assert.True(expectedUnconnected.Count > 0, "this control needs a design with unconnected pins");

        var actualUnconnected = fixture.Model.Components
            .SelectMany(c => c.Pins.Where(p => p.NetName == null).Select(p => (c.Name, p.PinNumber)))
            .ToHashSet();

        Assert.Equal(expectedUnconnected, actualUnconnected);
    }

    [Fact]
    public void NamedNetsResolve_EveryConnectedPinCarriesAName()
    {
        // The name-resolution half of the old full-connectivity control (design
        // doc §4.2): every covered golden net of this design is named, so every
        // CONNECTED pin resolves a name and every UNCONNECTED pin carries none.
        var fixture = Fixture;
        Assert.All(
            fixture.Golden.Nets.Take(fixture.Golden.NetNameCoverage),
            net => Assert.False(string.IsNullOrEmpty(net.Name),
                "this control requires every covered net to be named"));

        var connectedPins = fixture.Model.Components.Sum(c => c.Pins.Count(p => p.NetName != null));
        var unconnectedPins = fixture.Model.Components.Sum(c => c.Pins.Count(p => p.NetName == null));
        var goldenNoneMembers = fixture.Golden.Components
            .SelectMany(c => c.Pins)
            .Count(p => GoldenPinIsUnconnected(fixture.Golden, p));

        Assert.Equal(fixture.Golden.Aggregates.PinsTotal - goldenNoneMembers, connectedPins);
        Assert.Equal(goldenNoneMembers, unconnectedPins);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Identity rules the conformance exists to enforce
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ViewIsUnchangedWhenIdsChange()
    {
        // The §5 deploy landmine, now structural: the view keys on refDes and pin
        // number only, so ids — fileModel record ids and Connectivity
        // PlacedComponent.id alike — are display handles that can never move a
        // pin row, a net summary, or a net-tab edge. Rewrite every id in both
        // sources and require a bit-identical relationship view.
        var fixture = Fixture;
        var golden = fixture.Golden;
        var original = fixture.Index;

        var top = fixture.TopComponents.Clone();
        var bottom = fixture.BottomComponents.Clone();
        foreach (var file in new[] { top, bottom })
        {
            for (var i = 0; i < file.ComponentRecords.Count; i++)
            {
                file.ComponentRecords[i].Id = 900_000u + (uint)i;
            }
        }

        var connectivity = fixture.Connectivity.Clone();
        foreach (var (component, i) in connectivity.Components.Select((c, i) => (c, i)))
        {
            component.Id = 800_000u + (uint)i;
        }

        var rebuilt = ConnectivityConformanceFixture.BuildIndex(top, bottom, fixture.EdaData, connectivity);
        AssertPinViewMatchesGolden(golden, rebuilt);

        // Bit-identical against the unmutated build: no pin row or net summary may
        // move when ids change (only the displayed Id handle does).
        Assert.Equal(original.ByRefDes.Keys.OrderBy(k => k), rebuilt.ByRefDes.Keys.OrderBy(k => k));
        foreach (var refDes in original.ByRefDes.Keys)
        {
            var a = original.ByRefDes[refDes];
            var b = rebuilt.ByRefDes[refDes];
            Assert.NotEqual(a.Id, b.Id);
            Assert.Equal(a.Pins, b.Pins);
            Assert.Equal(a.Nets, b.Nets);
        }

        Assert.Equal(original.Nets.Count, rebuilt.Nets.Count);
        for (var i = 0; i < original.Nets.Count; i++)
        {
            Assert.Equal(original.Nets[i].Name, rebuilt.Nets[i].Name);
            Assert.Equal(original.Nets[i].Index, rebuilt.Nets[i].Index);
            Assert.Equal(
                original.Nets[i].Connections.Select(ConnectionSignature),
                rebuilt.Nets[i].Connections.Select(ConnectionSignature));
        }
    }

    [Fact]
    public void RefDesLookupIsCaseSensitive_OverRealData()
    {
        // Spec p.152: refDes case is significant (handoff §8a / §8 hazard 3, the
        // shared contract with the 3D client). With no case-variant refDes in this
        // design, a case-folded probe missing is exactly the ordinal behaviour the
        // identity model requires. The probe must itself contain uppercase, or the
        // fold probe says nothing.
        var probe = Fixture.Golden.Components
            .Select(c => c.RefDes)
            .First(r => r != r.ToLowerInvariant());

        Assert.True(Fixture.Index.ByRefDes.ContainsKey(probe), "the exact refDes must hit");
        Assert.False(Fixture.Index.ByRefDes.ContainsKey(probe.ToLowerInvariant()),
            $"the refDes index must be StringComparer.Ordinal — '{probe}' must not case-fold to a hit");
    }
}
