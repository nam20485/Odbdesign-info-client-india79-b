using Microsoft.Extensions.Logging.Abstractions;
using Odb.Lib.Protobuf;
using Odb.Lib.Protobuf.ProductModel;
using OdbDesign.ProductModel;
using Xunit;

namespace OdbDesignInfoClient.Tests.ProductModel;

/// <summary>
/// Regression pins for the Connectivity-contract rebuild of
/// <c>OdbDesign.ProductModel.ComponentDetailBuilder</c>: the pin→net truth is read
/// from the server-owned <c>Connectivity</c> block (roster pins, connection kinds,
/// <c>netMembers</c>) — never re-joined from the fileModel (no toeprint/netlist
/// resolution, no sentinel interpretation); fileModel data (placement, packages,
/// attributes) attaches through published identity fields only (refDes, package
/// name, pin number); and the refDes index is case-sensitive (spec p.152,
/// component-connectivity.md §8 hazard 3 — the contract shared with the 3D client).
/// </summary>
public class ComponentDetailBuilderTests
{
    private static ComponentDetailBuilder CreateBuilder() =>
        new(NullLogger<ComponentDetailBuilder>.Instance);

    // ───────────────────────────────────────────────────────────────────────────
    // Synthetic fixture: one design whose relationships live in a server-shaped
    // Connectivity message and whose fileModel carries placement/package data.
    // NetRecord.Index == list position everywhere (the contract's dense roster).
    // ───────────────────────────────────────────────────────────────────────────

    private static ComponentsFile.Types.ComponentRecord.Types.ToeprintRecord Toeprint(
        uint pinNumber,
        double x,
        uint rawNetNumber) => new()
    {
        PinNumber = pinNumber,
        LocationX = x,
        LocationY = -x / 2,
        // Raw toeprint net numbers are deliberately mispointed: the rebuilt view
        // must never read them (the server absorbed them into Connection.Kind).
        NetNumber = rawNetNumber,
    };

    private static ComponentsFile CreateTopComponents()
    {
        var r1 = new ComponentsFile.Types.ComponentRecord
        {
            CompName = "R1",
            PartName = "RES-10K",
            // Deliberately wrong list index: packages resolve by NAME (published
            // identity), never by the positional PkgRef.
            PkgRef = 1,
            LocationX = 1.5,
            LocationY = -2.5,
            Rotation = 90,
        };
        r1.ToeprintRecords.Add(Toeprint(1, 0.5, 99));
        r1.ToeprintRecords.Add(Toeprint(2, 1.0, 99));
        r1.PropertyRecords.Add(new PropertyRecord { Name = "VALUE", Value = "10k" });

        // pin 5 is NC: its toeprint points at the raw $NONE$ net record, but the
        // roster says UNCONNECTED — the sentinel must not leak through.
        var u1 = new ComponentsFile.Types.ComponentRecord
        {
            CompName = "U1",
            PartName = "IC-SOIC8",
            PkgRef = 1,
            LocationX = -6,
            LocationY = 7,
        };
        u1.ToeprintRecords.Add(Toeprint(1, 0.1, 99));
        u1.ToeprintRecords.Add(Toeprint(2, 0.2, 99));
        u1.ToeprintRecords.Add(Toeprint(3, 0.3, 99));
        u1.ToeprintRecords.Add(Toeprint(4, 0.4, 99));
        u1.ToeprintRecords.Add(Toeprint(5, 0.5, 3));

        var top = new ComponentsFile { Units = "mm" };
        top.ComponentRecords.Add(r1);
        top.ComponentRecords.Add(u1);
        return top;
    }

    private static ComponentsFile CreateBottomComponents()
    {
        var l1 = new ComponentsFile.Types.ComponentRecord
        {
            CompName = "L1",
            PartName = "IND-0603",
            PkgRef = 0,
            LocationX = 9,
            LocationY = -9,
        };
        l1.ToeprintRecords.Add(Toeprint(1, 0.25, 99));

        var bottom = new ComponentsFile { Units = "mm" };
        bottom.ComponentRecords.Add(l1);
        return bottom;
    }

