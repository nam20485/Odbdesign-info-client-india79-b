using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using Xunit;

namespace OdbDesignInfoClient.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="StackupTabViewModel"/> and <see cref="LayerRowViewModel"/>.
/// </summary>
public class StackupTabViewModelTests
{
    private readonly Mock<IDesignService> _mockDesignService = new();
    private readonly StackupTabViewModel _sut;

    public StackupTabViewModelTests()
    {
        _sut = new StackupTabViewModel(_mockDesignService.Object);
    }

    [Fact]
    public async Task LoadAsync_PopulatesLayers_AndReportsCount()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetStackupAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MatrixLayer(1, "LAYER-1", "Signal")]);

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.Single(_sut.Layers);
        Assert.Equal(1, _sut.TotalLayers);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public async Task LoadAsync_SecondActivationWithoutForce_DoesNotRefetch()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetStackupAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MatrixLayer(1, "LAYER-1", "Signal")]);

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "step");

        // Assert - the load-once guard means the service is consulted exactly once
        _mockDesignService.Verify(
            x => x.GetStackupAsync("design-1", "step", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoadAsync_ForceReload_ReFetches()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetStackupAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MatrixLayer(1, "LAYER-1", "Signal")]);

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "step", forceReload: true);

        // Assert
        _mockDesignService.Verify(
            x => x.GetStackupAsync("design-1", "step", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task LoadAsync_WhenServiceThrows_SetsVisibleError()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetStackupAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.True(_sut.HasError);
        Assert.Contains("Failed to load stackup", _sut.ErrorMessage);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public void LayerRowViewModel_UsesServerColorAndFormatsDrillSpan()
    {
        // Arrange - a drill layer with a real server color and span
        var drill = new Layer
        {
            Id = 5,
            StackOrder = 5,
            Name = "DRILL",
            Type = "Drill",
            ColorHex = "#9C27B0",
            StartLayer = "LAYER-1",
            EndLayer = "LAYER-8",
        };

        // Act
        var row = new LayerRowViewModel(drill);

        // Assert
        Assert.Equal("#9C27B0", row.TypeColor);
        Assert.Equal("LAYER-1 → LAYER-8", row.DrillSpan);
        Assert.True(row.HasDrillSpan);
        Assert.Equal(5, row.StackOrder);
    }

    [Fact]
    public void LayerRowViewModel_RendersPlaceholders_WhenServerOmitsFields()
    {
        // Arrange - a layer with no thickness/material/polarity (current server behavior)
        var layer = new Layer
        {
            Id = 3,
            StackOrder = 3,
            Name = "LAYER-1",
            Type = "Signal",
            ColorHex = "#4CAF50",
        };

        // Act
        var row = new LayerRowViewModel(layer);

        // Assert - unavailable fields show an em dash, never a fabricated "0.000 mm"
        Assert.Equal("—", row.Thickness);
        Assert.Equal("—", row.Material);
        Assert.Equal("—", row.Polarity);
        Assert.False(row.HasDrillSpan);
        Assert.Empty(row.DrillSpan);
    }

    private static Layer MatrixLayer(int row, string name, string type) => new()
    {
        Id = row,
        StackOrder = row,
        Name = name,
        Type = type,
        ColorHex = "#4CAF50",
    };
}
