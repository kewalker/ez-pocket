using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class FeaturedCoreSetServiceTests
{
    [Fact]
    public void ApplyingAFeaturedSetReplacesTheDesiredSelectionAndReportsUnavailableCores()
    {
        var service = new FeaturedCoreSetService();
        var selection = new CoreSelectionService();
        var pocket = new PocketDrive("C:\\Pocket", "Pocket", DriveType.Unknown, 0, 0, 2, ["Assets", "Cores"], 1, ["Other.Core"]);
        var cores = new CoreComparison[]
        {
            new("Spiritualized.GB", "Game Boy", "Handheld", "-", "1.0", false, true, "Available"),
            new("Spiritualized.GBC", "Game Boy Color", "Handheld", "-", "1.0", false, true, "Available"),
            new("Other.Core", "Other", "Other", "1.0", "1.0", true, true, "Installed")
        };
        selection.InitializeForPocket(pocket, cores);

        Assert.True(service.Select("handheld-essentials"));
        FeaturedCoreSetSelection result = Assert.IsType<FeaturedCoreSetSelection>(service.ApplyPendingSelection(selection, cores));

        Assert.Equal(2, result.MatchedCoreCount);
        Assert.Contains("Spiritualized.GBA", result.MissingCoreIdentifiers);
        Assert.Equal(["Spiritualized.GB", "Spiritualized.GBC"], selection.SelectedCores.Select(core => core.Identifier).OrderBy(identifier => identifier));
        Assert.False(cores.Single(core => core.Identifier == "Other.Core").IsSelected);
        Assert.Null(service.PendingSet);
    }
}
