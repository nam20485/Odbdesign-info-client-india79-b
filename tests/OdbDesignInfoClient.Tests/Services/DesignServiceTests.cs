using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Services;
using Xunit;

namespace OdbDesignInfoClient.Tests.Services;

/// <summary>
/// Unit tests for DesignService.
/// </summary>
public class DesignServiceTests
{
    private readonly Mock<IConnectionService> _mockConnectionService;
    private readonly DesignService _sut;

    public DesignServiceTests()
    {
        _mockConnectionService = new Mock<IConnectionService>();
        _sut = new DesignService(_mockConnectionService.Object);
    }

    [Fact]
    public async Task GetDesignsAsync_ReturnsEmptyList_WhenNoDesignsAvailable()
    {
        // Act
        var result = await _sut.GetDesignsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDesignAsync_ReturnsNull_WhenDesignNotFound()
    {
        // Act
        var result = await _sut.GetDesignAsync("non-existent-id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetComponentsAsync_ReturnsEmptyList_WhenNoComponentsAvailable()
    {
        // Act
        var result = await _sut.GetComponentsAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetNetsAsync_ReturnsEmptyList_WhenNoNetsAvailable()
    {
        // Act
        var result = await _sut.GetNetsAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStackupAsync_ReturnsEmptyList_WhenNoLayersAvailable()
    {
        // Act
        var result = await _sut.GetStackupAsync("design-1", "pcb");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
