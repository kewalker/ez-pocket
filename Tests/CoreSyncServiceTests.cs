using System.IO.Compression;
using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class CoreSyncServiceTests
{
    [Fact]
    public async Task PrepareAndApplyCopiesOnlyAllowedPackageFilesAndBacksUpReplacements()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            Directory.CreateDirectory(Path.Combine(pocketPath, "Cores", "Example.Core"));
            string existing = Path.Combine(pocketPath, "Cores", "Example.Core", "core.json");
            await File.WriteAllTextAsync(existing, "old");
            byte[] archive = CreateArchive([
                ("Cores/Example.Core/core.json", "new"),
                ("Assets/example/readme.txt", "asset"),
                ("System/unsafe.bin", "must not copy")
            ]);
            var service = new CoreSyncService(new HttpClient(new ArchiveHandler(archive)), Path.Combine(root, "staging"), Path.Combine(root, "backups"));
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 2, ["Assets", "Cores"], 1, ["Example.Core"]);
            var core = new CoreComparison("Example.Core", "Example", "Test", "1.0", "2.0", true, true, "Update", "https://packages.example/core.zip");

            CoreSyncPreview preview = await service.PrepareAsync(pocket, [core]);

            Assert.True(preview.CanSync);
            Assert.Equal(2, preview.Changes.Count);
            Assert.Contains(preview.Changes, change => change.RelativePath == Path.Combine("Cores", "Example.Core", "core.json") && change.ReplacesExisting);

            CoreSyncResult result = await service.ApplyAsync(preview);

            Assert.True(result.Succeeded);
            Assert.Equal("new", await File.ReadAllTextAsync(existing));
            Assert.Equal("asset", await File.ReadAllTextAsync(Path.Combine(pocketPath, "Assets", "example", "readme.txt")));
            Assert.False(File.Exists(Path.Combine(pocketPath, "System", "unsafe.bin")));
            Assert.NotNull(result.BackupPath);
            Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(result.BackupPath!, "Cores", "Example.Core", "core.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PrepareBlocksLicenseRequiredCoreBeforeDownload()
    {
        string root = CreateTempFolder();
        try
        {
            var service = new CoreSyncService(new HttpClient(new ArchiveHandler([])), Path.Combine(root, "staging"), Path.Combine(root, "backups"));
            var pocket = new PocketDrive(Path.Combine(root, "Pocket"), "Pocket", DriveType.Unknown, 0, 0, 0, [], 0, []);
            var core = new CoreComparison("Example.Core", "Example", "Test", "-", "2.0", false, true, "Available", "https://packages.example/core.zip", true);

            CoreSyncPreview preview = await service.PrepareAsync(pocket, [core]);

            Assert.False(preview.CanSync);
            Assert.Contains("requires a license", Assert.Single(preview.Blockers));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ApplyRetainsOnlyFiveNewestBackupsForAPocket()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            Directory.CreateDirectory(Path.Combine(pocketPath, "Cores", "Example.Core"));
            await File.WriteAllTextAsync(Path.Combine(pocketPath, "Cores", "Example.Core", "core.json"), "old");
            byte[] archive = CreateArchive([("Cores/Example.Core/core.json", "new")]);
            var service = new CoreSyncService(new HttpClient(new ArchiveHandler(archive)), Path.Combine(root, "staging"), Path.Combine(root, "backups"));
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 2, ["Assets", "Cores"], 1, ["Example.Core"]);
            var core = new CoreComparison("Example.Core", "Example", "Test", "1.0", "2.0", true, true, "Update", "https://packages.example/core.zip");
            CoreSyncResult? result = null;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                CoreSyncPreview preview = await service.PrepareAsync(pocket, [core]);
                result = await service.ApplyAsync(preview);
                Assert.True(result.Succeeded);
            }

            Assert.NotNull(result?.BackupPath);
            Assert.Equal(1, result!.BackupsPruned);
            Assert.Equal(5, Directory.GetDirectories(Directory.GetParent(result.BackupPath!)!.FullName, "core-sync-*").Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static byte[] CreateArchive((string Path, string Content)[] files)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach ((string path, string content) in files)
            {
                using StreamWriter writer = new(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }
        return memory.ToArray();
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "ez-pocket-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(archive)
        });
    }
}
