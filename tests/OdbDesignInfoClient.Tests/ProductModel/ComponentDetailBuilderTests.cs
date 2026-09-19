using Microsoft.Extensions.Logging.Abstractions;
using Odb.Lib.Protobuf;
using Odb.Lib.Protobuf.ProductModel;
using OdbDesign.ProductModel;
using Xunit;

namespace OdbDesignInfoClient.Tests.ProductModel;

/// <summary>
/// Regression pins for the connectivity-contract prep in
/// <c>OdbDesign.ProductModel.ComponentDetailBuilder</c>: component numbers must
/// resolve by per-side ordinal only (never against <c>ComponentRecord.Id</c>,
/// which the server does not populate and which must not become a join key once
/// ODB++ UIDs ship), per-component bookkeeping must key on (side, per-side
/// ordinal) so nets/pin counts stay per-component, and refDes name indexes must
/// be case-sensitive.
/// See OdbDesign docs/plan/component-connectivity-client-handoff.md §5b/§6/§8.
/// </summary>
public class ComponentDetailBuilderTests
{
    private static ComponentDetailBuilder CreateBuilder() =>
        new(NullLogger<ComponentDetailBuilder>.Instance);

    private static EdaDataFile CreateEdaData(out EdaDataFile.Types.NetRecord net)
    {
        var eda = new EdaDataFile();
        net = new EdaDataFile.Types.NetRecord { Name = "VCC", Index = 0 };
        eda.NetRecords.Add(net);
        eda.PackageRecords.Add(new EdaDataFile.Types.PackageRecord { Name = "R0603" });
        return eda;
    }

    private static EdaDataFile.Types.NetRecord.Types.SubnetRecord ToeprintSubnet(
        uint componentNumber,
        BoardSide side = BoardSide.Top)
    {
        var subnet = new EdaDataFile.Types.NetRecord.Types.SubnetRecord
        {
            Type = EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Toeprint,
            ComponentNumber = componentNumber,
            ToeprintNumber = 0,
            Side = side
        };
        return subnet;
    }

    private static ComponentsFile CreateTopComponents()
    {
        var comps = new ComponentsFile { Units = "mm" };
        // Ordinal 0 carries a UID-shaped Id (42) that must never be joined on;
        // ordinal 1 is what a subnet numbering 1 must resolve to.
        comps.ComponentRecords.Add(new ComponentsFile.Types.ComponentRecord
        {
            Id = 42,
            CompName = "R1",
            PartName = "RES-SMD",
            PkgRef = 0
        });
        comps.ComponentRecords.Add(new ComponentsFile.Types.ComponentRecord
        {
            Id = 7,
            CompName = "R2",
            PartName = "CAP-SMD",
            PkgRef = 0
        });
        return comps;
    }

    [Fact]
    public void Build_ResolvesNetComponentsByOrdinalOnly()
    {
        var eda = CreateEdaData(out var net);
        net.SubnetRecords.Add(ToeprintSubnet(1)); // R2's ordinal, not anyone's Id
        net.SubnetRecords.Add(ToeprintSubnet(42)); // equals R1's Id, but no ordinal 42 exists

        var index = CreateBuilder().Build(CreateTopComponents(), null, eda);

        Assert.Equal(2, index.Nets[0].Connections.Count);
        Assert.Equal("R2", index.Nets[0].Connections[0].ComponentName);
        // Must degrade to the raw number: matching it against ComponentRecord.Id
        // is the mis-resolution landmine the contract removes.
        Assert.Equal("#42", index.Nets[0].Connections[1].ComponentName);
    }

    [Fact]
    public void Build_UnsetSubnetSide_ResolvesBySideBlindOrdinal()
    {
        var eda = CreateEdaData(out var net);
        net.SubnetRecords.Add(ToeprintSubnet(0, BoardSide.BsNone));

        var index = CreateBuilder().Build(CreateTopComponents(), null, eda);

        // Only top exists and only one record holds ordinal 0 → unique match.
        Assert.Equal("R1", index.Nets[0].Connections[0].ComponentName);
    }

