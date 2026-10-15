using System.Net;
using Moq;
using Odb.Lib.Protobuf;
using Odb.Lib.Protobuf.ProductModel;
using OdbDesignInfoClient.Services;
using OdbDesignInfoClient.Services.Api;
using Refit;
using Xunit;
using FileArchive = Odb.Lib.Protobuf.FileArchive;
using ProtoDesign = Odb.Lib.Protobuf.ProductModel.Design;

namespace OdbDesignInfoClient.Tests.Services;

/// <summary>
/// Unit tests for DesignService. Design content reads go over gRPC via the
/// <see cref="IOdbDesignGrpcClient"/> seam (F3 transport switch); REST carries the
/// discovery plane only (design names, step names, health). The gRPC fixtures are
/// synthetic protobuf designs — no network, mirroring the
/// ComponentDetailBuilderTests pattern.
/// </summary>
public class DesignServiceTests
{
    private readonly Mock<IOdbDesignRestApi> _mockRestApi;
    private readonly Mock<IOdbDesignGrpcClient> _mockGrpcClient;
    private readonly DesignService _sut;

    public DesignServiceTests()
    {
        _mockRestApi = new Mock<IOdbDesignRestApi>();
        _mockGrpcClient = new Mock<IOdbDesignGrpcClient>();

        // Default: gRPC unavailable (exercises the degrade paths).
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(false);

        _sut = new DesignService(_mockGrpcClient.Object, _mockRestApi.Object);
    }

    // ---- Discovery plane (REST): design names + step names ----

    [Fact]
    public async Task GetDesignsAsync_ReturnsDesignList_WhenServerReturnsNames()
    {
        // Arrange
        var designNamesJson = """{"filearchives":[{"name":"design1","loaded":true},{"name":"design2","loaded":true}]}""";
        _mockRestApi.Setup(x => x.GetDesignNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(designNamesJson));
        var stepsJson = """["pcb"]""";
        _mockRestApi.Setup(x => x.GetStepsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(stepsJson));

        // Act
        var result = await _sut.GetDesignsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("design1", result[0].Name);
    }

