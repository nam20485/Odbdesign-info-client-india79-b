using System.Text.Json;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Odb.Lib.Protobuf;
using OdbDesign.ProductModel;
using ProtoDesign = Odb.Lib.Protobuf.ProductModel.Design;

namespace OdbDesignInfoClient.Tests.ProductModel.TestSupport;

/// <summary>
/// Loads the fixture design's golden plus its captured server protobufs (the
/// <see cref="ComponentsFile"/> pair and <see cref="EdaDataFile"/> the production
/// path reads) and builds the client view once — real server data in, the client's
/// join out. The view under conformance test is the one this client actually serves:
/// <see cref="ProductModelReader.Read"/> over a <see cref="ProtoDesign"/> assembled
/// from the captures, exactly the shape a gRPC <c>GetDesign</c> response produces.
/// The builder's <see cref="ComponentDetailIndex"/> is kept alongside because the
/// golden's component identity (board side + per-side ordinal) lives in
/// <see cref="ComponentDetailIndex.ByKey"/> — the reader serves those same detail
/// instances.
/// </summary>
public sealed class ConnectivityConformanceFixture
{
    public const string SampleDesign = "sample_design";
    public const string Step = "step";

    private static readonly Lazy<ConnectivityConformanceFixture> Instance = new(() => new());

    public static ConnectivityConformanceFixture Load() => Instance.Value;

    private ConnectivityConformanceFixture()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Connectivity", SampleDesign);
        Golden = JsonSerializer.Deserialize<ConnectivityGolden>(
                     File.ReadAllText(Path.Combine(dir, $"{SampleDesign}.golden.json")))
                 ?? throw new InvalidOperationException("golden deserialized to null");
        Manifest = JsonSerializer.Deserialize<ConnectivityCaptureManifest>(
                       File.ReadAllText(Path.Combine(dir, "manifest.json")))
                   ?? throw new InvalidOperationException("manifest deserialized to null");

        TopComponents = ComponentsFile.Parser.ParseFrom(
            File.ReadAllBytes(Path.Combine(dir, "top-components.binpb")));
        BottomComponents = ComponentsFile.Parser.ParseFrom(
            File.ReadAllBytes(Path.Combine(dir, "bottom-components.binpb")));
        EdaData = EdaDataFile.Parser.ParseFrom(
            File.ReadAllBytes(Path.Combine(dir, "eda-data.binpb")));

        Index = BuildIndex(TopComponents, BottomComponents, EdaData);
        Model = ReadModel(TopComponents, BottomComponents, EdaData);
    }

    public ConnectivityGolden Golden { get; }

    public ConnectivityCaptureManifest Manifest { get; }

    public ComponentsFile TopComponents { get; }

    public ComponentsFile BottomComponents { get; }

    public EdaDataFile EdaData { get; }

    /// <summary>The builder's index — the golden (side, ordinal) lookup path.</summary>
    public ComponentDetailIndex Index { get; }

    /// <summary>The production view — the object under conformance test.</summary>
    public DesignProductModel Model { get; }

    /// <summary>
    /// The exact production construction of the component view, factored out so the
    /// UID-population test can rebuild over a mutated capture through the same code
    /// path.
    /// </summary>
    public static ComponentDetailIndex BuildIndex(ComponentsFile top, ComponentsFile bottom, EdaDataFile eda) =>
        new ComponentDetailBuilder(NullLogger<ComponentDetailBuilder>.Instance).Build(top, bottom, eda);

    /// <summary>
    /// Reassembles the captures into the <see cref="ProtoDesign"/> shape a gRPC
    /// <c>GetDesign</c> response carries and reads it through the production reader.
    /// </summary>
    public static DesignProductModel ReadModel(ComponentsFile top, ComponentsFile bottom, EdaDataFile eda)
    {
        var step = new StepDirectory { Edadatafile = eda };
        step.LayersByName["comp_+_top"] = new LayerDirectory { Components = top };
        step.LayersByName["comp_+_bot"] = new LayerDirectory { Components = bottom };

        var design = new ProtoDesign { FileModel = new FileArchive() };
        design.FileModel.StepsByName[Step] = step;
        return new ProductModelReader(NullLoggerFactory.Instance).Read(design, Step);
    }
}
