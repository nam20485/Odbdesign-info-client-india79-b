using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Services;
using OdbDesignInfoClient.Services.Api;
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

    public DesignServiceTests()
    {
        _mockConnectionService = new Mock<IConnectionService>();
        _mockRestApi = new Mock<IOdbDesignRestApi>();
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
    public async Task GetComponentsAsync_ReturnsEmptyList_WhenNoGrpcOrRest()
    {
        // Arrange
        _mockConnectionService.Setup(x => x.IsGrpcAvailable).Returns(false);
        _mockRestApi.Setup(x => x.GetComponentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });

        // Act
        var result = await _sut.GetComponentsAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetNetsAsync_ReturnsEmptyList_WhenNoGrpcOrRest()
    {
        // Arrange
        _mockConnectionService.Setup(x => x.IsGrpcAvailable).Returns(false);
        _mockRestApi.Setup(x => x.GetNetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });

        // Act
        var result = await _sut.GetNetsAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
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
}
