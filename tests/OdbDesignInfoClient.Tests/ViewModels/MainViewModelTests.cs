using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using Xunit;

namespace OdbDesignInfoClient.Tests.ViewModels;

/// <summary>
/// Unit tests for the MainViewModel step selector: selecting a design populates
/// the step list, and a user-driven step change acts as a context change
/// (cache clear + reload of the active tab for the new step).
/// </summary>
public class MainViewModelTests
{
    private readonly Mock<IConnectionService> _mockConnectionService = new();
    private readonly Mock<IDesignService> _mockDesignService = new();
    private readonly Mock<INavigationService> _mockNavigationService = new();
    private readonly Mock<ICrossProbeService> _mockCrossProbeService = new();
    private readonly MainViewModel _sut;

    public MainViewModelTests()
    {
        // Tab data loads must succeed with empty results so fire-and-forget
        // loads settle quickly and never surface tab error banners.
        _mockDesignService
            .Setup(x => x.GetComponentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockDesignService
            .Setup(x => x.GetNetsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockDesignService
            .Setup(x => x.GetStackupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockDesignService
            .Setup(x => x.GetDrillToolsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockDesignService
            .Setup(x => x.GetPackagesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockDesignService
            .Setup(x => x.GetPartsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockDesignService
            .Setup(x => x.GetViaSummariesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockDesignService
            .Setup(x => x.GetStepSummariesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockDesignService
            .Setup(x => x.GetSymbolsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockDesignService
            .Setup(x => x.GetEdaDataSummaryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EdaDataSummary?)null);

        _sut = CreateSut();
    }

    private MainViewModel CreateSut()
    {
        var components = new ComponentsTabViewModel(
            _mockDesignService.Object, _mockNavigationService.Object, _mockCrossProbeService.Object);
        var nets = new NetsTabViewModel(
            _mockDesignService.Object, _mockNavigationService.Object, _mockCrossProbeService.Object);
        var stackup = new StackupTabViewModel(_mockDesignService.Object);
        var drillTools = new DrillToolsTabViewModel(_mockDesignService.Object);
        var packages = new PackagesTabViewModel(_mockDesignService.Object, _mockNavigationService.Object);
        var parts = new PartsTabViewModel(_mockDesignService.Object, _mockNavigationService.Object);
        var pins = new PinsTabViewModel(
            _mockDesignService.Object, _mockNavigationService.Object, _mockCrossProbeService.Object);
        var vias = new ViasTabViewModel(_mockDesignService.Object, _mockNavigationService.Object);
        var stepHierarchy = new StepHierarchyTabViewModel(_mockDesignService.Object);
        var symbols = new SymbolsTabViewModel(_mockDesignService.Object);
        var edaData = new EdaDataTabViewModel(_mockDesignService.Object);

        return new MainViewModel(
            _mockConnectionService.Object,
            _mockDesignService.Object,
            _mockNavigationService.Object,
            _mockCrossProbeService.Object,
            new ServerConnectionConfig(),
            components, nets, stackup, drillTools, packages, parts,
            pins, vias, stepHierarchy, symbols, edaData);
    }

    private static Design MultiStepDesign() => new()
    {
        Id = "design-1",
        Name = "design-1",
        Steps = ["pcb", "panel"],
    };

    [Fact]
    public void SettingSelectedDesign_PopulatesStepSelector_WithFirstStepActive()
    {
        // Act
        _sut.SelectedDesign = MultiStepDesign();

        // Assert
        Assert.Equal(2, _sut.Steps.Count);
        Assert.Equal("pcb", _sut.SelectedStep);
    }

    [Fact]
    public async Task ChangingSelectedStep_ClearsCache_AndReloadsActiveTabForNewStep()
    {
        // Arrange
        _sut.SelectedDesign = MultiStepDesign();
        await WaitUntilAsync(() => _mockDesignService.Invocations
            .Any(i => i.Method.Name == nameof(IDesignService.GetComponentsAsync)));

        // Act - user picks the second step in the selector (context change)
        _sut.SelectedStep = "panel";
        await WaitUntilAsync(() => _mockDesignService.Invocations
            .Any(i => i.Method.Name == nameof(IDesignService.GetComponentsAsync) &&
                i.Arguments[1] as string == "panel"));

        // Assert - the active tab (Components, index 0) was loaded for the NEW step
        _mockDesignService.Verify(
            x => x.GetComponentsAsync("design-1", "panel", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        // The step change cleared the design-service cache (context change)
        _mockDesignService.Verify(x => x.ClearCache(), Times.AtLeast(2));

        // Other tabs were NOT force-loaded; they reload lazily via their guards
        _mockDesignService.Verify(
            x => x.GetStepSummariesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChangingSelectedStep_RearmsLoadOnceGuard_ForOtherTabs()
    {
        // Arrange - land on the Nets tab and load it for the first step
        _sut.SelectedTabIndex = 1;
        _sut.SelectedDesign = MultiStepDesign();
        await WaitUntilAsync(() => _mockDesignService.Invocations
            .Any(i => i.Method.Name == nameof(IDesignService.GetNetsAsync)));

        // Act - switch step, then revisit the Nets tab
        _sut.SelectedStep = "panel";
        _sut.SelectedTabIndex = 1;
        await WaitUntilAsync(() => _mockDesignService.Invocations
            .Count(i => i.Method.Name == nameof(IDesignService.GetNetsAsync) &&
                i.Arguments[1] as string == "panel") >= 1);

        // Assert - Nets reloaded because its load-once guard keys on (design, step)
        _mockDesignService.Verify(
            x => x.GetNetsAsync("design-1", "panel", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task DeepLink_LoadsTargetTab_ForCurrentlySelectedStep()
    {
        // Arrange - select the second step explicitly
        _sut.SelectedDesign = MultiStepDesign();
        await WaitUntilAsync(() => _mockDesignService.Invocations
            .Any(i => i.Method.Name == nameof(IDesignService.GetComponentsAsync)));
        _sut.SelectedStep = "panel";
        await WaitUntilAsync(() => _mockDesignService.Invocations
            .Any(i => i.Method.Name == nameof(IDesignService.GetComponentsAsync) &&
                i.Arguments[1] as string == "panel"));

        // Act - deep-link to a net (same mechanism the Pins/Vias tabs use)
        _mockNavigationService.Raise(
            x => x.Navigated += null,
            new NavigationEventArgs { TabIndex = 1, EntityType = "net", EntityId = "GND" });
        await WaitUntilAsync(() => _mockDesignService.Invocations
            .Any(i => i.Method.Name == nameof(IDesignService.GetNetsAsync) &&
                i.Arguments[1] as string == "panel"));

        // Assert - the deep-link load used the selected step, not the first step
        _mockDesignService.Verify(
            x => x.GetNetsAsync("design-1", "panel", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        Assert.Equal(1, _sut.SelectedTabIndex);
    }

    [Fact]
    public async Task StepHierarchyActivation_SetsSelectedStep()
    {
        // Arrange - the Step Hierarchy tab lists two steps
        _mockDesignService
            .Setup(x => x.GetStepSummariesAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StepSummary { Name = "pcb" }, new StepSummary { Name = "panel" }]);
        _sut.SelectedDesign = MultiStepDesign();
        _sut.SelectedTabIndex = 8; // Step Hierarchy tab
        await WaitUntilAsync(() => _sut.StepHierarchyTab.Steps.Count == 2);

        // Act - the row's "Set Active" button promotes the step to the global context
        _sut.StepHierarchyTab.Steps[1].SetActiveCommand.Execute(null);

        // Assert
        Assert.Equal("panel", _sut.SelectedStep);
    }

    /// <summary>Polls until the condition holds, bounded to ~5 s (fire-and-forget loads).</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(50);
        }
    }
}