    [Fact]
    public void Build_IndexesComponentNames_CaseSensitively()
    {
        var eda = CreateEdaData(out _);
        var comps = new ComponentsFile { Units = "mm" };
        comps.ComponentRecords.Add(new ComponentsFile.Types.ComponentRecord { Id = 5, CompName = "R1", PkgRef = 0 });
        comps.ComponentRecords.Add(new ComponentsFile.Types.ComponentRecord { Id = 6, CompName = "r1", PkgRef = 0 });

        var index = CreateBuilder().Build(comps, null, eda);

        // refDes is case-significant (ODB++ spec p.152): both placements are kept.
        Assert.Equal(2, index.ByName.Count);
        Assert.Equal(5u, index.ByName["R1"].Id);
        Assert.Equal(6u, index.ByName["r1"].Id);
        Assert.Equal(6u, index.FindByName("r1")!.Id);
        Assert.Equal(5u, index.FindByName("R1")!.Id);
    }

    // ---- Per-component net separation: (side, per-side ordinal) keying ----
    //
    // The canonical component identity is refDes + per-side 0-based ordinal
    // (proto ComponentRecord.index); ComponentRecord.id is never populated by
    // the server (always 0) and is NOT a join key. Keying the per-component
    // maps on Comp.Id merged every component on a side into one entry — a
    // 1-pin component reported the whole board's net summaries. These tests
    // pin the corrected join with synthetic protobuf data (no network).

    private static ComponentsFile.Types.ComponentRecord.Types.ToeprintRecord Toeprint(
        uint pinNumber,
        uint netNumber) => new()
    {
        PinNumber = pinNumber,
        LocationX = pinNumber * 0.5,
        LocationY = -pinNumber * 0.25,
        NetNumber = netNumber,
    };

    private static EdaDataFile.Types.NetRecord.Types.SubnetRecord Connection(
        uint componentNumber,
        uint toeprintNumber,
        BoardSide side = BoardSide.Top) => new()
    {
        Type = EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Toeprint,
        ComponentNumber = componentNumber,
        ToeprintNumber = toeprintNumber,
        Side = side,
    };

    /// <summary>
    /// Top: R1 (ordinal 0, VCC/GND), C1 (ordinal 1, VCC/GND), U1 (ordinal 2, two
    /// VCC pins + GND + CLK). Bottom: L1 (ordinal 0, one pin on CLK). All ids are
    /// left at the proto default 0, exactly as the server ships.
    /// </summary>
    private static (ComponentsFile Top, ComponentsFile Bottom) CreateSyntheticComponents()
    {
        var r1 = new ComponentsFile.Types.ComponentRecord
        {
            CompName = "R1",
            PartName = "RES-10K",
            PkgRef = 0,
            LocationX = 1.5,
            LocationY = -2.5,
        };
        r1.ToeprintRecords.Add(Toeprint(1, 10));
        r1.ToeprintRecords.Add(Toeprint(2, 20));
        r1.PropertyRecords.Add(new PropertyRecord { Name = "VALUE", Value = "10k" });

        var c1 = new ComponentsFile.Types.ComponentRecord
        {
            CompName = "C1",
            PartName = "CAP-0402",
            PkgRef = 1,
            LocationX = 3,
            LocationY = 4,
        };
        c1.ToeprintRecords.Add(Toeprint(1, 10));
        c1.ToeprintRecords.Add(Toeprint(2, 20));
        c1.PropertyRecords.Add(new PropertyRecord { Name = "VALUE", Value = "100n" });

        // Two supply pins on VCC (10) — U1 must get ONE VCC summary row with PinCount 2.
        var u1 = new ComponentsFile.Types.ComponentRecord
        {
            CompName = "U1",
            PartName = "IC-SOIC8",
            PkgRef = 1,
            LocationX = -6,
            LocationY = 7,
        };
        u1.ToeprintRecords.Add(Toeprint(1, 10));
        u1.ToeprintRecords.Add(Toeprint(2, 10));
        u1.ToeprintRecords.Add(Toeprint(3, 20));
        u1.ToeprintRecords.Add(Toeprint(4, 30));

        var top = new ComponentsFile { Units = "mm" };
        top.ComponentRecords.Add(r1);
        top.ComponentRecords.Add(c1);
        top.ComponentRecords.Add(u1);

        var l1 = new ComponentsFile.Types.ComponentRecord
        {
            CompName = "L1",
            PartName = "IND-0603",
            PkgRef = 0,
            LocationX = 9,
            LocationY = -9,
        };
        l1.ToeprintRecords.Add(Toeprint(1, 30));

        var bottom = new ComponentsFile { Units = "mm" };
        bottom.ComponentRecords.Add(l1);

        return (top, bottom);
    }

