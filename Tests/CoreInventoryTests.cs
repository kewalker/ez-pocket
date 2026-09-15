using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class CoreInventoryTests
{
    [Fact]
    public void CompareIncludesInstalledCoreMissingFromInventory()
    {
        var pocket = new PocketDrive("C:\\Pocket", "Pocket", DriveType.Unknown, 0, 0, 2,
            ["Assets", "Cores"], 1, ["missing-core"]);
        IReadOnlyList<CoreComparison> result = CoreInventoryService.Compare(pocket, []);
        CoreComparison core = Assert.Single(result);
        Assert.Equal("missing-core", core.Identifier);
        Assert.Equal("Unknown", core.Status);
    }
}