    [Fact]
    public async Task GetDesignsAsync_ReturnsEmptyList_WhenServerReturnsNoContent()
    {
        // Arrange - the server returns 204 when no designs are loaded
        var response = new ApiResponse<string>(
            new HttpResponseMessage(HttpStatusCode.NoContent),
            null,
            new RefitSettings());
        _mockRestApi.Setup(x => x.GetDesignNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetDesignsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDesignAsync_ReturnsDesign_WhenDesignExists()
    {
        // Arrange
        var stepsJson = """["pcb", "panel"]""";
        _mockRestApi.Setup(x => x.GetStepsAsync("test-design", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(stepsJson));

        // Act
        var result = await _sut.GetDesignAsync("test-design");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-design", result.Name);
        Assert.Equal(2, result.Steps.Count);
    }

    // ---- Stackup (gRPC matrix file) ----

    [Fact]
    public async Task GetStackupAsync_MapsMatrixLayerRecords_WithAuthoritativeTypeColorAndStackOrder()
    {
        // Arrange - the matrix rides the gRPC file archive; layer types are the
        // proto enum (default = Signal), colors carry noPreference, drill rows
        // declare their span.
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithMatrix());

        // Act
        var result = await _sut.GetStackupAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Count);
        Assert.Equal("COMP_+_TOP", result[0].Name);
        Assert.Equal("Component", result[0].Type);
        // The enum default (0) is Signal, matching the JSON projection that
        // omitted the field for signal layers.
        Assert.Equal("Signal", result[2].Type);
        // Real design color when set; type-based default when "noPreference".
        Assert.Equal("#636337", result[3].ColorHex);
        Assert.Equal("#2196F3", result[1].ColorHex);
        // Drill span carried through ("" stays null in the domain model).
        Assert.Equal("LAYER-1", result[4].StartLayer);
        Assert.Equal("LAYER-8", result[4].EndLayer);
        Assert.Null(result[0].StartLayer);
        // Physical stack order preserved.
        Assert.Equal(1, result[0].StackOrder);
        Assert.Equal(5, result[4].StackOrder);
    }

    [Fact]
    public async Task GetStackupAsync_FallsBackToStepLayerNames_WhenMatrixAbsent()
    {
        // Arrange - the archive has no matrix file; the step's layer map still
        // names the layers (sorted by name), types guessed from the names.
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        var design = new ProtoDesign
        {
            FileModel = new FileArchive(),
        };
        design.FileModel.StepsByName["pcb"] = new StepDirectory();
        foreach (var name in new[] { "top_copper", "dielectric_1", "bottom_copper" })
        {
            design.FileModel.StepsByName["pcb"].LayersByName[name] = new LayerDirectory();
        }

        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(design);

        // Act
        var result = await _sut.GetStackupAsync("design-1", "pcb");

        // Assert - minimal stackup still built from names, no fabricated thickness/material
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("bottom_copper", result[0].Name);
        Assert.Equal("top_copper", result[2].Name);
        Assert.Equal(1, result[0].StackOrder);
        Assert.Null(result[0].Thickness);
        Assert.Null(result[0].Material);
    }

    [Fact]
    public async Task GetStackupAsync_ReturnsEmpty_WhenGrpcUnavailable()
    {
        // Arrange - the REST matrix route is gone with the transport switch;
        // without gRPC the tab is honestly empty.

        // Act
        var result = await _sut.GetStackupAsync("design-1", "pcb");

        // Assert
        Assert.Empty(result);
    }

    // ---- Step summaries (gRPC stephdr files) ----

    [Fact]
    public async Task GetStepSummariesAsync_MapsStepHdrFiles_FromFileArchive()
    {
        // Arrange - steps and their headers both live in the gRPC archive.
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        var design = new ProtoDesign { FileModel = new FileArchive() };
        design.FileModel.StepsByName["pcb"] = new StepDirectory
        {
            Stephdrfile = new StepHdrFile
            {
                Id = 5,
                XOrigin = 1.5,
                YOrigin = -2.5,
                XDatum = 0,
                YDatum = 0,
            },
        };
        design.FileModel.StepsByName["panel"] = new StepDirectory();

        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(design);

        // Act
        var result = await _sut.GetStepSummariesAsync("design-1");

        // Assert - ordinal key ordering is deterministic ("panel" sorts first)
        Assert.Equal(2, result.Count);

        var panel = result[0];
        Assert.Equal("panel", panel.Name);
        Assert.Null(panel.Id);
        Assert.Null(panel.XOrigin);
        Assert.Equal(0, panel.RepeatCount);

        var pcb = result[1];
        Assert.Equal("pcb", pcb.Name);
        Assert.Equal(5, pcb.Id);
        Assert.Equal(1.5, pcb.XOrigin);
        Assert.Equal(-2.5, pcb.YOrigin);
        Assert.Equal(0, pcb.XDatum);
        Assert.Equal(0, pcb.RepeatCount);
    }

    [Fact]
    public async Task GetStepSummariesAsync_CountsStepRepeatRecords()
    {
        // Arrange - a panelized step declares repeat records in its header
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        var design = new ProtoDesign { FileModel = new FileArchive() };
        design.FileModel.StepsByName["panel"] = new StepDirectory
        {
            Stephdrfile = new StepHdrFile
            {
                Id = 9,
                StepRepeatRecords =
                {
                    new StepHdrFile.Types.StepRepeatRecord { Name = "pcb", Nx = 2, Ny = 3 },
                    new StepHdrFile.Types.StepRepeatRecord { Name = "pcb" },
                },
            },
        };

        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(design);

        // Act
        var result = await _sut.GetStepSummariesAsync("design-1");

        // Assert
        Assert.Single(result);
        Assert.Equal(9, result[0].Id);
        Assert.Equal(2, result[0].RepeatCount);
    }

    [Fact]
    public async Task GetStepSummariesAsync_DegradesToRestNames_WhenGrpcUnavailable()
    {
        // Arrange - gRPC down: the REST discovery steps route still names the
        // steps; headers are unavailable so the rows degrade to bare names.
        _mockRestApi
            .Setup(x => x.GetStepsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("""["pcb","panel"]"""));

        // Act
        var result = await _sut.GetStepSummariesAsync("design-1");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("pcb", result[0].Name);
        Assert.Null(result[0].Id);
        Assert.Null(result[0].XOrigin);
    }

    [Fact]
    public async Task GetStepSummariesAsync_ReturnsEmptyList_WhenDesignNotFound()
    {
        // Arrange
        _mockRestApi
            .Setup(x => x.GetStepsAsync("unknown-design", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateErrorResponse(HttpStatusCode.NotFound));

        // Act
        var result = await _sut.GetStepSummariesAsync("unknown-design");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStepSummariesAsync_ThrowsUnauthorizedAccessException_OnAuthFailure()
    {
        // Arrange - the 401 surfacing contract lives on the remaining REST reads
        // (discovery); it must surface, never silently degrade to empty.
        _mockRestApi
            .Setup(x => x.GetStepsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateErrorResponse(HttpStatusCode.Unauthorized));

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.GetStepSummariesAsync("design-1"));
    }

    // ---- Symbols (gRPC symbols directories) ----

    [Fact]
    public async Task GetSymbolsAsync_ListsSymbolDirectoryNames_FromFileArchive()
    {
        // Arrange - the symbols library is design-wide; its names are the keys of
        // the file archive's symbols map (what the REST route listed).
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        var design = new ProtoDesign { FileModel = new FileArchive() };
        foreach (var name in new[] { "r10", "drill_symbol1", "drill_symbol1+1" })
        {
            design.FileModel.SymbolsDirectoriesByName[name] = new SymbolsDirectory();
        }

        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(design);

        // Act
        var result = await _sut.GetSymbolsAsync("design-1");

        // Assert - deterministic ordinal ordering
        Assert.Equal(3, result.Count);
        Assert.Equal("drill_symbol1", result[0].Name);
        Assert.Equal("drill_symbol1+1", result[1].Name);
        Assert.Equal("r10", result[2].Name);
    }

    [Fact]
    public async Task GetSymbolsAsync_ReturnsEmpty_WhenGrpcUnavailable()
    {
        // Arrange - the REST symbols route is gone with the transport switch

        // Act
        var result = await _sut.GetSymbolsAsync("design-1");

        // Assert
        Assert.Empty(result);
    }

    // ---- EDA data summary (gRPC eda_data file) ----

    [Fact]
    public async Task GetEdaDataSummaryAsync_SummarizesEdaDataFile_FromFileArchive()
    {
        // Arrange - the step's EDA data file rides the gRPC archive
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithEdaData());

        // Act
        var result = await _sut.GetEdaDataSummaryAsync("design-1", "step");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("INCH", result!.Units);
        Assert.Equal("Mentor PowerPCB file", result.Source);
        Assert.Equal(3, result.LayerCount);
        Assert.Equal(2, result.Nets.Count);

        var gnd = result.Nets[0];
        Assert.Equal("GND", gnd.Name);
        Assert.Equal(1, gnd.ToeprintCount);
        Assert.Equal(2, gnd.ViaCount);
        Assert.Equal(1, gnd.PlaneCount);
        Assert.Equal(1, gnd.TraceCount);
        Assert.Equal(1, gnd.AttributeCount);

        // Unnamed nets display as "#Index"; attribute-lookup entries count as attributes
        var unnamed = result.Nets[1];
        Assert.Equal("#7", unnamed.Name);
        Assert.Equal(1, unnamed.ToeprintCount);
        Assert.Equal(2, unnamed.AttributeCount);
    }

    [Fact]
    public async Task GetEdaDataSummaryAsync_ReturnsNull_WhenStepHasNoEdaData()
    {
        // Arrange - the step has no eda/data file in the archive
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        var design = new ProtoDesign { FileModel = new FileArchive() };
        design.FileModel.StepsByName["step"] = new StepDirectory();
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(design);

        // Act
        var result = await _sut.GetEdaDataSummaryAsync("design-1", "step");

        // Assert - null drives the tab's honest empty state
        Assert.Null(result);
    }

    [Fact]
    public async Task GetEdaDataSummaryAsync_ReturnsNull_WhenGrpcUnavailable()
    {
        // Act
        var result = await _sut.GetEdaDataSummaryAsync("design-1", "step");

        // Assert
        Assert.Null(result);
    }

    // ---- Components / nets (gRPC product model) ----

    [Fact]
    public async Task GetComponentsAsync_MapsProductModel_FromSyntheticArchive()
    {
        // Arrange - one top component joined to its net through the eda toeprint
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithProductModel());

        // Act
        var result = await _sut.GetComponentsAsync("design-1", "step");

        // Assert
        var component = Assert.Single(result);
        Assert.Equal("R1", component.RefDes);
        Assert.Equal("RES-SMD", component.PartName);
        Assert.Equal("Top", component.Side);
        var pin = Assert.Single(component.Pins);
        Assert.Equal("VCC", pin.NetName);
    }

