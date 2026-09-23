using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using Xunit;

namespace OdbDesignInfoClient.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="PartsTabViewModel"/> usage-row population.
/// </summary>
public class PartsTabViewModelTests
{
    private readonly Mock<IDesignService> _mockDesignService = new();
    private readonly Mock<INavigationService> _mockNavigationService = new();
    private readonly PartsTabViewModel _sut;

    public PartsTabViewModelTests()
    {
        _sut = new PartsTabViewModel(_mockDesignService.Object, _mockNavigationService.Object);
    }

    [Fact]
    public async Task LoadAsync_PopulatesUsageRows_FromPartUsages()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetPartsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Part
                {
                    PartNumber = "10K",
                    Manufacturer = "Yageo",
                    UsageCount = 2,
                    Usages =
                    [
                        new EntityUsage { ComponentRefDes = "R1" },
                        new EntityUsage { ComponentRefDes = "R7" },
                    ],
                },
                new Part { PartNumber = "100N", UsageCount = 0 },
            ]);

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert - usage rows carry the component RefDes; parts without usages expand to nothing
        Assert.Equal(2, _sut.Parts.Count);
        var part = _sut.Parts.First(p => p.PartNumber == "10K");
        Assert.Equal(2, part.Usages.Count);
        Assert.Equal("R1", part.Usages[0].ComponentRefDes);
        Assert.Equal("R7", part.Usages[1].ComponentRefDes);
        Assert.Empty(_sut.Parts.First(p => p.PartNumber == "100N").Usages);
        Assert.Equal(2, _sut.TotalCount);
    }

    [Fact]
    public async Task ClearFilter_AppliesEmptyFilterImmediately_BypassingDebounce()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetPartsAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Part { PartNumber = "10K" }, new Part { PartNumber = "100N" }]);
        await _sut.LoadAsync("design-1", "step");

        _sut.FilterText = "10K";
        await WaitForFilterAppliedAsync(() => _sut.Parts.Count == 1);
        Assert.Single(_sut.Parts); // filtered down to one row

        // Act
        _sut.ClearFilterCommand.Execute(null);

        // Assert - the full list is back right away, without waiting out the debounce
        Assert.Equal(string.Empty, _sut.FilterText);
        Assert.Equal(2, _sut.Parts.Count);
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
}
