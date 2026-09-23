using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using Xunit;

namespace OdbDesignInfoClient.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="PinsTabViewModel"/> and <see cref="PinListItemViewModel"/>.
/// </summary>
public class PinsTabViewModelTests
{
    private readonly Mock<IDesignService> _mockDesignService = new();
    private readonly Mock<INavigationService> _mockNavigationService = new();
    private readonly Mock<ICrossProbeService> _mockCrossProbeService = new();
    private readonly PinsTabViewModel _sut;

    public PinsTabViewModelTests()
    {
        _sut = new PinsTabViewModel(
            _mockDesignService.Object, _mockNavigationService.Object, _mockCrossProbeService.Object);
    }

    private static Component ComponentWithPins(string refDes, int pinCount) => new()
    {
        RefDes = refDes,
        PartName = $"{refDes}-PART",
        Package = "R0603",
        Side = "Top",
        Pins = Enumerable.Range(1, pinCount).Select(i => new Pin
        {
            Name = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Number = i,
            NetName = i == 1 ? "GND" : $"NET{i}",
            X = i * 0.5,
            Y = -i * 0.25,
        }).ToList(),
    };

    [Fact]
    public async Task LoadAsync_FlattensComponentPins_WithOwnerContext()
    {
        // Arrange - two components with 2 and 1 pins
        _mockDesignService
            .Setup(x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([ComponentWithPins("R1", 2), ComponentWithPins("C1", 1)]);

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.Equal(3, _sut.Pins.Count);
        Assert.Equal(3, _sut.TotalCount);
        Assert.Equal(3, _sut.FilteredCount);
        Assert.False(_sut.IsLoading);

        var first = _sut.Pins[0];
        Assert.Equal("R1", first.RefDes);
        Assert.Equal("R1-PART", first.PartName);
        Assert.Equal("Top", first.Side);
        Assert.Equal("1", first.PinName);
        Assert.Equal("GND", first.NetName);
        Assert.Equal(0.5, first.X);
        Assert.Equal(-0.25, first.Y);
    }

    [Fact]
    public async Task LoadAsync_SecondActivationWithoutForce_DoesNotRefetch()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([ComponentWithPins("R1", 1)]);

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "step");

        // Assert - the load-once guard means the service is consulted exactly once
        _mockDesignService.Verify(
            x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoadAsync_ForceReload_ReFetches()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([ComponentWithPins("R1", 1)]);

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "step", forceReload: true);

        // Assert
        _mockDesignService.Verify(
            x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task LoadAsync_DifferentStep_Refetches()
    {
        // Arrange - the load-once guard keys on (design, step)
        _mockDesignService
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([ComponentWithPins("R1", 1)]);

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "panel");

        // Assert
        _mockDesignService.Verify(
            x => x.GetComponentsAsync("design-1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task LoadAsync_WhenServiceThrows_SetsVisibleError()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.True(_sut.HasError);
        Assert.Contains("Failed to load pins", _sut.ErrorMessage);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public async Task LoadAsync_OnAuthFailure_NamesEnvironmentVariables()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("401"));

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.True(_sut.HasError);
        Assert.Contains("ODBDESIGN_REST_USERNAME", _sut.ErrorMessage);
        Assert.Contains("ODBDESIGN_REST_PASSWORD", _sut.ErrorMessage);
    }

    [Fact]
    public async Task Filter_MatchesRefDesPinAndNet()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([ComponentWithPins("R1", 2), ComponentWithPins("C1", 1)]);
        await _sut.LoadAsync("design-1", "step");

        // Act & Assert - RefDes match keeps all of R1's pins
        _sut.FilterText = "r1";
        await WaitForFilterAppliedAsync(() => _sut.Pins.Count == 2);
        Assert.All(_sut.Pins, p => Assert.Equal("R1", p.RefDes));

        // Net match keeps only the GND rows
        _sut.FilterText = "gnd";
        await WaitForFilterAppliedAsync(() => _sut.Pins.Count == 1);
        Assert.Equal("GND", _sut.Pins[0].NetName);

        // Pin-name match
        _sut.FilterText = "2";
        await WaitForFilterAppliedAsync(() => _sut.Pins.Count == 2);
    }

    [Fact]
    public async Task ClearFilter_AppliesEmptyFilterImmediately_BypassingDebounce()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([ComponentWithPins("R1", 2), ComponentWithPins("C1", 1)]);
        await _sut.LoadAsync("design-1", "step");

        _sut.FilterText = "R1";
        await WaitForFilterAppliedAsync(() => _sut.Pins.Count == 2);
        Assert.Equal(2, _sut.Pins.Count); // only R1's pins

        // Act
        _sut.ClearFilterCommand.Execute(null);

        // Assert - the full list is back right away, without waiting out the debounce
        Assert.Equal(string.Empty, _sut.FilterText);
        Assert.Equal(3, _sut.Pins.Count);
        Assert.Equal(3, _sut.FilteredCount);
    }

    [Fact]
    public void PinListItemViewModel_NavigateToNet_DeepLinksToNetsTab()
    {
        // Arrange
        var pin = new Pin { Name = "1", Number = 1, NetName = "GND" };
        var row = new PinListItemViewModel("R1", "PART", "Top", pin, _mockNavigationService.Object);

        // Act
        row.NavigateToNet();

        // Assert
        _mockNavigationService.Verify(x => x.NavigateToEntity("net", "GND"), Times.Once);
    }

    /// <summary>Polls until the 300 ms filter debounce has applied (condition holds), bounded to ~5 s.</summary>
    private static async Task WaitForFilterAppliedAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(50);
        }
    }
}
