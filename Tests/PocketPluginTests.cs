using System.Net;
using System.Text.Json;
using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class PocketPluginTests
{
    [Theory]
    [InlineData("owner/plugin", "owner/plugin")]
    [InlineData(" https://github.com/owner/plugin/ ", "owner/plugin")]
    public void AcceptsRepositoryReferences(string input, string expected) => Assert.Equal(expected, PocketPluginService.ParseRepository(input));

    [Theory]
    [InlineData("https://example.com/owner/plugin")]
    [InlineData("https://github.com/owner/plugin/releases")]
    [InlineData("https://github.com/owner/plugin?ref=main")]
    [InlineData("https://user@github.com/owner/plugin")]
    [InlineData("owner/..")]
    [InlineData("../plugin")]
    public void RejectsUnsupportedRepositoryReferences(string input) => Assert.Throws<InvalidDataException>(() => PocketPluginService.ParseRepository(input));

    [Fact]
    public void ParsesActualSerdeProtocol()
    {
        Assert.Null(PocketPluginRuntime.ParseMessage("\"Exit\""));
        var choice = PocketPluginRuntime.ParseMessage("""{"Choice":{"name":"menu","query":"Choose","choices":["Build","Exit"]}}""");
        Assert.Equal("menu", choice!.Name);
        Assert.Equal(new[] { "Build", "Exit" }, choice.Choices);
        var text = PocketPluginRuntime.ParseMessage("""{"Text":{"name":"key","query":"Enter a key"}}""");
        Assert.Null(text!.Choices);
        Assert.Throws<InvalidDataException>(() => PocketPluginRuntime.ParseMessage("""{"Exit":{}}"""));
        Assert.Throws<InvalidDataException>(() => PocketPluginRuntime.ParseMessage("""{"Choice":{"name":"x","query":"x","choices":[]}}"""));
    }

    [Fact]
    public async Task InstallStoresManifestAndNeverRunsPlugin()
    {
        using var test = new Scenario();
        PocketPlugin plugin = await test.Install();
        Assert.Equal("ROM builder", plugin.Name);
        Assert.Equal("v1.0", plugin.Version);
        Assert.Equal("Network: none", plugin.NetworkSummary);
        Assert.Single(test.Service.ListInstalled());
        Assert.Equal(0, test.Runtime.Calls);
        await Assert.ThrowsAsync<InvalidDataException>(() => test.Install());
        test.Service.Uninstall(plugin);
        Assert.Empty(test.Service.ListInstalled());
    }

    [Theory]
    [InlineData("bad-wasm")]
    [InlineData("bad-manifest")]
    [InlineData("missing-asset")]
    [InlineData("oversized-manifest")]
    public async Task InvalidInstallDoesNotLeaveAnInstalledPlugin(string kind)
    {
        using var test = new Scenario(kind);
        await Assert.ThrowsAnyAsync<Exception>(() => test.Install());
        Assert.Empty(test.Service.ListInstalled());
        Assert.Equal(0, test.Runtime.Calls);
    }

    [Fact]
    public async Task RunOnlyChangesStagingThenApplyBacksUpReplacementsAndDeletions()
    {
        using var test = new Scenario();
        test.Write("Assets/test/common/replace.rom", "old");
        test.Write("Assets/test/common/delete.rom", "remove");
        test.Runtime.Action = (pocket, host) =>
        {
            Assert.NotEqual(test.Target, pocket);
            File.WriteAllText(Path.Combine(pocket, "Assets/test/common/replace.rom"), "new");
            File.Delete(Path.Combine(pocket, "Assets/test/common/delete.rom"));
            File.WriteAllText(Path.Combine(pocket, "Assets/test/common/add.json"), "generated");
            File.WriteAllText(Path.Combine(host, "cache.txt"), "private");
        };
        PocketPluginPreview preview = await test.Prepare();
        Assert.True(preview.CanApply);
        Assert.Equal(3, preview.Changes.Count);
        Assert.Equal("old", test.Read("Assets/test/common/replace.rom"));
        Assert.Equal("remove", test.Read("Assets/test/common/delete.rom"));
        Assert.False(File.Exists(Path.Combine(test.Target, "Assets/test/common/add.json")));
        PocketPluginResult applied = await test.Service.ApplyAsync(preview);
        Assert.True(applied.Succeeded, applied.Message);
        Assert.Equal("new", test.Read("Assets/test/common/replace.rom"));
        Assert.Equal("generated", test.Read("Assets/test/common/add.json"));
        Assert.False(File.Exists(Path.Combine(test.Target, "Assets/test/common/delete.rom")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(applied.BackupPath!, "originals/Assets/test/common/replace.rom")));
        Assert.Equal("remove", File.ReadAllText(Path.Combine(applied.BackupPath!, "originals/Assets/test/common/delete.rom")));
        Assert.False((await test.Service.ApplyAsync(preview)).Succeeded);
    }

    [Fact]
    public async Task ChangedInputOrUnrelatedTargetFileInvalidatesReview()
    {
        using var test = new Scenario();
        test.Write("Assets/test/common/input.rom", "input");
        test.Runtime.Action = (pocket, _) => File.WriteAllText(Path.Combine(pocket, "Assets/test/common/game.json"), "generated");
        PocketPluginPreview preview = await test.Prepare();
        test.Write("Assets/test/common/input.rom", "changed since run");
        PocketPluginResult result = await test.Service.ApplyAsync(preview);
        Assert.False(result.Succeeded);
        Assert.Contains("target changed", result.Message);
        Assert.False(File.Exists(Path.Combine(test.Target, "Assets/test/common/game.json")));
    }

    [Fact]
    public async Task ChangedStagedFileInvalidatesReview()
    {
        using var test = new Scenario();
        string? staged = null;
        test.Runtime.Action = (pocket, _) => { staged = Path.Combine(pocket, "Assets/game.json"); File.WriteAllText(staged, "reviewed"); };
        PocketPluginPreview preview = await test.Prepare();
        File.WriteAllText(staged!, "modified after review");
        PocketPluginResult result = await test.Service.ApplyAsync(preview);
        Assert.False(result.Succeeded);
        Assert.Contains("staged file changed", result.Message);
        Assert.False(File.Exists(Path.Combine(test.Target, "Assets/game.json")));
    }

    [Fact]
    public async Task RemovedEmptyTargetFoldersInvalidateReview()
    {
        using var test = new Scenario();
        test.Runtime.Action = (pocket, _) => File.WriteAllText(Path.Combine(pocket, "Assets/game.json"), "generated");
        PocketPluginPreview preview = await test.Prepare();
        Directory.Delete(Path.Combine(test.Target, "Cores"));
        PocketPluginResult result = await test.Service.ApplyAsync(preview);
        Assert.False(result.Succeeded);
        Assert.Contains("target changed", result.Message);
        Assert.False(File.Exists(Path.Combine(test.Target, "Assets/game.json")));
    }

    [Fact]
    public async Task SystemStorageMetadataIsExcludedFromTheWorkingCopyAndApply()
    {
        using var test = new Scenario();
        test.Write("System Volume Information/index.dat", "storage metadata");
        test.Runtime.Action = (pocket, _) =>
        {
            Assert.False(Directory.Exists(Path.Combine(pocket, "System Volume Information")));
            Directory.CreateDirectory(Path.Combine(pocket, "System Volume Information"));
            File.WriteAllText(Path.Combine(pocket, "System Volume Information/index.dat"), "plugin metadata");
            File.WriteAllText(Path.Combine(pocket, "Assets/game.json"), "generated");
        };
        PocketPluginPreview preview = await test.Prepare();
        Assert.Single(preview.Changes);
        Assert.True((await test.Service.ApplyAsync(preview)).Succeeded);
        Assert.Equal("storage metadata", test.Read("System Volume Information/index.dat"));
    }

    [Fact]
    public async Task DiscardAndRunFailureLeaveTheTargetUntouchedAndCleanStaging()
    {
        using var test = new Scenario();
        test.Runtime.Action = (pocket, _) => File.WriteAllText(Path.Combine(pocket, "Assets/game.json"), "generated");
        PocketPluginPreview preview = await test.Prepare();
        test.Service.Discard(preview);
        Assert.False((await test.Service.ApplyAsync(preview)).Succeeded);
        Assert.Empty(Directory.EnumerateDirectories(test.Staging));
        test.Runtime.Action = (pocket, _) => { File.WriteAllText(Path.Combine(pocket, "Assets/game.json"), "partial"); throw new IOException("failed generator"); };
        await Assert.ThrowsAsync<IOException>(() => test.Prepare());
        Assert.False(File.Exists(Path.Combine(test.Target, "Assets/game.json")));
        Assert.Empty(Directory.EnumerateDirectories(test.Staging));
    }

    [Fact]
    public async Task CancellationDiscardsStagedChanges()
    {
        using var test = new Scenario();
        using var cancellation = new CancellationTokenSource();
        test.Runtime.Action = (pocket, _) => { File.WriteAllText(Path.Combine(pocket, "Assets/game.json"), "partial"); cancellation.Cancel(); };
        await test.Install();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.Service.PrepareAsync(test.Pocket, test.Service.ListInstalled().Single(),
            (_, _) => Task.FromResult<string?>(null), _ => { }, _ => { }, cancellation.Token));
        Assert.False(File.Exists(Path.Combine(test.Target, "Assets/game.json")));
        Assert.Empty(Directory.EnumerateDirectories(test.Staging));
    }

    [Theory]
    [InlineData("Saves/game.sav")]
    [InlineData("Memories/state.sta")]
    [InlineData("pocket_firmware.bin")]
    public async Task SensitiveWorkflowsCannotBeWrittenByPlugins(string relative)
    {
        using var test = new Scenario();
        test.Runtime.Action = (pocket, _) => { string path = Path.Combine(pocket, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "generated"); };
        PocketPluginPreview preview = await test.Prepare();
        Assert.False(preview.CanApply);
        Assert.NotEmpty(preview.Blockers);
        Assert.False((await test.Service.ApplyAsync(preview)).Succeeded);
        Assert.False(File.Exists(Path.Combine(test.Target, relative)));
    }

    [Fact]
    public async Task FailedApplyRestoresEarlierChanges()
    {
        using var test = new Scenario();
        test.Write("Assets/a.json", "old a");
        test.Write("Assets/z.json", "old z");
        test.Runtime.Action = (pocket, _) => { File.WriteAllText(Path.Combine(pocket, "Assets/a.json"), "new a"); File.WriteAllText(Path.Combine(pocket, "Assets/z.json"), "new z"); };
        PocketPluginPreview preview = await test.Prepare();
        // Read sharing allows preflight and backups; replacing the second file must fail.
        using var locked = new FileStream(Path.Combine(test.Target, "Assets/z.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        PocketPluginResult result = await test.Service.ApplyAsync(preview);
        Assert.False(result.Succeeded);
        Assert.Contains("restored", result.Message);
        Assert.Equal("old a", test.Read("Assets/a.json"));
        Assert.Equal("old z", test.Read("Assets/z.json"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(test.Target, "Assets"), "*.tmp"));
    }

    [Fact]
    public async Task DirectoryJunctionCannotEscapeTheStagedOrApplyTarget()
    {
        using var test = new Scenario();
        string outside = Path.Combine(test.Root, "outside");
        Directory.CreateDirectory(outside);
        test.Runtime.Action = (pocket, _) => File.WriteAllText(Path.Combine(pocket, "Assets/game.json"), "generated");
        PocketPluginPreview preview = await test.Prepare();
        string link = Path.Combine(test.Target, "Assets", "escape");
        CreateJunction(link, outside);
        try
        {
            PocketPluginResult result = await test.Service.ApplyAsync(preview);
            Assert.False(result.Succeeded);
            Assert.Contains("Linked", result.Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task APluginCreatedJunctionIsRejectedAndCleanedWithoutFollowingIt()
    {
        using var test = new Scenario();
        string outside = Path.Combine(test.Root, "outside");
        Directory.CreateDirectory(outside);
        string keep = Path.Combine(outside, "keep.txt");
        File.WriteAllText(keep, "untouched");
        test.Runtime.Action = (pocket, _) => CreateJunction(Path.Combine(pocket, "Assets", "escape"), outside);
        await Assert.ThrowsAsync<IOException>(() => test.Prepare());
        Assert.Equal("untouched", File.ReadAllText(keep));
        Assert.Empty(Directory.EnumerateDirectories(test.Staging));
    }

    private static void CreateJunction(string link, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(target);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class Scenario : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ez-pocket-plugin-tests", Guid.NewGuid().ToString("N"));
        public string Target => Path.Combine(Root, "target");
        public string Staging => Path.Combine(Root, "staging");
        public FakeRuntime Runtime { get; } = new();
        public PocketPluginService Service { get; }
        public PocketDrive Pocket => new(Target, "Test Pocket", DriveType.Unknown, 0, 0, 2, ["Assets", "Cores"], 0, []);

        public Scenario(string? fault = null)
        {
            Directory.CreateDirectory(Path.Combine(Target, "Assets"));
            Directory.CreateDirectory(Path.Combine(Target, "Cores"));
            Service = new PocketPluginService(new HttpClient(new ReleaseHandler(fault)), Runtime, Path.Combine(Root, "plugins"), Staging, Path.Combine(Root, "backups"));
        }
        public Task<PocketPlugin> Install() => Service.InstallFromGitHubAsync("test/builder");
        public async Task<PocketPluginPreview> Prepare()
        {
            PocketPlugin plugin = Service.ListInstalled().SingleOrDefault() ?? await Install();
            return await Service.PrepareAsync(Pocket, plugin, (_, _) => Task.FromResult<string?>(null), _ => { }, _ => { });
        }
        public void Write(string relative, string contents) { string path = Path.Combine(Target, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, contents); }
        public string Read(string relative) => File.ReadAllText(Path.Combine(Target, relative));
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    private sealed class FakeRuntime : IPocketPluginRuntime
    {
        public int Calls { get; private set; }
        public Action<string, string>? Action { get; set; }
        public Task RunAsync(string wasmPath, PocketPlugin descriptor, string pocketFolder, string hostFolder,
            Func<PocketPluginPrompt, CancellationToken, Task<string?>> prompt, Action<string> log, Action<Uri> offerUrl, CancellationToken cancellationToken)
        {
            Calls++;
            Action?.Invoke(pocketFolder, hostFolder);
            return Task.CompletedTask;
        }
    }

    private sealed class ReleaseHandler(string? fault) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            HttpContent content;
            if (path.EndsWith("/latest"))
                content = new StringContent(JsonSerializer.Serialize(new
                {
                    tag_name = "v1.0",
                    assets = (fault == "missing-asset" ? new[] { "plugin.json" } : new[] { "plugin.json", "plugin.wasm" }).Select(name => new { name, browser_download_url = $"https://github.com/test/builder/releases/download/v1.0/{name}" })
                }));
            else if (path.EndsWith("plugin.json"))
                content = new StringContent(fault switch { "bad-manifest" => "{}", "oversized-manifest" => new string('x', 65537), _ => """{"name":"ROM builder","description":"Build game files","allowed_hosts":[]}""" });
            else content = new ByteArrayContent(fault == "bad-wasm" ? [1, 2, 3] : [0, 97, 115, 109, 1, 0, 0, 0]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
