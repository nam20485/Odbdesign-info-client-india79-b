using System.Security.Cryptography;
using System.Text.Json;
using Odb.Lib.Protobuf;
using OdbDesign.ProductModel;
using OdbDesignInfoClient.Tests.ProductModel.TestSupport;
using Xunit;

namespace OdbDesignInfoClient.Tests.ProductModel;

/// <summary>
/// M3.3 conformance harness (OdbDesign component-connectivity plan, handoff §5c /
/// followup F2): pins this client's refDes → (pin, net) view — the
/// <see cref="ProductModelReader"/> output the tabs serve — to the server-derived
/// golden file over <b>real captured server data</b> (the ComponentsFile pair and
/// EdaDataFile of sample_design, serialized to .binpb test data; see
/// TestData/Connectivity/README.md).
///
/// What must never regress — the harness's whole purpose — is a client-side join
/// drifting from server-owned semantics: resolving component numbers against
/// <c>ComponentRecord.Id</c> (the handoff §6 deploy landmine, disarmed here with the
/// golden's real UIDs), reordering or renumbering the per-side ordinals the identity
/// model (D1) forbids clients from joining on, or mis-resolving a toeprint's net
/// ordinal into a name. Any of those makes these tests fail.
///
/// Scoped to the sample_design golden (public domain; this repo's test data rule):
/// the zero-connectivity controls of handoff §8b run in the 3D client's harness,
/// which shares this builder through <c>OdbDesign.ProductModel</c>.
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
    /// The builder's display rule: an unnamed NET record falls back to "#<ordinal>";
    /// the client never surfaces an empty name for a connected pin. Names are opaque
    /// strings — "$NONE$" included — and never branched on.
    /// </summary>
    private static string ExpectedNetName(ConnectivityGolden golden, int netOrdinal)
    {
        var name = golden.Nets[netOrdinal].Name;
        return string.IsNullOrEmpty(name) ? $"#{netOrdinal}" : name;
    }

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
    }

    [Fact]
    public void CaptureManifest_MatchesCommittedBytes()
    {
        // Guard the fixtures against silent corruption: the manifest's sha256 pins
        // the exact captured bytes the conformance runs over.
        var manifest = Fixture.Manifest;
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Connectivity", manifest.Slug);

        Assert.Equal(manifest.Sha256TopComponents, Sha256Of(Path.Combine(dir, "top-components.binpb")));
        Assert.Equal(manifest.Sha256BottomComponents, Sha256Of(Path.Combine(dir, "bottom-components.binpb")));
        Assert.Equal(manifest.Sha256EdaData, Sha256Of(Path.Combine(dir, "eda-data.binpb")));
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

        // Aggregates: the reader's component list is the builder's ByKey values —
        // every placed component of both sides, one pin row per component-file TOP
        // record, one net row per eda/data NET record.
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
    /// The golden's view: for every (golden) component, the detail row at
    /// (side, per-side ordinal) carries the same refDes and the same pin rows in
    /// toeprint order — pin numbers and names equal, a net exactly when the golden
    /// says so: the raw net ordinal through <c>PinDetail.NetNumber</c>, the resolved
    /// name through <c>PinDetail.NetName</c> (up to the golden's name coverage).
    /// Unconnected pins present no net; the -1 sentinel never becomes one.
    /// </summary>
    private static void AssertPinViewMatchesGolden(ConnectivityGolden golden, ComponentDetailIndex index)
    {
        var mismatches = new List<string>();
        foreach (var component in golden.Components)
        {
            var key = (BoardSideOf(component), (uint)component.Index);
            if (!index.ByKey.TryGetValue(key, out var detail))
            {
                mismatches.Add($"{component.Side} #{component.Index} ({component.RefDes}): no detail row");
                continue;
            }

            if (detail.Name != component.RefDes)
            {
                mismatches.Add($"{component.Side} #{component.Index}: refDes '{detail.Name}' != golden '{component.RefDes}'");
            }

            if (detail.Pins.Count != component.Pins.Count)
            {
                mismatches.Add($"{component.RefDes}: {detail.Pins.Count} pin rows, golden has {component.Pins.Count}");
                continue;
            }

            for (var i = 0; i < component.Pins.Count; i++)
            {
                var pin = detail.Pins[i];
                var expected = component.Pins[i];
                var where = $"{component.RefDes} pin {i}";

                if (pin.PinNumber != expected.PinNumber)
                {
                    mismatches.Add($"{where}: pinNumber {pin.PinNumber} != golden {expected.PinNumber}");
                }

                var expectedName = string.IsNullOrEmpty(expected.Name) ? null : expected.Name;
                if (pin.Name != expectedName)
                {
                    mismatches.Add($"{where}: name '{pin.Name}' != golden '{expectedName}'");
                }

                if (expected.NetOrdinal is int netOrdinal)
                {
                    if (pin.NetNumber != (uint)netOrdinal)
                    {
                        mismatches.Add($"{where}: netNumber {pin.NetNumber} != golden ordinal {netOrdinal}");
                    }

                    if (netOrdinal < golden.NetNameCoverage && pin.NetName != ExpectedNetName(golden, netOrdinal))
                    {
                        mismatches.Add($"{where}: net '{pin.NetName}' != golden '{ExpectedNetName(golden, netOrdinal)}'");
                    }
                }
                else if (pin.NetName != null)
                {
                    mismatches.Add($"{where}: golden says unconnected but the view shows net '{pin.NetName}'");
                }
            }
        }

        Assert.True(mismatches.Count == 0,
            $"the pin view must equal the golden exactly: {string.Join("; ", mismatches.Take(10))}" +
            (mismatches.Count > 10 ? $" (+{mismatches.Count - 10} more)" : string.Empty));
    }

    [Fact]
    public void ConformsToGolden_NetTab()
    {
        var fixture = Fixture;
        var golden = fixture.Golden;
        var model = fixture.Model;
        var totalConnections = 0;

        // The inverted view: every covered golden net's connections are the golden
        // (refDes, pinNumber, side) edges, all resolved — none degrade to raw
        // "#number" placeholders. Net-tab equality is only asserted for connected
        // designs, where the component files and the eda netlist agree.
        Assert.True(golden.Aggregates.PinsConnected > 0);

        foreach (var net in golden.Nets)
        {
            var expected = golden.Components
                .SelectMany(c => c.Pins
                    .Where(p => p.NetOrdinal == net.Ordinal)
                    .Select(p => $"{BoardSideOf(c)}|{c.RefDes}|{p.PinNumber}"))
                .OrderBy(s => s)
                .ToList();

            var detail = model.Nets[net.Ordinal];
            Assert.Equal(ExpectedNetName(golden, net.Ordinal), detail.Name);
            var actual = detail.Connections.Select(ConnectionSignature).OrderBy(s => s).ToList();
            totalConnections += actual.Count;

            Assert.True(actual.SequenceEqual(expected),
                $"net {net.Ordinal} ({net.Name}) must hold exactly the golden edges" +
                (actual.Count != expected.Count ? $" (view has {actual.Count}, golden {expected.Count})" : string.Empty));
        }

        // Each connected pin is exactly one net-tab edge (design doc §4.2: 2,811/2,811).
        Assert.Equal(golden.Aggregates.PinsConnected, totalConnections);
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
                .Where(p => p.NetOrdinal.HasValue)
                .GroupBy(p => p.NetOrdinal!.Value)
                .Select(g => (Index: (uint)g.Key, PinCount: g.Count(), Name: ExpectedNetName(golden, g.Key)))
                .OrderBy(r => r.Index)
                .ToList();

            if (!fixture.Index.ByKey.TryGetValue((BoardSideOf(component), (uint)component.Index), out var detail))
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
    public void EveryPinIsConnected_FullConnectivityControl()
    {
        // Full-connectivity control (design doc §4.2): every one of the 2,811 pins
        // is connected — verified from the view, not just the golden's own counts.
        var fixture = Fixture;

        Assert.True(
            fixture.Model.Components.Sum(c => c.Pins.Count(p => p.NetName != null)) == fixture.Golden.Aggregates.PinsTotal,
            "every golden net of this design is named, so every connected pin resolves a name");
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Identity rules the conformance exists to enforce
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ViewIsUnchangedWhenTheServerPopulatesUid()
    {
        // The capture predates server Phase 1 (id population): every
        // ComponentRecord.Id reads 0. Simulate the day it ships by writing each
        // golden uid into its (side, ordinal) record — on this design every uid
        // differs from its ordinal — and require the rebuilt view to be identical.
        // A view keyed on ordinal only is immune by construction; any id-based
        // resolution shows up here as a diff.
        var fixture = Fixture;
        var golden = fixture.Golden;

        Assert.True(golden.Aggregates.ComponentsTotal == golden.Aggregates.UidsPresent,
            "this simulation needs a uid on every component");

        var top = fixture.TopComponents.Clone();
        var bottom = fixture.BottomComponents.Clone();
        foreach (var component in golden.Components.Where(c => c.Uid.HasValue))
        {
            var file = BoardSideOf(component) == Odb.Lib.Protobuf.BoardSide.Top ? top : bottom;
            file.ComponentRecords[component.Index].Id = (uint)component.Uid!;
        }

        Assert.True(top.ComponentRecords.Count(r => r.Id != 0) > 0, "the simulation must populate ids");
        Assert.True(golden.Components.Any(c => c.Uid!.Value != c.Index),
            "the uids must differ from the ordinals, or the simulation proves nothing");

        var rebuiltIndex = ConnectivityConformanceFixture.BuildIndex(top, bottom, fixture.EdaData);
        AssertPinViewMatchesGolden(golden, rebuiltIndex);

        // Bit-identical against the unmutated build: no pin row or net summary may
        // move when ids populate.
        var original = fixture.Index;
        Assert.Equal(original.ByKey.Keys, rebuiltIndex.ByKey.Keys);
        foreach (var (key, detail) in original.ByKey)
        {
            var replayed = rebuiltIndex.ByKey[key];
            Assert.Equal(detail.Pins, replayed.Pins);
            Assert.Equal(detail.Nets, replayed.Nets);
        }

        Assert.Equal(original.Nets.Count, rebuiltIndex.Nets.Count);
        for (var i = 0; i < original.Nets.Count; i++)
        {
            Assert.Equal(original.Nets[i].Name, rebuiltIndex.Nets[i].Name);
            Assert.Equal(original.Nets[i].Index, rebuiltIndex.Nets[i].Index);
            Assert.Equal(original.Nets[i].SubnetCount, rebuiltIndex.Nets[i].SubnetCount);
            Assert.Equal(
                original.Nets[i].Connections.Select(ConnectionSignature),
                rebuiltIndex.Nets[i].Connections.Select(ConnectionSignature));
        }
    }

    [Fact]
    public void RefDesLookupIsCaseSensitive_OverRealData()
    {
        // Spec p.152: refDes case is significant (handoff §8a). With no
        // case-variant refDes in this design, a case-folded probe missing is
        // exactly the ordinal behaviour the identity model requires.
        var probe = Fixture.Golden.Components.First(c => c.RefDes.Any(char.IsLetter)).RefDes;

        Assert.True(Fixture.Index.ByName.ContainsKey(probe), "the exact refDes must hit");
        Assert.False(Fixture.Index.ByName.ContainsKey(probe.ToLowerInvariant()),
            $"the refDes index must be StringComparer.Ordinal — '{probe}' must not case-fold to a hit");
    }
}
