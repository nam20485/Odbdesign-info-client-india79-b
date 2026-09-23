using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using Xunit;

namespace OdbDesignInfoClient.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="NetsTabViewModel"/> deep-link navigation and filter clearing.
/// </summary>
public class NetsTabViewModelTests
{
    private readonly Mock<IDesignService> _mockDesignService = new();
    private readonly Mock<INavigationService> _mockNavigationService = new();
    private readonly Mock<ICrossProbeService> _mockCrossProbeService = new();
    private readonly NetsTabViewModel _sut;

    public NetsTabViewModelTests()
    {
        _sut = new NetsTabViewModel(
            _mockDesignService.Object,
            _mockNavigationService.Object,
            _mockCrossProbeService.Object);
    }

    [Fact]
    public async Task NavigateToNet_WithActiveFilter_ClearsFilter_AndSelectsAndExpandsTarget()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetNetsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Net("GND"), Net("CLK_25MHZ")]);
        await _sut.LoadAsync("design-1", "step");

        _sut.FilterText = "GND";
        await WaitForFilterAppliedAsync(() => _sut.Nets.Count == 1);
        Assert.Single(_sut.Nets); // the filter has actually applied

        // Act
        _sut.NavigateToNet("CLK_25MHZ");

        // Assert - the deep-link leaves the list unfiltered and selects + expands the target
        Assert.Equal(string.Empty, _sut.FilterText);
        Assert.Equal(2, _sut.Nets.Count);
        Assert.Equal(2, _sut.FilteredCount);
        Assert.NotNull(_sut.SelectedNet);
        Assert.Equal("CLK_25MHZ", _sut.SelectedNet.Name);
        Assert.True(_sut.SelectedNet.IsExpanded);
    }

    [Fact]
    public async Task NavigateToNet_WithoutActiveFilter_KeepsFilterEmpty_AndSelectsTarget()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetNetsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Net("GND"), Net("CLK_25MHZ")]);
        await _sut.LoadAsync("design-1", "step");

        // Act
        _sut.NavigateToNet("gnd"); // case-insensitive name match

        // Assert - the net name never leaks into the filter box
        Assert.Equal(string.Empty, _sut.FilterText);
        Assert.Equal(_sut.TotalCount, _sut.FilteredCount);
        Assert.NotNull(_sut.SelectedNet);
        Assert.Equal("GND", _sut.SelectedNet.Name);
        Assert.True(_sut.SelectedNet.IsExpanded);
    }

    [Fact]
    public async Task ClearFilter_AppliesEmptyFilterImmediately_BypassingDebounce()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetNetsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Net("GND"), Net("CLK_25MHZ")]);
        await _sut.LoadAsync("design-1", "step");

        _sut.FilterText = "GND";
        await WaitForFilterAppliedAsync(() => _sut.Nets.Count == 1);
        Assert.Single(_sut.Nets); // filtered down to one row

        // Act
        _sut.ClearFilterCommand.Execute(null);

        // Assert - the full list is back right away, without waiting out the debounce
        Assert.Equal(string.Empty, _sut.FilterText);
        Assert.Equal(2, _sut.Nets.Count);
        Assert.Equal(2, _sut.FilteredCount);
    }

    [Fact]
    public async Task NavigateToNet_SendsExactlyOneNetHighlightCrossProbe()
    {
        // Arrange
        _mockCrossProbeService.Setup(x => x.IsConnected).Returns(true);
        _mockDesignService
            .Setup(x => x.GetNetsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Net("GND"), Net("CLK_25MHZ")]);
        await _sut.LoadAsync("design-1", "step");

        // Act - deep-link into the Nets tab
        _sut.NavigateToNet("gnd");

        // Assert - the deep-link converges on the selection-change handler (Arch §5.3
        // step 6): exactly one net highlight message per user action, symmetric to the
        // component path's select message, and no component select on net navigation
        _mockCrossProbeService.Verify(
            x => x.HighlightNetAsync("GND", It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockCrossProbeService.Verify(
            x => x.SelectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>Polls until the 300 ms filter debounce has applied (condition holds), bounded to ~5 s.</summary>
    private static async Task WaitForFilterAppliedAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(50);
        }
    }

    private static Net Net(string name) => new()
    {
        Name = name,
        PinCount = 2,
    };
}