    private static EdaDataFile CreateEdaData()
    {
        var eda = new EdaDataFile();

        var r0603 = new EdaDataFile.Types.PackageRecord
        {
            Name = "R0603",
            Pitch = 0.5f,
            XMin = -1,
            YMin = -0.5,
            XMax = 1,
            YMax = 0.5,
        };
        r0603.PinRecords.Add(new EdaDataFile.Types.PackageRecord.Types.PinRecord());
        r0603.PinRecords.Add(new EdaDataFile.Types.PackageRecord.Types.PinRecord());
        eda.PackageRecords.Add(r0603);
        eda.PackageRecords.Add(new EdaDataFile.Types.PackageRecord { Name = "SOIC8" });

        EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type[] types =
        {
            EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Toeprint,
            EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Via,
        };

        var vcc = new EdaDataFile.Types.NetRecord { Name = "VCC", Index = 0 };
        vcc.SubnetRecords.Add(new EdaDataFile.Types.NetRecord.Types.SubnetRecord { Type = types[0] });
        vcc.SubnetRecords.Add(new EdaDataFile.Types.NetRecord.Types.SubnetRecord { Type = types[0] });
        vcc.SubnetRecords.Add(new EdaDataFile.Types.NetRecord.Types.SubnetRecord { Type = types[1] });
        vcc.SubnetRecords.Add(new EdaDataFile.Types.NetRecord.Types.SubnetRecord { Type = types[1] });
        eda.NetRecords.Add(vcc);

        var gnd = new EdaDataFile.Types.NetRecord { Name = "GND", Index = 1 };
        gnd.SubnetRecords.Add(new EdaDataFile.Types.NetRecord.Types.SubnetRecord { Type = types[0] });
        gnd.SubnetRecords.Add(new EdaDataFile.Types.NetRecord.Types.SubnetRecord { Type = types[1] });
        eda.NetRecords.Add(gnd);

        eda.NetRecords.Add(new EdaDataFile.Types.NetRecord { Name = "CLK", Index = 2 });
        eda.NetRecords.Add(new EdaDataFile.Types.NetRecord { Name = "$NONE$", Index = 3 });
        eda.NetRecords.Add(new EdaDataFile.Types.NetRecord { Name = "", Index = 4 });

        return eda;
    }

    private static Connectivity.Types.ComponentPin Pin(
        uint number,
        string name,
        Connectivity.Types.Connection.Types.Kind kind,
        uint net = 0) => new()
    {
        Number = number,
        Name = name,
        Connection = new Connectivity.Types.Connection { Kind = kind, Net = net },
    };

    private static Connectivity CreateConnectivity() => new()
    {
        Nets =
        {
            new Connectivity.Types.NetEntry { Ordinal = 0, Name = "VCC" },
            new Connectivity.Types.NetEntry { Ordinal = 1, Name = "GND" },
            new Connectivity.Types.NetEntry { Ordinal = 2, Name = "CLK" },
            new Connectivity.Types.NetEntry { Ordinal = 3, Name = "$NONE$" },
            new Connectivity.Types.NetEntry { Ordinal = 4, Name = "" },
        },
        Components =
        {
            new Connectivity.Types.PlacedComponent
            {
                RefDes = "R1",
                Id = 11,
                Side = BoardSide.Top,
                Package = "R0603",
                Pins =
                {
                    Pin(1, "1", Connectivity.Types.Connection.Types.Kind.Connected, 0),
                    Pin(2, "2", Connectivity.Types.Connection.Types.Kind.Connected, 1),
                },
            },
            new Connectivity.Types.PlacedComponent
            {
                RefDes = "U1",
                Id = 12,
                Side = BoardSide.Top,
                Package = "SOIC8",
                Pins =
                {
                    Pin(1, "1", Connectivity.Types.Connection.Types.Kind.Connected, 0),
                    Pin(2, "2", Connectivity.Types.Connection.Types.Kind.Connected, 0),
                    Pin(3, "3", Connectivity.Types.Connection.Types.Kind.Connected, 1),
                    Pin(4, "4", Connectivity.Types.Connection.Types.Kind.Connected, 2),
                    // NC pin: Kind.UNCONNECTED, net unset — the roster's truth even
                    // though the fileModel toeprint carries a $NONE$ net number.
                    Pin(5, "5", Connectivity.Types.Connection.Types.Kind.Unconnected),
                },
            },
            new Connectivity.Types.PlacedComponent
            {
                RefDes = "L1",
                Id = 13,
                Side = BoardSide.Bottom,
                Package = "R0603",
                Pins = { Pin(1, "1", Connectivity.Types.Connection.Types.Kind.Connected, 2) },
            },
        },
        NetMembers =
        {
            new Connectivity.Types.NetMembers
            {
                Net = 0,
                Members =
                {
                    new Connectivity.Types.Member { RefDes = "R1", Pin = { 1 } },
                    new Connectivity.Types.Member { RefDes = "U1", Pin = { 1, 2 } },
                },
            },
            new Connectivity.Types.NetMembers
            {
                Net = 1,
                Members =
                {
                    new Connectivity.Types.Member { RefDes = "R1", Pin = { 2 } },
                    new Connectivity.Types.Member { RefDes = "U1", Pin = { 3 } },
                },
            },
            new Connectivity.Types.NetMembers
            {
                Net = 2,
                Members =
                {
                    new Connectivity.Types.Member { RefDes = "U1", Pin = { 4 } },
                    new Connectivity.Types.Member { RefDes = "L1", Pin = { 1 } },
                },
            },
            // nets 3 ($NONE$) and 4 (unnamed) have no members by contract.
        },
    };