    [Fact]
    public async Task GetComponentsAsync_ReturnsEmpty_WhenGrpcUnavailable()
    {
        // Arrange - the REST components projection was deleted with the transport
        // switch (D5); without gRPC the tab is honestly empty.

        // Act
        var result = await _sut.GetComponentsAsync("design-1", "pcb");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetNetsAsync_MapsProductModelWithViaCounts_FromSyntheticArchive()
    {
        // Arrange
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithProductModel());

        // Act
        var result = await _sut.GetNetsAsync("design-1", "step");

        // Assert
        var net = Assert.Single(result);
        Assert.Equal("VCC", net.Name);
        Assert.Equal(1, net.PinCount);
        Assert.Equal(2, net.ViaCount);
        var feature = Assert.Single(net.Features);
        Assert.Equal("R1-1", feature.Id);
        Assert.Equal("R1", feature.ComponentRef);
    }

    [Fact]
    public async Task GetNetsAsync_ReturnsEmpty_WhenGrpcUnavailable()
    {
        // Act
        var result = await _sut.GetNetsAsync("design-1", "pcb");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetViaSummariesAsync_ReturnsEmpty_WithoutGrpcProductModel()
    {
        // Arrange - gRPC unavailable (REST nets exposed no via information)

        // Act
        var result = await _sut.GetViaSummariesAsync("design-1", "pcb");

        // Assert - honestly empty rather than fabricated counts
        Assert.Empty(result);
    }

    // ---- Packages / parts / drill tools / via summaries (gRPC product model) ----

    [Fact]
    public async Task GetPackagesAsync_MapsPackageGeometryAndUsage_FromProductModel()
    {
        // Arrange
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithProductModel());

        // Act
        var result = await _sut.GetPackagesAsync("design-1", "step");

        // Assert
        var package = Assert.Single(result);
        Assert.Equal("RES-SMD", package.Name);
        Assert.Equal(2, package.PinCount);
        Assert.Equal(2, package.Width, 3);
        Assert.Equal(1, package.Height, 3);
        // No pitch declared: null, never an authoritative 0.000 mm
        Assert.Null(package.Pitch);
        var usage = Assert.Single(package.Usages);
        Assert.Equal("R1", usage.ComponentRefDes);
    }