    private static EdaDataFile CreateSyntheticEdaData()
    {
        var eda = new EdaDataFile();

        var r0603 = new EdaDataFile.Types.PackageRecord { Name = "R0603", Pitch = 0.5f };
        r0603.PinRecords.Add(new EdaDataFile.Types.PackageRecord.Types.PinRecord());
        r0603.PinRecords.Add(new EdaDataFile.Types.PackageRecord.Types.PinRecord());
        eda.PackageRecords.Add(r0603);
        eda.PackageRecords.Add(new EdaDataFile.Types.PackageRecord { Name = "SOIC8" });

        // Subnet ComponentNumbers are per-side ordinals: 0=R1/C1-ordinal-1... on
        // Top; on Bottom, 0=L1. U1 is ordinal 2 on Top.
        var vcc = new EdaDataFile.Types.NetRecord { Name = "VCC", Index = 10 };
        vcc.SubnetRecords.Add(Connection(0, 0)); // R1 pin 1
        vcc.SubnetRecords.Add(Connection(1, 0)); // C1 pin 1
        vcc.SubnetRecords.Add(Connection(2, 0)); // U1 pin 1
        vcc.SubnetRecords.Add(Connection(2, 1)); // U1 pin 2 — same net, second toeprint
        eda.NetRecords.Add(vcc);

        var gnd = new EdaDataFile.Types.NetRecord { Name = "GND", Index = 20 };
        gnd.SubnetRecords.Add(Connection(0, 1)); // R1 pin 2
        gnd.SubnetRecords.Add(Connection(1, 1)); // C1 pin 2
        gnd.SubnetRecords.Add(Connection(2, 2)); // U1 pin 3
        eda.NetRecords.Add(gnd);

        var clk = new EdaDataFile.Types.NetRecord { Name = "CLK", Index = 30 };
        clk.SubnetRecords.Add(Connection(2, 3)); // U1 pin 4
        clk.SubnetRecords.Add(Connection(0, 0, BoardSide.Bottom)); // L1 pin 1 (bottom ordinal 0)
        eda.NetRecords.Add(clk);

        return eda;
    }

