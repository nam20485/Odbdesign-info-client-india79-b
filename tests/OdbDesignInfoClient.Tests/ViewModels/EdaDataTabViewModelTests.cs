using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using Xunit;

namespace OdbDesignInfoClient.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="EdaDataTabViewModel"/>.
/// </summary>
public class EdaDataTabViewModelTests
{
    private readonly Mock<IDesignService> _mockDesignService = new();
    private readonly EdaDataTabViewModel _sut;

    public EdaDataTabViewModelTests()
    {
        _sut = new EdaDataTabViewModel(_mockDesignService.Object);
    }

    private static EdaDataSummary Summary() => new()
    {
        Units = "INCH",
        Source = "Mentor PowerPCB file",
        Path = "/OdbDesign/designs/d/d/steps/step/eda/data",
        LayerCount = 13,
        Nets =
        [
            new EdaNetSummary { Name = "GND", Index = 0, ToeprintCount = 40, TraceCount = 5, ViaCount = 12, PlaneCount = 1, AttributeCount = 2 },
            new EdaNetSummary { Name = "#7", Index = 7, ViaCount = 1 },
        ],
    };

    [Fact]
    public async Task LoadAsync_PopulatesHeaderAndNetRows()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetEdaDataSummaryAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Summary());

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.Equal(2, _sut.Nets.Count);
        Assert.Equal(2, _sut.TotalNets);
        Assert.Equal("INCH", _sut.Units);
        Assert.Equal("Mentor PowerPCB file", _sut.Source);
        Assert.Equal(13, _sut.LayerCount);
        Assert.False(_sut.IsEmpty);
        Assert.False(_sut.IsLoading);

        var gnd = _sut.Nets[0];
        Assert.Equal("GND", gnd.Name);
        Assert.Equal(40, gnd.ToeprintCount);
        Assert.Equal(12, gnd.ViaCount);
        Assert.Equal(1, gnd.PlaneCount);
        Assert.Equal(2, gnd.AttributeCount);
    }

    [Fact]
    public async Task LoadAsync_WithNoEdaData_ShowsHonestEmptyState()
    {
        // Arrange - the server exposes no eda/data file for the step
        _mockDesignService
            .Setup(x => x.GetEdaDataSummaryAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EdaDataSummary?)null);

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.Empty(_sut.Nets);
        Assert.True(_sut.IsEmpty);
        Assert.False(_sut.HasError);
        Assert.Equal(string.Empty, _sut.Units);
        Assert.Contains("no eda/data file", _sut.EmptyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_SecondActivationWithoutForce_DoesNotRefetch()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetEdaDataSummaryAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Summary());

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "step");

        // Assert - the load-once guard means the service is consulted exactly once
        _mockDesignService.Verify(
            x => x.GetEdaDataSummaryAsync("design-1", "step", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoadAsync_DifferentStep_Refetches()
    {
        // Arrange - the load-once guard keys on (design, step)
        _mockDesignService
            .Setup(x => x.GetEdaDataSummaryAsync("design-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Summary());

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "panel");

        // Assert
        _mockDesignService.Verify(
            x => x.GetEdaDataSummaryAsync("design-1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task LoadAsync_WhenServiceThrows_SetsVisibleError()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetEdaDataSummaryAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.True(_sut.HasError);
        Assert.Contains("Failed to load EDA data", _sut.ErrorMessage);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public async Task LoadAsync_OnAuthFailure_NamesEnvironmentVariables()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetEdaDataSummaryAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("401"));

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.True(_sut.HasError);
        Assert.Contains("ODBDESIGN_REST_PASSWORD", _sut.ErrorMessage);
    }
}
