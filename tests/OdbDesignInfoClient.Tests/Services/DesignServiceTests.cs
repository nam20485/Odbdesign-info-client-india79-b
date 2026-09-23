using System.Net;
using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Services;
using OdbDesignInfoClient.Services.Api;
using Refit;
using Xunit;

namespace OdbDesignInfoClient.Tests.Services;

/// <summary>
/// Unit tests for DesignService.
/// </summary>
public class DesignServiceTests
{
    private readonly Mock<IConnectionService> _mockConnectionService;
    private readonly Mock<IOdbDesignRestApi> _mockRestApi;
    private readonly DesignService _sut;

    /// <summary>
    /// Sample JSON for component deserialization tests.
    /// </summary>
    private const string SampleComponentsJson = """
        [
          {
            "refDes": "U1",
            "partName": "STM32F407VGT6",
            "package": {
              "name": "LQFP-100",
              "pins": [
                { "name": "1", "index": 0 },
                { "name": "2", "index": 1 }
              ]
            },
            "index": 0,
            "side": "TOP",
            "part": {
              "name": "STM32F407VGT6"
            }
          },
          {
            "refDes": "R1",
            "partName": "10K",
            "package": { "name": "0402" },
            "index": 1,
            "side": "BOTTOM"
          }
        ]
        """;

    /// <summary>
    /// Sample JSON for net deserialization tests.
    /// </summary>
    private const string SampleNetsJson = """
        [
          {
            "name": "GND",
            "index": 0,
            "pinConnections": [
              {
                "name": "U1-1-GND",
                "component": { "refDes": "U1" },
                "pin": { "name": "1", "index": 0 }
              }
            ]
          },
          {
            "name": "+3.3V",
            "index": 1,
            "pinConnections": []
          }
        ]
        """;

    /// <summary>
    /// Sample design-matrix (stackup) JSON, mirroring the live server projection shape:
    /// sparse optional fields (type absent = Signal, drill span, real color) per layer.
    /// </summary>
    private const string SampleMatrixJson = """
        {
          "steps": [{ "column": 1, "id": 19, "name": "STEP" }],
          "layers": [
            { "row": 1, "type": "Component", "name": "COMP_+_TOP", "color": { "red": 0, "green": 0, "blue": 0, "noPreference": true } },
            { "row": 2, "type": "SolderMask", "name": "SOLDERMASK-TOP", "color": { "red": 0, "green": 0, "blue": 0, "noPreference": true } },
            { "row": 3, "name": "LAYER-1", "color": { "red": 0, "green": 0, "blue": 0, "noPreference": true } },
            { "row": 4, "type": "Dielectric", "name": "DIELECTRIC1", "color": { "red": 99, "green": 99, "blue": 55, "noPreference": false } },
            { "row": 5, "type": "Drill", "name": "DRILL", "startName": "LAYER-1", "endName": "LAYER-8", "color": { "red": 0, "green": 0, "blue": 0, "noPreference": true } }
          ]
        }
        """;

    public DesignServiceTests()
    {
        _mockConnectionService = new Mock<IConnectionService>();
        _mockRestApi = new Mock<IOdbDesignRestApi>();

        // Default: gRPC unavailable to test REST path
        _mockConnectionService.Setup(x => x.IsGrpcAvailable).Returns(false);

        _sut = new DesignService(_mockConnectionService.Object, _mockRestApi.Object);
    }

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

    [Fact]
    public async Task GetComponentsAsync_DeserializesRestJson_WhenGrpcUnavailable()
    {
        // Arrange
        var response = CreateSuccessResponse(SampleComponentsJson);
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetComponentsAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);

        var u1 = result.First(c => c.RefDes == "U1");
        Assert.Equal("STM32F407VGT6", u1.PartName);
        Assert.Equal("LQFP-100", u1.Package);
        Assert.Equal("Top", u1.Side);
        Assert.Equal(2, u1.Pins.Count);