    [Fact]
    public void Build_SeparatesPerComponentNets_BySideAndOrdinal()
    {
        var (top, bottom) = CreateSyntheticComponents();
        var index = CreateBuilder().Build(top, bottom, CreateSyntheticEdaData());

        // One ByKey entry per record, keyed on the per-side ordinal — keying on
        // the never-populated Comp.Id collapsed each side to a single entry.
        Assert.Equal(4, index.ByKey.Count);
        Assert.Same(index.FindByName("R1"), index.ByKey[(BoardSide.Top, 0u)]);
        Assert.Same(index.FindByName("C1"), index.ByKey[(BoardSide.Top, 1u)]);
        Assert.Same(index.FindByName("U1"), index.ByKey[(BoardSide.Top, 2u)]);
        // Bottom ordinal 0 coexists with Top ordinal 0.
        Assert.Same(index.FindByName("L1"), index.ByKey[(BoardSide.Bottom, 0u)]);

        // Names all resolve; the record's Id still reflects the proto id (0).
        Assert.NotNull(index.FindByName("R1"));
        Assert.NotNull(index.FindByName("C1"));
        Assert.NotNull(index.FindByName("U1"));
        Assert.NotNull(index.FindByName("L1"));
        Assert.Equal(0u, index.FindByName("R1")!.Id);

        // Each component sees ONLY the nets whose subnets reference it.
        var r1 = index.FindByName("R1")!;
        Assert.Equal(new[] { "GND", "VCC" }, r1.Nets.Select(n => n.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(1, r1.Nets.Single(n => n.Name == "VCC").PinCount);
        Assert.Equal(1, r1.Nets.Single(n => n.Name == "GND").PinCount);

        var c1 = index.FindByName("C1")!;
        Assert.Equal(new[] { "GND", "VCC" }, c1.Nets.Select(n => n.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(1, c1.Nets.Single(n => n.Name == "VCC").PinCount);
        Assert.Equal(1, c1.Nets.Single(n => n.Name == "GND").PinCount);

        // One summary row per (component, net) even though U1 touches VCC twice.
        var u1 = index.FindByName("U1")!;
        Assert.Equal(new[] { "CLK", "GND", "VCC" }, u1.Nets.Select(n => n.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(2, u1.Nets.Single(n => n.Name == "VCC").PinCount);
        Assert.Equal(1, u1.Nets.Single(n => n.Name == "GND").PinCount);
        Assert.Equal(1, u1.Nets.Single(n => n.Name == "CLK").PinCount);

        // Bottom-side component: CLK only, via the bottom ordinal 0.
        var l1 = index.FindByName("L1")!;
        Assert.Equal(BoardSide.Bottom, l1.Side);
        Assert.Equal(new[] { "CLK" }, l1.Nets.Select(n => n.Name));
        Assert.Equal(1, l1.Nets.Single(n => n.Name == "CLK").PinCount);

        // Synthetic placements/properties/pins flow through per record.
        Assert.Equal(1.5, r1.PositionX);
        Assert.Equal(-2.5, r1.PositionY);
        Assert.Equal("10k", r1.Value);
        Assert.Equal(2, r1.Pins.Count);
        Assert.Equal("VCC", r1.Pins[0].NetName);
        Assert.Equal("R0603", r1.Package?.Name);

        // Net-side connections reference each component by resolved name.
        var vcc = index.Nets.Single(n => n.Name == "VCC");
        Assert.Equal(
            new[] { "C1", "R1", "U1", "U1" },
            vcc.Connections.Select(c => c.ComponentName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Read_BuildsConsistentModelFromSyntheticDesign()
    {
        var (top, bottom) = CreateSyntheticComponents();
        var step = new StepDirectory { Name = "step" };
        step.LayersByName["comp_+_top"] = new LayerDirectory { Components = top };
        step.LayersByName["comp_+_bot"] = new LayerDirectory { Components = bottom };
        step.Edadatafile = CreateSyntheticEdaData();

        var design = new Design { Name = "synthetic", FileModel = new FileArchive() };
        design.FileModel.StepsByName["step"] = step;

        var model = new ProductModelReader(NullLogger<ProductModelReader>.Instance).Read(design, "step");

        // ByKey-derived component list: one entry per record, not one per side.
        Assert.Equal(4, model.Components.Count);
        Assert.Equal(3, model.Nets.Count);
        Assert.Equal(2, model.Packages.Count);

        // Parts group over the full component list with exact usage counts.
        Assert.Equal(4, model.Parts.Count);
        Assert.All(model.Parts, p => Assert.Equal(1, p.UsageCount));
        Assert.Contains(model.Parts, p => p.Name == "RES-10K");

        // The per-component separation survives the reader: R1 is a 2-pin part
        // on 2 nets, never the merged whole-board summary.
        var r1 = model.Components.Single(c => c.Name == "R1");
        Assert.Equal(2, r1.Nets.Count);
        var u1 = model.Components.Single(c => c.Name == "U1");
        Assert.Equal(3, u1.Nets.Count);
        Assert.Equal(3, model.Components.Max(c => c.Nets.Count));
        Assert.Equal(1, model.Components.Min(c => c.Nets.Count));

        // Unknown step → empty model (degrade, never throw).
        var reader = new ProductModelReader(NullLogger<ProductModelReader>.Instance);
        Assert.Same(DesignProductModel.Empty, reader.Read(design, "no-such-step"));
        Assert.Same(DesignProductModel.Empty, reader.Read(null, "step"));
    }
}
