using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using Xunit;

namespace OdbDesignInfoClient.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="StepHierarchyTabViewModel"/> and <see cref="StepRowViewModel"/>.
/// </summary>
public class StepHierarchyTabViewModelTests
{
    private readonly Mock<IDesignService> _mockDesignService = new();
    private readonly StepHierarchyTabViewModel _sut;

    public StepHierarchyTabViewModelTests()
    {
        _sut = new StepHierarchyTabViewModel(_mockDesignService.Object);
    }

    [Fact]
    public async Task LoadAsync_PopulatesSteps_WithHeaderMetadata()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetStepSummariesAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StepSummary { Name = "pcb", Id = 5, XOrigin = 1.5, YOrigin = -2.5, RepeatCount = 0 },
                new StepSummary { Name = "panel" },
            ]);

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.Equal(2, _sut.Steps.Count);
        Assert.Equal(2, _sut.TotalSteps);
        Assert.False(_sut.IsEmpty);
        Assert.False(_sut.IsLoading);

        var pcb = _sut.Steps[0];
        Assert.Equal("pcb", pcb.Name);
        Assert.Equal("5", pcb.Id);
        Assert.Equal("1.500", pcb.XOrigin);
        Assert.Equal("-2.500", pcb.YOrigin);

        // Missing header fields degrade to an em dash, never a fabricated value
        var panel = _sut.Steps[1];
        Assert.Equal("—", panel.Id);
        Assert.Equal("—", panel.XOrigin);
        Assert.Equal("—", panel.YDatum);
    }

    [Fact]
    public async Task LoadAsync_SecondActivationWithoutForce_DoesNotRefetch()
    {
        // Arrange - the guard keys on the design (step data is design-scoped)
        _mockDesignService
            .Setup(x => x.GetStepSummariesAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StepSummary { Name = "pcb" }]);

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "other-step");

        // Assert - same design, no refetch even for a different active step
        _mockDesignService.Verify(
            x => x.GetStepSummariesAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoadAsync_DifferentDesign_Refetches()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetStepSummariesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StepSummary { Name = "pcb" }]);

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-2", "step");

        // Assert
        _mockDesignService.Verify(
            x => x.GetStepSummariesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task LoadAsync_WhenServiceThrows_SetsVisibleError()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetStepSummariesAsync("design-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.True(_sut.HasError);
        Assert.Contains("Failed to load steps", _sut.ErrorMessage);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public async Task StepActivation_RaisesEvent_WithStepName()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetStepSummariesAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StepSummary { Name = "pcb" }, new StepSummary { Name = "panel" }]);
        await _sut.LoadAsync("design-1", "step");

        string? requested = null;
        _sut.StepActivationRequested += (_, step) => requested = step;

        // Act - the row's Set Active button raises through the parent VM
        _sut.Steps[1].SetActiveCommand.Execute(null);

        // Assert
        Assert.Equal("panel", requested);
    }
}
