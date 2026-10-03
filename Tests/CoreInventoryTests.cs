using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class CoreInventoryTests
{
    [Fact]
    public async Task NullLicenseRequirementIsTreatedAsFalse()
    {
        const string json = """{"data":[{"identifier":"Example.Core","version":"1.0","download_url":"https://packages.example/core.zip","requires_license":null,"platform":{"name":"Example","category":"Test"}}]}""";
        var client = new HttpClient(new JsonHandler(json));

        IReadOnlyList<AvailableCore> available = await new CoreInventoryService(client).GetAvailableAsync();

        AvailableCore core = Assert.Single(available);
        Assert.False(core.RequiresLicense);
        Assert.Equal("https://packages.example/core.zip", core.DownloadUrl);
    }

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

    [Fact]
    public void CompareToleratesCoreMetadataWithoutVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), "ez-pocket-tests", Guid.NewGuid().ToString("N"));
        string coreDirectory = Path.Combine(root, "Cores", "example.core");
        Directory.CreateDirectory(coreDirectory);
        File.WriteAllText(Path.Combine(coreDirectory, "core.json"), """{"core":{"metadata":{}}}""");
        try
        {
            var pocket = new PocketDrive(root, "Pocket", DriveType.Unknown, 0, 0, 2,
                ["Assets", "Cores"], 1, ["example.core"]);
            var available = new[] { new AvailableCore("example.core", "1.0", "Example", "Console", null, false) };

            CoreComparison result = Assert.Single(CoreInventoryService.Compare(pocket, available));

            Assert.Equal("-", result.InstalledVersion);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void InstalledCoresAreSelectedInitiallyAndUserChangesSurviveRefresh()
    {
        var selection = new CoreSelectionService();
        var pocket = new PocketDrive("C:\\Pocket", "Pocket", DriveType.Unknown, 0, 0, 2, ["Assets", "Cores"], 1, ["installed"]);
        var initial = new[]
        {
            new CoreComparison("installed", "Installed", "Test", "1.0", "1.0", true, true, "Installed"),
            new CoreComparison("available", "Available", "Test", "-", "1.0", false, true, "Available")
        };

        selection.InitializeForPocket(pocket, initial);

        Assert.True(initial[0].IsSelected);
        Assert.False(initial[1].IsSelected);
        selection.SetSelected(initial[0], false);

        var refreshed = new[]
        {
            new CoreComparison("installed", "Installed", "Test", "1.0", "1.1", true, true, "Update"),
            new CoreComparison("available", "Available", "Test", "-", "1.0", false, true, "Available")
        };
        selection.InitializeForPocket(pocket, refreshed);

        Assert.Empty(selection.SelectedCores);
        Assert.All(refreshed, core => Assert.False(core.IsSelected));
    }

    [Fact]
    public void NewManageSessionSelectsInstalledCoresAgain()
    {
        var selection = new CoreSelectionService();
        var pocket = new PocketDrive("C:\\Pocket", "Pocket", DriveType.Unknown, 0, 0, 2,
            ["Assets", "Cores"], 1, ["installed"]);
        var firstVisit = new[] { new CoreComparison("installed", "Installed", "Console", "1.0", "1.0", true, true, "Installed") };
        selection.InitializeForPocket(pocket, firstVisit);
        selection.SetSelected(firstVisit[0], false);

        selection.BeginManageSession();
        var nextVisit = new[] { new CoreComparison("installed", "Installed", "Console", "1.0", "1.0", true, true, "Installed") };
        selection.InitializeForPocket(pocket, nextVisit);

        Assert.True(nextVisit[0].IsSelected);
        Assert.Single(selection.SelectedCores);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });
    }
}