        var r1 = result.First(c => c.RefDes == "R1");
        Assert.Equal("10K", r1.PartName);
        Assert.Equal("0402", r1.Package);
        Assert.Equal("Bottom", r1.Side);
    }

    [Fact]
    public async Task GetComponentsAsync_ReturnsEmptyList_WhenResponseIsEmptyArray()
    {
        // Arrange
        var response = CreateSuccessResponse("[]");
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetComponentsAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetComponentsAsync_ThrowsInvalidOperationException_OnMalformedJson()
    {
        // Arrange
        var response = CreateSuccessResponse("{ invalid json }");
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.GetComponentsAsync("design-1", "pcb"));
    }

    [Fact]
    public async Task GetComponentsAsync_ReturnsEmptyList_WhenDesignNotFound()
    {
        // Arrange
        var response = CreateErrorResponse(HttpStatusCode.NotFound);
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("unknown-design", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetComponentsAsync("unknown-design", "pcb");

        // Assert — 404 now returns empty list for graceful UX
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetComponentsAsync_ThrowsUnauthorizedAccessException_OnAuthFailure()
    {
        // Arrange
        var response = CreateErrorResponse(HttpStatusCode.Unauthorized);
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await _sut.GetComponentsAsync("design-1", "pcb"));
    }

    [Fact]
    public async Task GetComponentsAsync_ThrowsInvalidOperationException_WhenServerErrors()
    {
        // Arrange
        var response = CreateErrorResponse(HttpStatusCode.InternalServerError);
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.GetComponentsAsync("design-1", "pcb"));
        Assert.Contains("Server error", ex.Message);
    }

    [Fact]
    public async Task GetComponentsAsync_ThrowsWithJsonDetails_OnMalformedJson()
    {
        // Arrange - Invalid JSON with unmatched braces
        var response = CreateSuccessResponse("{ \"refDes\": \"U1\", ");
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.GetComponentsAsync("design-1", "pcb"));
        Assert.Contains("Failed to parse components response", ex.Message);
    }

    [Fact]
    public async Task GetComponentsAsync_HandlesMissingOptionalFields_Gracefully()
    {
        // Arrange - Component with minimal fields
        var minimalJson = """
            [
              {
                "refDes": "U1"
              }
            ]
            """;
        var response = CreateSuccessResponse(minimalJson);
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetComponentsAsync("design-1", "pcb");

        // Assert
        Assert.Single(result);
        var component = result[0];
        Assert.Equal("U1", component.RefDes);
        Assert.Equal(string.Empty, component.PartName);
        Assert.Equal(string.Empty, component.Package);
        Assert.Equal(string.Empty, component.Side);
        Assert.Empty(component.Pins);
    }

    [Fact]
    public async Task GetNetsAsync_DeserializesRestJson_WhenGrpcUnavailable()
    {
        // Arrange
        var response = CreateSuccessResponse(SampleNetsJson);
        _mockRestApi
            .Setup(x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetNetsAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);

        var gnd = result.First(n => n.Name == "GND");
        Assert.Equal(1, gnd.PinCount);
        Assert.Single(gnd.Features);
        Assert.Equal("U1-1-GND", gnd.Features[0].Id);
        Assert.Equal("U1", gnd.Features[0].ComponentRef);
        Assert.Equal("Pin", gnd.Features[0].FeatureType);

        var vcc = result.First(n => n.Name == "+3.3V");
        Assert.Equal(0, vcc.PinCount);
        Assert.Empty(vcc.Features);
    }

    [Fact]
    public async Task GetNetsAsync_ReturnsEmptyList_WhenResponseIsEmptyArray()
    {
        // Arrange
        var response = CreateSuccessResponse("[]");
        _mockRestApi
            .Setup(x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetNetsAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetNetsAsync_HandlesManyPinConnections_Correctly()
    {
        // Arrange
        var manyConnectionsJson = """
            [
              {
                "name": "GND",
                "index": 0,
                "pinConnections": [
                  { "name": "U1-1", "component": { "refDes": "U1" } },
                  { "name": "U2-1", "component": { "refDes": "U2" } },
                  { "name": "C1-2", "component": { "refDes": "C1" } },
                  { "name": "C2-2", "component": { "refDes": "C2" } },
                  { "name": "R1-1", "component": { "refDes": "R1" } }
                ]
              }
            ]
            """;
        var response = CreateSuccessResponse(manyConnectionsJson);
        _mockRestApi
            .Setup(x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetNetsAsync("design-1", "pcb");

        // Assert
        Assert.Single(result);
        Assert.Equal(5, result[0].PinCount);
        Assert.Equal(5, result[0].Features.Count);
    }

    [Fact]
    public async Task GetNetsAsync_ReturnsEmptyList_WhenDesignNotFound()
    {
        // Arrange
        var response = CreateErrorResponse(HttpStatusCode.NotFound);
        _mockRestApi
            .Setup(x => x.GetNetsAsync("unknown-design", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetNetsAsync("unknown-design", "pcb");

        // Assert — 404 now returns empty list for graceful UX
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetNetsAsync_ThrowsUnauthorizedAccessException_OnAuthFailure()
    {
        // Arrange
        var response = CreateErrorResponse(HttpStatusCode.Unauthorized);
        _mockRestApi
            .Setup(x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await _sut.GetNetsAsync("design-1", "pcb"));
    }

    [Fact]
    public async Task GetNetsAsync_ThrowsInvalidOperationException_WhenServerErrors()
    {
        // Arrange
        var response = CreateErrorResponse(HttpStatusCode.InternalServerError);
        _mockRestApi
            .Setup(x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.GetNetsAsync("design-1", "pcb"));
        Assert.Contains("Server error", ex.Message);
    }

    [Fact]
    public async Task GetNetsAsync_ThrowsInvalidOperationException_OnMalformedJson()
    {
        // Arrange
        var response = CreateSuccessResponse("{ invalid json }");
        _mockRestApi
            .Setup(x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.GetNetsAsync("design-1", "pcb"));
    }

    [Fact]
    public async Task GetNetsAsync_HandlesMissingOptionalFields_Gracefully()
    {
        // Arrange - Net with minimal fields
        var minimalJson = """
            [
              {
                "name": "NET1"
              }
            ]
            """;
        var response = CreateSuccessResponse(minimalJson);
        _mockRestApi
            .Setup(x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetNetsAsync("design-1", "pcb");

        // Assert
        Assert.Single(result);
        var net = result[0];
        Assert.Equal("NET1", net.Name);
        Assert.Equal(0, net.PinCount);
        Assert.Empty(net.Features);
    }

    [Fact]
    public async Task GetStackupAsync_ParsesMatrixLayers_WithAuthoritativeTypeColorAndStackOrder()
    {
        // Arrange
        _mockRestApi.Setup(x => x.GetMatrixAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(SampleMatrixJson));

        // Act
        var result = await _sut.GetStackupAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Count);
        Assert.Equal("COMP_+_TOP", result[0].Name);
        Assert.Equal("Component", result[0].Type);
        // Absent "type" field means a Signal layer (server omits the default enum).
        Assert.Equal("Signal", result[2].Type);
        // Real server color when set; type-based default when "noPreference".
        Assert.Equal("#636337", result[3].ColorHex);
        Assert.Equal("#2196F3", result[1].ColorHex);
        // Drill span carried through.
        Assert.Equal("LAYER-1", result[4].StartLayer);
        Assert.Equal("LAYER-8", result[4].EndLayer);
        // Physical stack order preserved.
        Assert.Equal(1, result[0].StackOrder);
        Assert.Equal(5, result[4].StackOrder);
    }

    [Fact]
    public async Task GetStackupAsync_FallsBackToLayerNames_WhenMatrixUnavailable()
    {
        // Arrange - the matrix route fails but the layer-name list is available
        _mockRestApi.Setup(x => x.GetMatrixAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateErrorResponse(HttpStatusCode.NotFound));
        var layerNamesJson = """["top_copper", "dielectric_1", "bottom_copper"]""";
        _mockRestApi.Setup(x => x.GetLayerNamesAsync("design-1", "pcb", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(layerNamesJson));

        // Act
        var result = await _sut.GetStackupAsync("design-1", "pcb");

        // Assert - minimal stackup still built from names, no fabricated thickness/material
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("top_copper", result[0].Name);
        Assert.Equal(1, result[0].StackOrder);
        Assert.Null(result[0].Thickness);
        Assert.Null(result[0].Material);
    }

    [Fact]
    public async Task GetStackupAsync_ParsesEnvelopeFormat_InLayerNameFallback()
    {
        // Arrange - the layer-name list arrives wrapped in a "layers" envelope
        _mockRestApi.Setup(x => x.GetMatrixAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateErrorResponse(HttpStatusCode.NotFound));
        var envelopeJson = """{"layers":["board-outline.doc","comp_+_top","dielectric1","layer-1","soldermask-top"]}""";
        _mockRestApi.Setup(x => x.GetLayerNamesAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(envelopeJson));

        // Act
        var result = await _sut.GetStackupAsync("design-1", "step");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Count);
        Assert.Equal("board-outline.doc", result[0].Name);
        Assert.Equal("soldermask-top", result[4].Name);
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
    public async Task GetComponentsAsync_SecondCallWithinCacheWindow_SkipsHttpRequest()
    {
        // Arrange
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(SampleComponentsJson));

        // Act
        var first = await _sut.GetComponentsAsync("design-1", "pcb");
        var second = await _sut.GetComponentsAsync("design-1", "pcb");

        // Assert - both calls succeed from one HTTP round trip (cache hit)
        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        _mockRestApi.Verify(
            x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetNetsAsync_SecondCallWithinCacheWindow_SkipsHttpRequest()
    {
        // Arrange
        _mockRestApi
            .Setup(x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(SampleNetsJson));

        // Act
        _ = await _sut.GetNetsAsync("design-1", "pcb");
        _ = await _sut.GetNetsAsync("design-1", "pcb");

        // Assert
        _mockRestApi.Verify(
            x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetStackupAsync_SecondCallWithinCacheWindow_SkipsHttpRequest()
    {
        // Arrange
        _mockRestApi.Setup(x => x.GetMatrixAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(SampleMatrixJson));

        // Act
        _ = await _sut.GetStackupAsync("design-1", "pcb");
        _ = await _sut.GetStackupAsync("design-1", "pcb");

        // Assert
        _mockRestApi.Verify(
            x => x.GetMatrixAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }


    [Fact]
    public async Task ClearCache_ForcesRefetchOnNextCall()
    {
        // Arrange
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(SampleComponentsJson));

        // Act
        _ = await _sut.GetComponentsAsync("design-1", "pcb");
        _sut.ClearCache();
        _ = await _sut.GetComponentsAsync("design-1", "pcb");

        // Assert - cache cleared, second load hits the server again
        _mockRestApi.Verify(
            x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetComponentsAsync_DifferentStep_BypassesCacheAndRefetches()
    {
        // Arrange - step is part of the cache key
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(SampleComponentsJson));

        // Act
        _ = await _sut.GetComponentsAsync("design-1", "pcb");
        _ = await _sut.GetComponentsAsync("design-1", "panel");

        // Assert
        _mockRestApi.Verify(
            x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetSymbolsAsync_ParsesEnvelopeFormat_FromLiveServerShape()
    {
        // Arrange - the observed live shape: { "symbols": ["drill_symbol1", ...] }
        var response = CreateSuccessResponse("""{"symbols":["drill_symbol1","drill_symbol1+1","r10"]}""");
        _mockRestApi
            .Setup(x => x.GetSymbolNamesAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetSymbolsAsync("design-1");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("drill_symbol1", result[0].Name);
        Assert.Equal("r10", result[2].Name);
    }

    [Fact]
    public async Task GetSymbolsAsync_ParsesBareArray_WhenServerReturnsPlainList()
    {
        // Arrange - tolerate a bare string array as well
        var response = CreateSuccessResponse("""["r10","rect20x30"]""");
        _mockRestApi
            .Setup(x => x.GetSymbolNamesAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetSymbolsAsync("design-1");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("rect20x30", result[1].Name);
    }

    [Fact]
    public async Task GetSymbolsAsync_ReturnsEmptyList_WhenDesignNotFound()
    {
        // Arrange
        _mockRestApi
            .Setup(x => x.GetSymbolNamesAsync("unknown-design", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateErrorResponse(HttpStatusCode.NotFound));

        // Act
        var result = await _sut.GetSymbolsAsync("unknown-design");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSymbolsAsync_ThrowsUnauthorizedAccessException_OnAuthFailure()
    {
        // Arrange
        _mockRestApi
            .Setup(x => x.GetSymbolNamesAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateErrorResponse(HttpStatusCode.Unauthorized));

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.GetSymbolsAsync("design-1"));
    }

    [Fact]
    public async Task GetSymbolsAsync_SecondCallWithinCacheWindow_SkipsHttpRequest()
    {
        // Arrange
        _mockRestApi
            .Setup(x => x.GetSymbolNamesAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("""{"symbols":["r10"]}"""));

        // Act
        _ = await _sut.GetSymbolsAsync("design-1");
        _ = await _sut.GetSymbolsAsync("design-1");

        // Assert
        _mockRestApi.Verify(
            x => x.GetSymbolNamesAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetStepSummariesAsync_JoinsStepsWithHeaderMetadata()
    {
        // Arrange - steps list plus per-step stephdr projections (live shapes)
        _mockRestApi
            .Setup(x => x.GetStepsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("""["pcb","panel"]"""));
        _mockRestApi
            .Setup(x => x.GetStepHdrAsync("design-1", "pcb", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(
                """{"xDatum":0,"yDatum":0,"id":5,"xOrigin":1.5,"yOrigin":-2.5,"topActive":0}"""));
        _mockRestApi
            .Setup(x => x.GetStepHdrAsync("design-1", "panel", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateErrorResponse(HttpStatusCode.NotFound));

        // Act
        var result = await _sut.GetStepSummariesAsync("design-1");

        // Assert
        Assert.Equal(2, result.Count);

        var pcb = result[0];
        Assert.Equal("pcb", pcb.Name);
        Assert.Equal(5, pcb.Id);
        Assert.Equal(1.5, pcb.XOrigin);
        Assert.Equal(-2.5, pcb.YOrigin);
        Assert.Equal(0, pcb.XDatum);
        Assert.Equal(0, pcb.RepeatCount);

        // A missing stephdr degrades the row to a bare step name, never fails the tab
        var panel = result[1];
        Assert.Equal("panel", panel.Name);
        Assert.Null(panel.Id);
        Assert.Null(panel.XOrigin);
        Assert.Equal(0, panel.RepeatCount);
    }

    [Fact]
    public async Task GetStepSummariesAsync_ParsesStepRepeatRecords()
    {
        // Arrange - a panelized step declares repeat records in its header
        _mockRestApi
            .Setup(x => x.GetStepsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("""["panel"]"""));
        _mockRestApi
            .Setup(x => x.GetStepHdrAsync("design-1", "panel", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(
                """{"id":9,"stepRepeatRecords":[{"name":"pcb","nx":2,"ny":3},{"name":"pcb","nx":1,"ny":1}]}"""));

        // Act
        var result = await _sut.GetStepSummariesAsync("design-1");

        // Assert
        Assert.Single(result);
        Assert.Equal(9, result[0].Id);
        Assert.Equal(2, result[0].RepeatCount);
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
    public async Task GetStepSummariesAsync_SecondCallWithinCacheWindow_SkipsHttpRequests()
    {
        // Arrange
        _mockRestApi
            .Setup(x => x.GetStepsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("""["pcb"]"""));
        _mockRestApi
            .Setup(x => x.GetStepHdrAsync("design-1", "pcb", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("""{"id":5}"""));

        // Act
        _ = await _sut.GetStepSummariesAsync("design-1");
        _ = await _sut.GetStepSummariesAsync("design-1");

        // Assert
        _mockRestApi.Verify(
            x => x.GetStepsAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRestApi.Verify(
            x => x.GetStepHdrAsync("design-1", "pcb", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetViaSummariesAsync_ReturnsEmpty_WithoutGrpcProductModel()
    {
        // Arrange - gRPC unavailable (REST nets expose no via information)

        // Act
        var result = await _sut.GetViaSummariesAsync("design-1", "pcb");

        // Assert - honestly empty rather than fabricated counts
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEdaDataSummaryAsync_ParsesHeaderAndPerNetSubnetCounts()
    {
        // Arrange - a trimmed eda_data payload with the live server's shape
        var edaJson = """
            {
              "path": "/OdbDesign/designs/d/d/steps/step/eda/data",
              "units": "INCH",
              "source": "Mentor PowerPCB file",
              "layerNames": ["layer-1", "drill", "layer-8"],
              "netRecords": [
                {
                  "name": "GND",
                  "index": 0,
                  "subnetRecords": [
                    { "type": "TOEPRINT", "side": "Top", "componentNumber": 0 },
                    { "type": "VIA" },
                    { "type": "VIA" },
                    { "type": "PLANE" },
                    { "type": "TRACE" }
                  ],
                  "propertyRecords": [{ "name": ".net_class", "value": "PWR" }]
                },
                {
                  "index": 7,
                  "subnetRecords": [
                    { "type": "TOEPRINT", "side": "Top", "componentNumber": 4 }
                  ],
                  "attributeLookupTable": { "0": "clock", "1": "" }
                }
              ]
            }
            """;
        _mockRestApi
            .Setup(x => x.GetEdaDataAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse(edaJson));

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
    public async Task GetEdaDataSummaryAsync_ReturnsNull_WhenNoEdaDataExists()
    {
        // Arrange - the step has no eda/data file on the server
        _mockRestApi
            .Setup(x => x.GetEdaDataAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateErrorResponse(HttpStatusCode.NotFound));

        // Act
        var result = await _sut.GetEdaDataSummaryAsync("design-1", "step");

        // Assert - null drives the tab's honest empty state
        Assert.Null(result);
    }

    [Fact]
    public async Task GetEdaDataSummaryAsync_ThrowsUnauthorizedAccessException_OnAuthFailure()
    {
        // Arrange
        _mockRestApi
            .Setup(x => x.GetEdaDataAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateErrorResponse(HttpStatusCode.Unauthorized));

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.GetEdaDataSummaryAsync("design-1", "step"));
    }

    [Fact]
    public async Task GetEdaDataSummaryAsync_SecondCallWithinCacheWindow_SkipsHttpRequest()
    {
        // Arrange
        _mockRestApi
            .Setup(x => x.GetEdaDataAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("""{"units":"INCH","netRecords":[]}"""));

        // Act
        _ = await _sut.GetEdaDataSummaryAsync("design-1", "step");
        _ = await _sut.GetEdaDataSummaryAsync("design-1", "step");

        // Assert
        _mockRestApi.Verify(
            x => x.GetEdaDataAsync("design-1", "step", It.IsAny<CancellationToken>()),
            Times.Once);
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

    #endregion
}
