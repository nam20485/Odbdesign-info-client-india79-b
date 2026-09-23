using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using Xunit;

namespace OdbDesignInfoClient.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="PackagesTabViewModel"/> usage-row population.
/// </summary>
public class PackagesTabViewModelTests
{
    private readonly Mock<IDesignService> _mockDesignService = new();
    private readonly Mock<INavigationService> _mockNavigationService = new();
    private readonly PackagesTabViewModel _sut;

    public PackagesTabViewModelTests()
    {
        _sut = new PackagesTabViewModel(_mockDesignService.Object, _mockNavigationService.Object);
    }

    [Fact]
    public async Task LoadAsync_PopulatesUsageRows_FromPackageUsages()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetPackagesAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Package
                {
                    Name = "0402",
                    Pitch = 1.0,
                    PinCount = 2,
                    Usages =
                    [
                        new EntityUsage { ComponentRefDes = "R1", PartName = "10K" },
                        new EntityUsage { ComponentRefDes = "C2", PartName = "100N" },
                    ],
                },
                new Package { Name = "SOT-23", PinCount = 3 },
            ]);

        // Act
        await _sut.LoadAsync("design-1", "step");

        // Assert - usage rows carry RefDes + part name; packages without usages expand to nothing
        Assert.Equal(2, _sut.Packages.Count);
        var package = _sut.Packages.First(p => p.Name == "0402");
        Assert.Equal(2, package.Usages.Count);
        Assert.Equal("R1", package.Usages[0].ComponentRefDes);
        Assert.Equal("10K", package.Usages[0].PartName);
        Assert.Equal("C2", package.Usages[1].ComponentRefDes);
        Assert.Empty(_sut.Packages.First(p => p.Name == "SOT-23").Usages);
        Assert.Equal(2, _sut.TotalCount);
    }

    [Fact]
    public async Task ClearFilter_AppliesEmptyFilterImmediately_BypassingDebounce()
    {
        // Arrange
        _mockDesignService
            .Setup(x => x.GetPackagesAsync("design-1", "step", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Package { Name = "0402" }, new Package { Name = "SOT-23" }]);
        await _sut.LoadAsync("design-1", "step");

        _sut.FilterText = "0402";
        await WaitForFilterAppliedAsync(() => _sut.Packages.Count == 1);
        Assert.Single(_sut.Packages); // filtered down to one row

        // Act
        _sut.ClearFilterCommand.Execute(null);

        // Assert - the full list is back right away, without waiting out the debounce
        Assert.Equal(string.Empty, _sut.FilterText);
        Assert.Equal(2, _sut.Packages.Count);
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
