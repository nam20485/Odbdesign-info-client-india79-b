using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using Xunit;

namespace OdbDesignInfoClient.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="ViasTabViewModel"/> and <see cref="ViaRowViewModel"/>.
/// </summary>
public class ViasTabViewModelTests
{
    private readonly Mock<IDesignService> _mockDesignService = new();
    private readonly Mock<INavigationService> _mockNavigationService = new();
    private readonly ViasTabViewModel _sut;

    public ViasTabViewModelTests()
    {
        _sut = new ViasTabViewModel(_mockDesignService.Object, _mockNavigationService.Object);
    }

    [Fact]
    public async Task LoadAsync_PopulatesRows_AndAggregatesTotals()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetViaSummariesAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ViaSummary { NetName = "GND", NetIndex = 0, ViaCount = 12, PinCount = 40 },
                new ViaSummary { NetName = "VCC", NetIndex = 1, ViaCount = 3, PinCount = 8 },
            ]);

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.Equal(2, _sut.Vias.Count);
        Assert.Equal(2, _sut.NetsWithVias);
        Assert.Equal(15, _sut.TotalViaCount);
        Assert.False(_sut.IsEmpty);
        Assert.False(_sut.IsLoading);

        var gnd = _sut.Vias.First(v => v.NetName == "GND");
        Assert.Equal(12, gnd.ViaCount);
        Assert.Equal(40, gnd.PinCount);
        Assert.Equal(0u, gnd.NetIndex);
    }

    [Fact]
    public async Task LoadAsync_WithNoVias_ShowsHonestEmptyState()
    {
        // Arrange - a design/step without VIA subnets (or without the product model)
        _mockDesignService
            .Setup(x => x.GetViaSummariesAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.Empty(_sut.Vias);
        Assert.True(_sut.IsEmpty);
        Assert.Equal(0, _sut.TotalViaCount);
        Assert.False(_sut.HasError);
        Assert.Contains("VIA subnets", _sut.EmptyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_SecondActivationWithoutForce_DoesNotRefetch()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetViaSummariesAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ViaSummary { NetName = "GND", ViaCount = 1 }]);

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "step");

        // Assert - the load-once guard means the service is consulted exactly once
        _mockDesignService.Verify(
            x => x.GetViaSummariesAsync("design-1", "step", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoadAsync_ForceReload_ReFetches()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetViaSummariesAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ViaSummary { NetName = "GND", ViaCount = 1 }]);

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "step", forceReload: true);

        // Assert
        _mockDesignService.Verify(
            x => x.GetViaSummariesAsync("design-1", "step", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task LoadAsync_DifferentStep_Refetches()
    {
        // Arrange - the load-once guard keys on (design, step)
        _mockDesignService
            .Setup(x => x.GetViaSummariesAsync("design-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "panel");

        // Assert
        _mockDesignService.Verify(
            x => x.GetViaSummariesAsync("design-1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task LoadAsync_WhenServiceThrows_SetsVisibleError()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetViaSummariesAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.True(_sut.HasError);
        Assert.Contains("Failed to load vias", _sut.ErrorMessage);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public void ViaRowViewModel_NavigateToNet_DeepLinksToNetsTab()
    {
        // Arrange
        var row = new ViaRowViewModel(
            new ViaSummary { NetName = "GND", ViaCount = 2 },
            _mockNavigationService.Object);

        // Act
        row.NavigateToNet();

        // Assert
        _mockNavigationService.Verify(x => x.NavigateToEntity("net", "GND"), Times.Once);
    }
}
