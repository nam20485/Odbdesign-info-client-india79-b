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
        var designNames = new List<string> { "design1", "design2" };
        _mockRestApi.Setup(x => x.GetDesignNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(designNames);
        _mockRestApi.Setup(x => x.GetStepsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "pcb" });

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
        var steps = new List<string> { "pcb", "panel" };
        _mockRestApi.Setup(x => x.GetStepsAsync("test-design", It.IsAny<CancellationToken>()))
            .ReturnsAsync(steps);

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
    public async Task GetComponentsAsync_ThrowsInvalidOperationException_WhenDesignNotFound()
    {
        // Arrange
        var response = CreateErrorResponse(HttpStatusCode.NotFound);
        _mockRestApi
            .Setup(x => x.GetComponentsAsync("unknown-design", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.GetComponentsAsync("unknown-design", "pcb"));
        Assert.Contains("not found", ex.Message);
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
    public async Task GetNetsAsync_ThrowsInvalidOperationException_WhenDesignNotFound()
    {
        // Arrange
        var response = CreateErrorResponse(HttpStatusCode.NotFound);
        _mockRestApi
            .Setup(x => x.GetNetsAsync("unknown-design", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.GetNetsAsync("unknown-design", "pcb"));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task GetStackupAsync_ReturnsLayers_WhenServerReturnsLayerNames()
    {
        // Arrange
        var layerNames = new List<string> { "top_copper", "dielectric_1", "bottom_copper" };
        _mockRestApi.Setup(x => x.GetLayerNamesAsync("design-1", "pcb", It.IsAny<CancellationToken>()))
            .ReturnsAsync(layerNames);

        // Act
        var result = await _sut.GetStackupAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("top_copper", result[0].Name);
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
