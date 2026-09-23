using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using Xunit;

namespace OdbDesignInfoClient.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="ComponentsTabViewModel"/> deep-link navigation and filter clearing.
/// </summary>
public class ComponentsTabViewModelTests
{
    private readonly Mock<IDesignService> _mockDesignService = new();
    private readonly Mock<INavigationService> _mockNavigationService = new();
    private readonly Mock<ICrossProbeService> _mockCrossProbeService = new();
    private readonly ComponentsTabViewModel _sut;

    public ComponentsTabViewModelTests()
    {
        _sut = new ComponentsTabViewModel(
            _mockDesignService.Object,
            _mockNavigationService.Object,
            _mockCrossProbeService.Object);
    }

    [Fact]
    public async Task NavigateToComponent_WithActiveFilter_ClearsFilter_AndSelectsAndExpandsTarget()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Component("R1", "10K", "0402"), Component("C2", "100N", "0603")]);
        await _sut.LoadAsync("design-1", "step");

        _sut.FilterText = "R1";
        await WaitForFilterAppliedAsync(() => _sut.Components.Count == 1);
        Assert.Single(_sut.Components); // the filter has actually applied

        // Act
        _sut.NavigateToComponent("C2");

        // Assert - the deep-link leaves the list unfiltered and selects + expands the target
        Assert.Equal(string.Empty, _sut.FilterText);
        Assert.Equal(2, _sut.Components.Count);
        Assert.Equal(2, _sut.FilteredCount);
        Assert.NotNull(_sut.SelectedComponent);
        Assert.Equal("C2", _sut.SelectedComponent.RefDes);
        Assert.True(_sut.SelectedComponent.IsExpanded);
    }

    [Fact]
    public async Task NavigateToComponent_WithoutActiveFilter_KeepsFilterEmpty_AndSelectsTarget()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Component("R1", "10K", "0402"), Component("C2", "100N", "0603")]);
        await _sut.LoadAsync("design-1", "step");

        // Act
        _sut.NavigateToComponent("r1"); // case-insensitive RefDes match

        // Assert - the RefDes never leaks into the filter box
        Assert.Equal(string.Empty, _sut.FilterText);
        Assert.Equal(_sut.TotalCount, _sut.FilteredCount);
        Assert.NotNull(_sut.SelectedComponent);
        Assert.Equal("R1", _sut.SelectedComponent.RefDes);
        Assert.True(_sut.SelectedComponent.IsExpanded);
    }

    [Fact]
    public async Task ClearFilter_AppliesEmptyFilterImmediately_BypassingDebounce()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetComponentsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Component("R1", "10K", "0402"), Component("C2", "100N", "0603")]);
        await _sut.LoadAsync("design-1", "step");

        _sut.FilterText = "R1";
        await WaitForFilterAppliedAsync(() => _sut.Components.Count == 1);
        Assert.Single(_sut.Components); // filtered down to one row

        // Act
        _sut.ClearFilterCommand.Execute(null);

        // Assert - the full list is back right away, without waiting out the debounce
        Assert.Equal(string.Empty, _sut.FilterText);
        Assert.Equal(2, _sut.Components.Count);
        Assert.Equal(2, _sut.FilteredCount);
    }

    /// <summary>Polls until the 300 ms filter debounce has applied (condition holds), bounded to ~5 s.</summary>
    private static async Task WaitForFilterAppliedAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(50);
        }
    }

    private static Component Component(string refDes, string partName, string packageName) => new()
    {
        RefDes = refDes,
        PartName = partName,
        Package = packageName,
    };
}