    [Fact]
    public async Task GetPartsAsync_MapsPartUsage_FromProductModel()
    {
        // Arrange
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithProductModel());

        // Act
        var result = await _sut.GetPartsAsync("design-1", "step");

        // Assert
        var part = Assert.Single(result);
        Assert.Equal("RES-SMD", part.PartNumber);
        Assert.Equal(1, part.UsageCount);
        var usage = Assert.Single(part.Usages);
        Assert.Equal("R1", usage.ComponentRefDes);
    }

    [Fact]
    public async Task GetDrillToolsAsync_MapsToolsFile_FromProductModel()
    {
        // Arrange
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithProductModel());

        // Act
        var result = await _sut.GetDrillToolsAsync("design-1", "step");

        // Assert - map iteration order must not leak: tools sort by tool number
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].ToolNumber);
        Assert.Equal(0.5, result[0].Diameter, 3);
        Assert.True(result[0].IsPlated);
        Assert.Equal(2, result[1].ToolNumber);
        Assert.False(result[1].IsPlated);
    }

    [Fact]
    public async Task GetViaSummariesAsync_ReportsPerNetViaCounts_FromProductModel()
    {
        // Arrange
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithProductModel());

        // Act
        var result = await _sut.GetViaSummariesAsync("design-1", "step");

        // Assert
        var via = Assert.Single(result);
        Assert.Equal("VCC", via.NetName);
        Assert.Equal(2, via.ViaCount);
        Assert.Equal(1, via.PinCount);
    }

    // ---- Archive sharing + caching semantics ----

    [Fact]
    public async Task ContentReads_ShareOneGrpcDownload_PerDesign()
    {
        // Arrange - every content tab derives from the same cached file archive;
        // a refresh fan-out must not issue one GetDesign per tab.
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithMatrix());

        // Act
        _ = await _sut.GetStackupAsync("design-1", "pcb");
        _ = await _sut.GetSymbolsAsync("design-1");
        _ = await _sut.GetStepSummariesAsync("design-1");

        // Assert
        _mockGrpcClient.Verify(
            x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProductModel_SecondCallWithinCacheWindow_SkipsGrpcFetch()
    {
        // Arrange
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithProductModel());

        // Act
        _ = await _sut.GetComponentsAsync("design-1", "step");
        _ = await _sut.GetComponentsAsync("design-1", "step");

        // Assert - both calls succeed from one download (cache hit)
        _mockGrpcClient.Verify(
            x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ClearCache_ForcesRefetchOnNextCall()
    {
        // Arrange
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithMatrix());

        // Act
        _ = await _sut.GetSymbolsAsync("design-1");
        _sut.ClearCache();
        _ = await _sut.GetSymbolsAsync("design-1");

        // Assert - cache cleared, second load hits the server again
        _mockGrpcClient.Verify(
            x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetStackupAsync_DifferentDesign_BypassesCacheAndRefetches()
    {
        // Arrange - the archive cache keys on the design
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithMatrix());
        _mockGrpcClient.Setup(x => x.GetDesignAsync("design-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDesignWithMatrix());

        // Act
        _ = await _sut.GetStackupAsync("design-1", "pcb");
        _ = await _sut.GetStackupAsync("design-2", "pcb");

        // Assert
        _mockGrpcClient.Verify(
            x => x.GetDesignAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GrpcFetchFailure_DegradesToEmptyWithoutCaching()
    {
        // Arrange - a transport failure must degrade honestly and not poison the
        // cache: the next call fetches again.
        var recoveredDesign = new ProtoDesign { FileModel = new FileArchive() };
        recoveredDesign.FileModel.SymbolsDirectoriesByName["r10"] = new SymbolsDirectory();
        _mockGrpcClient.SetupSequence(x => x.IsAvailable)
            .Returns(true)
            .Returns(true);
        _mockGrpcClient.SetupSequence(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "gRPC down")))
            .ReturnsAsync(recoveredDesign);

        // Act
        var failed = await _sut.GetSymbolsAsync("design-1");
        var recovered = await _sut.GetSymbolsAsync("design-1");

        // Assert
        Assert.Empty(failed);
        var symbol = Assert.Single(recovered);
        Assert.Equal("r10", symbol.Name);
    }

    [Fact]
    public async Task ClearCache_DuringInFlightFetch_DiscardsSupersededSnapshot()
    {
        // Arrange - a refresh that lands while a GetDesign download is in flight
        // must not adopt or re-cache the pre-clear snapshot: the download runs on
        // CancellationToken.None, so cancellation alone cannot stop it.
        var superseded = new ProtoDesign { FileModel = new FileArchive() };
        superseded.FileModel.SymbolsDirectoriesByName["stale"] = new SymbolsDirectory();
        var fresh = new ProtoDesign { FileModel = new FileArchive() };
        fresh.FileModel.SymbolsDirectoriesByName["fresh"] = new SymbolsDirectory();

        var inFlight = new TaskCompletionSource<ProtoDesign?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockGrpcClient.Setup(x => x.IsAvailable).Returns(true);
        _mockGrpcClient.SetupSequence(x => x.GetDesignAsync("design-1", It.IsAny<CancellationToken>()))
            .Returns(inFlight.Task)
            .ReturnsAsync(fresh);

        // Act
        var supersededRead = _sut.GetSymbolsAsync("design-1");
        _sut.ClearCache();
        inFlight.SetResult(superseded);
        var freshRead = await _sut.GetSymbolsAsync("design-1");

        // Assert - the superseded download's result flows to its own (cancelled)
        // caller but is neither cached nor adopted by the post-refresh read
        Assert.Equal("stale", (await supersededRead).Single().Name);
        Assert.Equal("fresh", freshRead.Single().Name);
    }

    #region Test Helpers

    /// <summary>
    /// Creates a successful ApiResponse with the given JSON content.
    /// </summary>
    private static ApiResponse<string> CreateSuccessResponse(string content)
    {
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content),
        };
        return new ApiResponse<string>(
            httpResponse,
            content,
            new RefitSettings());
    }

    /// <summary>
    /// Creates an error ApiResponse with the given HTTP status code.
    /// </summary>
    private static ApiResponse<string> CreateErrorResponse(HttpStatusCode statusCode)
    {
        var httpResponse = new HttpResponseMessage(statusCode);
        return new ApiResponse<string>(
            httpResponse,
            null,
            new RefitSettings());
    }

    /// <summary>
    /// A design whose matrix mirrors the live sample_design shape: component and
    /// solder-mask rows, a signal row with no explicit type, a dielectric with a
    /// real color, and a drill row with its span.
    /// </summary>
    private static ProtoDesign CreateDesignWithMatrix()
    {
        var design = new ProtoDesign
        {
            FileModel = new FileArchive
            {
                MatrixFile = new MatrixFile
                {
                    Layers =
                    {
                        LayerRow(1, MatrixFile.Types.LayerRecord.Types.Type.Component, "COMP_+_TOP", noPreference: true),
                        LayerRow(2, MatrixFile.Types.LayerRecord.Types.Type.SolderMask, "SOLDERMASK-TOP", noPreference: true),
                        LayerRow(3, MatrixFile.Types.LayerRecord.Types.Type.Signal, "LAYER-1", noPreference: true),
                        LayerRow(4, MatrixFile.Types.LayerRecord.Types.Type.Dielectric, "DIELECTRIC1",
                            color: new Color { Red = 99, Green = 99, Blue = 55 }),
                        LayerRow(5, MatrixFile.Types.LayerRecord.Types.Type.Drill, "DRILL",
                            startName: "LAYER-1", endName: "LAYER-8", noPreference: true),
                    },
                },
            },
        };
        return design;
    }

    private static MatrixFile.Types.LayerRecord LayerRow(
        uint row,
        MatrixFile.Types.LayerRecord.Types.Type type,
        string name,
        bool noPreference = false,
        Color? color = null,
        string startName = "",
        string endName = "") => new()
    {
        Row = row,
        Type = type,
        Name = name,
        Color = color ?? new Color { Red = 0, Green = 0, Blue = 0, NoPreference = noPreference },
        StartName = startName,
        EndName = endName,
    };

    /// <summary>
    /// A design whose step carries one top component file plus an EDA data file
    /// with one net (a toeprint to that component, two vias) — the minimal input
    /// the shared product-model reader joins. The package record and drill layer
    /// feed the packages/parts/drill-tools mappings.
    /// </summary>
    private static ProtoDesign CreateDesignWithProductModel()
    {
        var design = new ProtoDesign { FileModel = new FileArchive() };
        var step = new StepDirectory
        {
            Edadatafile = new EdaDataFile
            {
                Units = "MM",
                NetRecords =
                {
                    new EdaDataFile.Types.NetRecord
                    {
                        Name = "VCC",
                        Index = 0,
                        SubnetRecords =
                        {
                            new EdaDataFile.Types.NetRecord.Types.SubnetRecord
                            {
                                Type = EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Toeprint,
                                ComponentNumber = 0,
                                ToeprintNumber = 0,
                                Side = BoardSide.Top,
                            },
                            new EdaDataFile.Types.NetRecord.Types.SubnetRecord
                            {
                                Type = EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Via,
                            },
                            new EdaDataFile.Types.NetRecord.Types.SubnetRecord
                            {
                                Type = EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Via,
                            },
                        },
                    },
                },
                PackageRecords =
                {
                    new EdaDataFile.Types.PackageRecord
                    {
                        Name = "RES-SMD",
                        XMin = -1,
                        YMin = -0.5,
                        XMax = 1,
                        YMax = 0.5,
                        PinRecords =
                        {
                            new EdaDataFile.Types.PackageRecord.Types.PinRecord { Name = "1" },
                            new EdaDataFile.Types.PackageRecord.Types.PinRecord { Name = "2" },
                        },
                    },
                },
            },
        };
        step.LayersByName["comp_+_top"] = new LayerDirectory
        {
            Components = new ComponentsFile
            {
                Units = "mm",
                ComponentRecords =
                {
                    new ComponentsFile.Types.ComponentRecord
                    {
                        CompName = "R1",
                        PartName = "RES-SMD",
                        PkgRef = 0,
                        ToeprintRecords =
                        {
                            new ComponentsFile.Types.ComponentRecord.Types.ToeprintRecord
                            {
                                PinNumber = 1,
                                NetNumber = 0,
                            },
                        },
                    },
                },
            },
        };
        step.LayersByName["drill"] = new LayerDirectory
        {
            ToolFile = new ToolsFile
            {
                Units = "mm",
                Tools =
                {
                    [2] = new ToolsFile.Types.ToolsRecord
                    {
                        ToolNum = 2,
                        DrillSize = 0.8,
                        FinishSize = 0.8,
                        Type = ToolsFile.Types.ToolsRecord.Types.Type.NonPlated,
                    },
                    [1] = new ToolsFile.Types.ToolsRecord
                    {
                        ToolNum = 1,
                        DrillSize = 0.5,
                        FinishSize = 0.5,
                        Type = ToolsFile.Types.ToolsRecord.Types.Type.Plated,
                    },
                },
            },
        };
        design.FileModel.StepsByName["step"] = step;
        return design;
    }

    /// <summary>
    /// A design whose step carries an EDA data file mirroring the live server's
    /// shape: header fields, two nets (one named, one unnamed with an index).
    /// </summary>
    private static ProtoDesign CreateDesignWithEdaData()
    {
        var design = new ProtoDesign { FileModel = new FileArchive() };
        design.FileModel.StepsByName["step"] = new StepDirectory
        {
            Edadatafile = new EdaDataFile
            {
                Path = "/OdbDesign/designs/d/d/steps/step/eda/data",
                Units = "INCH",
                Source = "Mentor PowerPCB file",
                LayerNames = { "layer-1", "drill", "layer-8" },
                NetRecords =
                {
                    new EdaDataFile.Types.NetRecord
                    {
                        Name = "GND",
                        Index = 0,
                        SubnetRecords =
                        {
                            new EdaDataFile.Types.NetRecord.Types.SubnetRecord
                            {
                                Type = EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Toeprint,
                                Side = BoardSide.Top,
                                ComponentNumber = 0,
                            },
                            new EdaDataFile.Types.NetRecord.Types.SubnetRecord
                            {
                                Type = EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Via,
                            },
                            new EdaDataFile.Types.NetRecord.Types.SubnetRecord
                            {
                                Type = EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Via,
                            },
                            new EdaDataFile.Types.NetRecord.Types.SubnetRecord
                            {
                                Type = EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Plane,
                            },
                            new EdaDataFile.Types.NetRecord.Types.SubnetRecord
                            {
                                Type = EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Trace,
                            },
                        },
                        PropertyRecords = { new PropertyRecord { Name = ".net_class", Value = "PWR" } },
                    },
                    new EdaDataFile.Types.NetRecord
                    {
                        Index = 7,
                        SubnetRecords =
                        {
                            new EdaDataFile.Types.NetRecord.Types.SubnetRecord
                            {
                                Type = EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Toeprint,
                                Side = BoardSide.Top,
                                ComponentNumber = 4,
                            },
                        },
                        AttributeLookupTable =
                        {
                            ["0"] = "clock",
                            ["1"] = "",
                        },
                    },
                },
            },
        };
        return design;
    }

    #endregion
}
