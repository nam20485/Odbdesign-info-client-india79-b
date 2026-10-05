using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using Xunit;

namespace OdbDesignInfoClient.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="SymbolsTabViewModel"/>.
/// </summary>
public class SymbolsTabViewModelTests
{
    private readonly Mock<IDesignService> _mockDesignService = new();
    private readonly SymbolsTabViewModel _sut;

    public SymbolsTabViewModelTests()
    {
        _sut = new SymbolsTabViewModel(_mockDesignService.Object);
    }

    [Fact]
    public async Task LoadAsync_PopulatesSymbols()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetSymbolsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SymbolSummary { Name = "r10" }, new SymbolSummary { Name = "rect10x20" }]);

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.Equal(2, _sut.Symbols.Count);
        Assert.Equal(2, _sut.TotalCount);
        Assert.Equal(2, _sut.FilteredCount);
        Assert.False(_sut.IsEmpty);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public async Task LoadAsync_WithNoSymbols_ShowsHonestEmptyState()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetSymbolsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.Empty(_sut.Symbols);
        Assert.True(_sut.IsEmpty);
        Assert.False(_sut.HasError);
        Assert.Contains("names only", _sut.EmptyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_SecondActivationWithoutForce_DoesNotRefetch()
    {
        // Arrange - the guard keys on the design (symbols are design-scoped)
        _mockDesignService
            .Setup(x => x.GetSymbolsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SymbolSummary { Name = "r10" }]);

        // Act
        await _sut.LoadAsync("design-1", "step");
        await _sut.LoadAsync("design-1", "other-step");

        // Assert
        _mockDesignService.Verify(
            x => x.GetSymbolsAsync("design-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoadAsync_WhenServiceThrows_SetsVisibleError()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetSymbolsAsync("design-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.True(_sut.HasError);
        Assert.Contains("Failed to load symbols", _sut.ErrorMessage);
        Assert.False(_sut.IsLoading);
    }

    [Fact]
    public async Task LoadAsync_OnAuthFailure_NamesEnvironmentVariables()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetSymbolsAsync("design-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("401"));

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert
        Assert.True(_sut.HasError);
        Assert.Contains("ODBDESIGN_REST_USERNAME", _sut.ErrorMessage);
    }

    [Fact]
    public async Task Filter_MatchesSymbolName()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetSymbolsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SymbolSummary { Name = "drill_symbol1" },
                new SymbolSummary { Name = "pads_th_square74x62" },
            ]);
        await _sut.LoadAsync("design-1", "step");

        // Act
        _sut.FilterText = "drill";

        // Assert (debounced - poll until applied, bounded to ~5 s)
        for (var i = 0; i < 100 && _sut.Symbols.Count != 1 && i < 100; i++)
        {
            await Task.Delay(50);
        }

        Assert.Single(_sut.Symbols);
        Assert.Equal("drill_symbol1", _sut.Symbols[0].Name);
        Assert.Equal(1, _sut.FilteredCount);
        Assert.Equal(2, _sut.TotalCount);
    }
}