    private static ComponentDetailIndex BuildSynthetic() =>
        CreateBuilder().Build(CreateTopComponents(), CreateBottomComponents(), CreateEdaData(), CreateConnectivity());

    // ───────────────────────────────────────────────────────────────────────────
    // Index shape and identity
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_ReturnsEmpty_WithoutConnectivity()
    {
        var index = CreateBuilder().Build(CreateTopComponents(), CreateBottomComponents(), CreateEdaData(), null);

        Assert.Same(ComponentDetailIndex.Empty, index);
        Assert.Empty(index.Components);
        Assert.Empty(index.Nets);
    }

    [Fact]
    public void Build_IndexesComponents_ByRefDes()
    {
        var index = BuildSynthetic();

        // Every placed component, in server order, keyed on its authoritative refDes.
        Assert.Equal(new[] { "R1", "U1", "L1" }, index.Components.Select(c => c.Name));
        Assert.Equal(3, index.ByRefDes.Count);
        Assert.Same(index.Components[0], index.FindByRefDes("R1"));
        Assert.Same(index.Components[2], index.FindByRefDes("L1"));
        Assert.Null(index.FindByRefDes("NOPE"));

        // Identity fields come from the roster: refDes, id (a handle, never a key), side.
        Assert.Equal(11u, index.FindByRefDes("R1")!.Id);
        Assert.Equal(BoardSide.Bottom, index.FindByRefDes("L1")!.Side);
    }

    [Fact]
    public void Build_KeysRefDes_CaseSensitively()
    {
        var comps = new ComponentsFile { Units = "mm" };
        comps.ComponentRecords.Add(new ComponentsFile.Types.ComponentRecord { Id = 5, CompName = "R1", PkgRef = 0 });
        comps.ComponentRecords.Add(new ComponentsFile.Types.ComponentRecord { Id = 6, CompName = "r1", PkgRef = 0 });

        var connectivity = new Connectivity
        {
            Nets = { new Connectivity.Types.NetEntry { Ordinal = 0, Name = "VCC" } },
            Components =
            {
                new Connectivity.Types.PlacedComponent
                {
                    RefDes = "R1",
                    Id = 5,
                    Side = BoardSide.Top,
                    Pins = { Pin(1, "1", Connectivity.Types.Connection.Types.Kind.Connected, 0) },
                },
                new Connectivity.Types.PlacedComponent
                {
                    RefDes = "r1",
                    Id = 6,
                    Side = BoardSide.Top,
                    Pins = { Pin(1, "1", Connectivity.Types.Connection.Types.Kind.Connected, 0) },
                },
                new Connectivity.Types.PlacedComponent
                {
                    RefDes = "U1",
                    Id = 7,
                    Side = BoardSide.Top,
                    Pins = { Pin(1, "1", Connectivity.Types.Connection.Types.Kind.Connected, 0) },
                },
            },
        };

        var index = CreateBuilder().Build(comps, null, null, connectivity);

        // refDes is case-significant (spec p.152, §8 hazard 3): case-variant
        // placements are distinct identities (a case-folded index would collapse
        // them to one), and a case-folded probe of a mixed-case refDes misses.
        Assert.Equal(3, index.ByRefDes.Count);
        Assert.Equal(5u, index.FindByRefDes("R1")!.Id);
        Assert.Equal(6u, index.FindByRefDes("r1")!.Id);
        Assert.Equal(7u, index.FindByRefDes("U1")!.Id);
        Assert.Null(index.FindByRefDes("U1".ToLowerInvariant()));
    }

