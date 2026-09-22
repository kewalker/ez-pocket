using System.IO.Compression;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class AppDiagnosticsServiceTests
{
    [Fact]
    public async Task WritesStructuredEventsAndExportsThemWithoutSensitiveTargetPath()
    {
        string root = CreateTempFolder();
        try
        {
            var diagnostics = new AppDiagnosticsService(root, Path.Combine(root, "exports"));
            const string sensitivePath = "C:\\Users\\Example\\Private Pocket";

            diagnostics.Info("PocketSelected", new Dictionary<string, string?>
            {
                ["TargetId"] = AppDiagnosticsService.TargetId(sensitivePath)
            });
            diagnostics.Error("CoreSyncApplyFailed", new IOException(sensitivePath));
            string bundle = await diagnostics.CreateBundleAsync();

            Assert.True(File.Exists(bundle));
            using ZipArchive archive = ZipFile.OpenRead(bundle);
            ZipArchiveEntry log = Assert.Single(archive.Entries, entry => entry.Name.EndsWith(".jsonl", StringComparison.Ordinal));
            using StreamReader reader = new(log.Open());
            string content = await reader.ReadToEndAsync();
            Assert.Contains("PocketSelected", content);
            Assert.Contains("IOException", content);
            Assert.DoesNotContain(sensitivePath, content);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "ez-pocket-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