    [Fact]
    public void Build_DuplicateRefDesInConnectivity_KeepsFirstEntry()
    {
        var connectivity = CreateConnectivity();
        connectivity.Components.Add(new Connectivity.Types.PlacedComponent
        {
            RefDes = "R1",
            Id = 999,
            Side = BoardSide.Top,
        });

        var index = CreateBuilder().Build(CreateTopComponents(), CreateBottomComponents(), CreateEdaData(), connectivity);

        // A duplicate refDes violates the identity rule; the builder degrades to
        // the first entry instead of throwing or silently swapping rows.
        Assert.Equal(3, index.Components.Count);
        Assert.Equal(11u, index.FindByRefDes("R1")!.Id);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Pin rosters and net truth (server-owned)
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_PinsComeFromTheRoster_WithNetTruthAndGeometry()
    {
        var index = BuildSynthetic();

        // Roster order is pin-number ascending; one row per roster pin.
        var u1 = index.FindByRefDes("U1")!;
        Assert.Equal(new uint[] { 1, 2, 3, 4, 5 }, u1.Pins.Select(p => p.PinNumber));

        // Connected pins carry the net ordinal and its roster name.
        Assert.All(
            u1.Pins.Take(4),
            p => Assert.Equal(index.Nets[(int)p.NetNumber].Name, p.NetName));
        Assert.Equal("VCC", u1.Pins[0].NetName);
        Assert.Equal("CLK", u1.Pins[3].NetName);

        // The NC pin: Kind.UNCONNECTED → net unset, never reconstructed from the
        // net lists, and the raw $NONE$ net number on its fileModel toeprint
        // (3) never leaks into the view.
        var nc = u1.Pins[4];
        Assert.Null(nc.NetName);
        Assert.Equal(0u, nc.NetNumber);

        // Geometry attaches by pin-number identity with the record's own units.
        var r1 = index.FindByRefDes("R1")!;
        Assert.Equal(0.5, r1.Pins[0].X);
        Assert.Equal(-0.25, r1.Pins[0].Y);
        Assert.Equal(1.0, r1.Pins[1].X);
        Assert.Equal(90, r1.Rotation);

        // The NC pin keeps its toeprint geometry — geometry is fileModel data and
        // attaches by pin-number identity regardless of the connection kind.
        Assert.Equal(0.5, nc.X);
    }

    [Fact]
    public void Build_NetTabComesFromNetMembers()
    {
        var index = BuildSynthetic();

        // One row per roster net, in roster order.
        Assert.Equal(new[] { "VCC", "GND", "CLK", "$NONE$", "#4" }, index.Nets.Select(n => n.Name));
        Assert.All(index.Nets, (n, i) => Assert.Equal((uint)i, n.Index));

        // The CONNECTED edges, pre-inverted server-side; sides come from the
        // component roster by refDes.
        var vcc = index.Nets[0];
        Assert.Equal(
            new[] { "Top|R1|1", "Top|U1|1", "Top|U1|2" },
            vcc.Connections.Select(c => $"{c.Side}|{c.ComponentName}|{c.PinNumber}"));
        var clk = index.Nets[2];
        Assert.Equal(
            new[] { "Top|U1|4", "Bottom|L1|1" },
            clk.Connections.Select(c => $"{c.Side}|{c.ComponentName}|{c.PinNumber}"));

        // $NONE$ and memberless nets carry no edges (their members are
        // UNCONNECTED pins, not connections).
        Assert.Empty(index.Nets[3].Connections);
        Assert.Empty(index.Nets[4].Connections);

        // Net-record facts (subnet census, via counts) address the eda-data NET
        // record by net ordinal: VCC has 2 toeprints + 2 vias.
        Assert.Equal(4, vcc.SubnetCount);
        Assert.Equal(2, vcc.ViaCount);
        Assert.Equal(1, index.Nets[1].ViaCount);
        Assert.Equal(0, clk.ViaCount);
    }

    [Fact]
    public void Build_ComponentNetSummariesComeFromRosterConnections()
    {
        var index = BuildSynthetic();

        // One summary row per (component, net), pin counts exact, ordered by net
        // ordinal; the NC pin contributes nothing.
        var u1 = index.FindByRefDes("U1")!;
        Assert.Equal(
            new[] { ("VCC", 0u, 2), ("GND", 1u, 1), ("CLK", 2u, 1) },
            u1.Nets.Select(n => (n.Name, n.Index, n.PinCount)));

        var r1 = index.FindByRefDes("R1")!;
        Assert.Equal(
            new[] { ("VCC", 0u, 1), ("GND", 1u, 1) },
            r1.Nets.Select(n => (n.Name, n.Index, n.PinCount)));

        var l1 = index.FindByRefDes("L1")!;
        Assert.Equal(new[] { ("CLK", 2u, 1) }, l1.Nets.Select(n => (n.Name, n.Index, n.PinCount)));
    }

    // ───────────────────────────────────────────────────────────────────────────
    // fileModel enrichment (placement, packages, attributes)
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_EnrichesFromFileModel_ByRefDes()
    {
        var index = BuildSynthetic();

        var r1 = index.FindByRefDes("R1")!;
        Assert.Equal("RES-10K", r1.PartName);
        Assert.Equal("10k", r1.Value);
        Assert.Equal(1.5, r1.PositionX);
        Assert.Equal(-2.5, r1.PositionY);
        Assert.Contains(r1.Attributes, a => a.Name == "VALUE" && a.Value == "10k");

        // Package resolves by NAME from the roster (never by the positional
        // PkgRef, which deliberately points at the wrong record).
        Assert.Equal("R0603", r1.Package!.Name);
        Assert.Equal(0.5, r1.Package.Pitch!.Value, 3);

        // A component the fileModel does not know degrades honestly: roster rows
        // and net truth stay, placement/attributes/toeprint geometry empty — never
        // a throw. Package geometry still resolves: it is name-keyed on the EDA
        // data, and a components-file record is not needed for it.
        var connectivity = CreateConnectivity();
        connectivity.Components.Add(new Connectivity.Types.PlacedComponent
        {
            RefDes = "X9",
            Side = BoardSide.Top,
            Package = "R0603",
            Pins = { Pin(1, "1", Connectivity.Types.Connection.Types.Kind.Unconnected) },
        });
        var degraded = CreateBuilder().Build(null, null, CreateEdaData(), connectivity);
        var x9 = degraded.FindByRefDes("X9")!;
        Assert.Null(x9.PartName);
        Assert.Null(x9.Value);
        Assert.Equal(0, x9.PositionX);
        Assert.Empty(x9.Attributes);
        Assert.Single(x9.Pins);
        Assert.Null(x9.Pins[0].NetName);
        Assert.Equal(0, x9.Pins[0].X);
        Assert.Equal("R0603", x9.Package!.Name);
    }

    [Fact]
    public void Build_PackageWithoutEdaRecord_LeavesDetailUnset()
    {
        var connectivity = CreateConnectivity();
        connectivity.Components.Add(new Connectivity.Types.PlacedComponent
        {
            RefDes = "X1",
            Side = BoardSide.Top,
            Package = "NO-SUCH-PACKAGE",
        });

        var index = CreateBuilder().Build(CreateTopComponents(), CreateBottomComponents(), CreateEdaData(), connectivity);
        var x1 = index.FindByRefDes("X1")!;

        Assert.Equal("NO-SUCH-PACKAGE", x1.PkgRef);
        Assert.Null(x1.Package);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Reader level
    // ───────────────────────────────────────────────────────────────────────────

    private static Design CreateSyntheticDesign(Connectivity? connectivity = null)
    {
        var step = new StepDirectory { Name = "step" };
        step.LayersByName["comp_+_top"] = new LayerDirectory { Components = CreateTopComponents() };
        step.LayersByName["comp_+_bot"] = new LayerDirectory { Components = CreateBottomComponents() };
        step.Edadatafile = CreateEdaData();

        var design = new Design { Name = "synthetic", FileModel = new FileArchive(), Connectivity = connectivity };
        design.FileModel.StepsByName["step"] = step;
        return design;
    }

    [Fact]
    public void Read_BuildsConsistentModelFromSyntheticDesign()
    {
        var design = CreateSyntheticDesign(CreateConnectivity());
        var model = new ProductModelReader(NullLoggerFactory.Instance).Read(design, "step");

        Assert.Equal(3, model.Components.Count);
        Assert.Equal(5, model.Nets.Count);
        Assert.Equal(2, model.Packages.Count);

        // Parts group over the full component list with exact usage counts.
        Assert.Equal(3, model.Parts.Count);
        Assert.All(model.Parts, p => Assert.Equal(1, p.UsageCount));
        Assert.Contains(model.Parts, p => p.Name == "RES-10K");

        // The per-component separation survives the reader: U1 touches 3 nets with
        // 5 pins (one NC), never a merged whole-board summary.
        var u1 = model.Components.Single(c => c.Name == "U1");
        Assert.Equal(5, u1.Pins.Count);
        Assert.Equal(3, u1.Nets.Count);

        // Unknown step or missing connectivity → empty model (degrade, never throw).
        var reader = new ProductModelReader(NullLoggerFactory.Instance);
        Assert.Same(DesignProductModel.Empty, reader.Read(design, "no-such-step"));
        Assert.Same(DesignProductModel.Empty, reader.Read(null, "step"));
        Assert.Same(DesignProductModel.Empty, reader.Read(CreateSyntheticDesign(null), "step"));
    }

    [Fact]
    public void Read_ClassifiesDrillToolPlating_PlatedViasAndNonPlated()
    {
        var design = CreateSyntheticDesign(CreateConnectivity());
        var tools = new ToolsFile { Units = "mm" };
        tools.Tools[1] = new ToolsFile.Types.ToolsRecord
        {
            ToolNum = 1,
            Type = ToolsFile.Types.ToolsRecord.Types.Type.Plated,
            DrillSize = 0.3,
        };
        tools.Tools[2] = new ToolsFile.Types.ToolsRecord
        {
            ToolNum = 2,
            Type = ToolsFile.Types.ToolsRecord.Types.Type.NonPlated,
            DrillSize = 0.5,
        };
        tools.Tools[3] = new ToolsFile.Types.ToolsRecord
        {
            ToolNum = 3,
            Type = ToolsFile.Types.ToolsRecord.Types.Type.Via,
            DrillSize = 0.2,
        };
        design.FileModel.StepsByName["step"].LayersByName["drill"] = new LayerDirectory { ToolFile = tools };

        var model = new ProductModelReader(NullLoggerFactory.Instance).Read(design, "step");

        Assert.True(model.DrillTools.Single(t => t.ToolNumber == 1).IsPlated);
        Assert.False(model.DrillTools.Single(t => t.ToolNumber == 2).IsPlated);
        // A Via tool is a distinct enum but still drills a plated via hole —
        // only NonPlated may report unplated.
        Assert.True(model.DrillTools.Single(t => t.ToolNumber == 3).IsPlated);
    }

    [Fact]
    public void Read_ScalesPackageGeometryByEdaUnits_PlacementsByComponentsUnits()
    {
        // The EDA header declares inch while the components files declare mm —
        // package geometry must follow its own file's units, not the placements'.
        var design = CreateSyntheticDesign(CreateConnectivity());
        design.FileModel.StepsByName["step"].Edadatafile.Units = "inch";
        var r0603 = design.FileModel.StepsByName["step"].Edadatafile.PackageRecords[0];
        r0603.Pitch = 0.5;
        r0603.XMin = 1;
        r0603.XMax = 2;
        r0603.YMin = -1;
        r0603.YMax = 1;

        var model = new ProductModelReader(NullLoggerFactory.Instance).Read(design, "step");

        var package = model.Packages.Single(p => p.Name == "R0603");
        Assert.Equal(0.5 * 25.4, package.Pitch!.Value, 3);
        Assert.Equal(1 * 25.4, package.XMin, 3);
        Assert.Equal(2 * 25.4, package.XMax, 3);

        // Placements still scale by the components file's own (mm) header.
        var r1 = model.Components.Single(c => c.Name == "R1");
        Assert.Equal(1.5, r1.PositionX);
        Assert.Equal(-2.5, r1.PositionY);

        // The per-component package detail uses the same EDA scale.
        Assert.Equal(0.5 * 25.4, r1.Package!.Pitch!.Value, 3);
    }
}
