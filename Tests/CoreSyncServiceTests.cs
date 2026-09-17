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
    public async Task PrepareSkipsIdenticalInstalledFiles()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "pocket");
            Directory.CreateDirectory(Path.Combine(pocketPath, "Cores", "Example.Core"));
            Directory.CreateDirectory(Path.Combine(pocketPath, "Assets", "example"));
            await File.WriteAllTextAsync(Path.Combine(pocketPath, "Cores", "Example.Core", "core.json"), "same-core");
            await File.WriteAllTextAsync(Path.Combine(pocketPath, "Assets", "example", "readme.txt"), "same-asset");
            byte[] archive = CreateArchive([
                ("Cores/Example.Core/core.json", "same-core"),
                ("Assets/example/readme.txt", "same-asset")
            ]);
            var service = new CoreSyncService(new HttpClient(new ArchiveHandler(archive)), Path.Combine(root, "staging"), Path.Combine(root, "backups"));
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 2, ["Assets", "Cores"], 1, ["Example.Core"]);
            var core = new CoreComparison("Example.Core", "Example", "Test", "1.0", "1.0", true, false, "Installed", "https://packages.example/core.zip");

            CoreSyncPreview preview = await service.PrepareAsync(pocket, [core]);

            Assert.Empty(preview.Changes);
            Assert.False(preview.CanSync);
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
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 2, ["Assets", "Cores"], 1, ["Example.Core"]);
            var core = new CoreComparison("Example.Core", "Example", "Test", "1.0", "2.0", true, true, "Update", "https://packages.example/core.zip");
            CoreSyncResult? result = null;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                byte[] archive = CreateArchive([("Cores/Example.Core/core.json", $"new-{attempt}")]);
                var service = new CoreSyncService(new HttpClient(new ArchiveHandler(archive)), Path.Combine(root, "staging"), Path.Combine(root, "backups"));
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

    [Fact]
    public async Task SyncRemovesUnselectedInstalledCoreAndBacksItUp()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            string coreDirectory = Path.Combine(pocketPath, "Cores", "Example.Core");
            Directory.CreateDirectory(coreDirectory);
            await File.WriteAllTextAsync(Path.Combine(coreDirectory, "core.json"), "core");
            await File.WriteAllTextAsync(Path.Combine(coreDirectory, "core.rbf"), "bitstream");
            Directory.CreateDirectory(Path.Combine(pocketPath, "Assets", "Example"));
            await File.WriteAllTextAsync(Path.Combine(pocketPath, "Assets", "Example", "keep.txt"), "asset");
            var service = new CoreSyncService(new HttpClient(new ArchiveHandler([])), Path.Combine(root, "staging"), Path.Combine(root, "backups"));
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 2, ["Assets", "Cores"], 1, ["Example.Core"]);
            var core = new CoreComparison("Example.Core", "Example", "Test", "1.0", "1.0", true, true, "Installed");

            CoreSyncPreview preview = await service.PrepareAsync(pocket, [], [core]);
            CoreSyncResult result = await service.ApplyAsync(preview);

            Assert.True(preview.CanSync);
            Assert.Single(preview.Removals);
            Assert.True(result.Succeeded);
            Assert.Equal(1, result.CoresRemoved);
            Assert.False(Directory.Exists(coreDirectory));
            Assert.Equal("asset", await File.ReadAllTextAsync(Path.Combine(pocketPath, "Assets", "Example", "keep.txt")));
            Assert.Equal("core", await File.ReadAllTextAsync(Path.Combine(result.BackupPath!, "removed", "Cores", "Example.Core", "core.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PrepareDeduplicatesIdenticalFilesSharedBySelectedPackages()
    {
        string root = CreateTempFolder();
        try
        {
            byte[] firstArchive = CreateArchive([("Cores/First/core.json", "first"), ("Assets/shared/image.bin", "same-image")]);
            byte[] secondArchive = CreateArchive([("Cores/Second/core.json", "second"), ("Assets/shared/image.bin", "same-image")]);
            var client = new HttpClient(new ArchiveMapHandler(new Dictionary<string, byte[]>
            {
                ["https://packages.example/first.zip"] = firstArchive,
                ["https://packages.example/second.zip"] = secondArchive
            }));
            var service = new CoreSyncService(client, Path.Combine(root, "staging"), Path.Combine(root, "backups"));
            var pocket = new PocketDrive(Path.Combine(root, "Pocket"), "Pocket", DriveType.Unknown, 0, 0, 0, [], 0, []);
            var first = new CoreComparison("First", "First", "Test", "-", "1.0", false, true, "Available", "https://packages.example/first.zip");
            var second = new CoreComparison("Second", "Second", "Test", "-", "1.0", false, true, "Available", "https://packages.example/second.zip");

            CoreSyncPreview preview = await service.PrepareAsync(pocket, [first, second]);

            Assert.True(preview.CanSync);
            Assert.Empty(preview.Blockers);
            Assert.Equal(3, preview.Changes.Count);
            Assert.Single(preview.Changes, change => change.RelativePath == Path.Combine("Assets", "shared", "image.bin"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PrepareKeepsFirstSelectedPackageForDifferingSharedFile()
    {
        string root = CreateTempFolder();
        try
        {
            byte[] firstArchive = CreateArchive([("Cores/First/core.json", "first"), ("Assets/shared/image.bin", "first-image")]);
            byte[] secondArchive = CreateArchive([("Cores/Second/core.json", "second"), ("Assets/shared/image.bin", "second-image")]);
            var client = new HttpClient(new ArchiveMapHandler(new Dictionary<string, byte[]>
            {
                ["https://packages.example/first.zip"] = firstArchive,
                ["https://packages.example/second.zip"] = secondArchive
            }));
            var service = new CoreSyncService(client, Path.Combine(root, "staging"), Path.Combine(root, "backups"));
            var pocket = new PocketDrive(Path.Combine(root, "Pocket"), "Pocket", DriveType.Unknown, 0, 0, 0, [], 0, []);
            var first = new CoreComparison("First", "First", "Test", "-", "1.0", false, true, "Available", "https://packages.example/first.zip");
            var second = new CoreComparison("Second", "Second", "Test", "-", "1.0", false, true, "Available", "https://packages.example/second.zip");

            CoreSyncPreview preview = await service.PrepareAsync(pocket, [first, second]);

            Assert.True(preview.CanSync);
            CoreSyncOverride overrideFile = Assert.Single(preview.Overrides);
            Assert.Equal(Path.Combine("Assets", "shared", "image.bin"), overrideFile.RelativePath);
            Assert.Equal("First", overrideFile.KeptFromCore);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PrepareRetriesAnIndividualPackageDownloadOnce()
    {
        string root = CreateTempFolder();
        try
        {
            byte[] archive = CreateArchive([("Cores/Example.Core/core.json", "new")]);
            var handler = new FlakyArchiveHandler(archive);
            var service = new CoreSyncService(new HttpClient(handler), Path.Combine(root, "staging"), Path.Combine(root, "backups"));
            var pocket = new PocketDrive(Path.Combine(root, "Pocket"), "Pocket", DriveType.Unknown, 0, 0, 0, [], 0, []);
            var core = new CoreComparison("Example.Core", "Example", "Test", "-", "1.0", false, true, "Available", "https://packages.example/core.zip");

            CoreSyncPreview preview = await service.PrepareAsync(pocket, [core]);

            Assert.True(preview.CanSync);
            Assert.Equal(2, handler.RequestCount);
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

    private sealed class ArchiveMapHandler(IReadOnlyDictionary<string, byte[]> archives) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archives[request.RequestUri!.ToString()])
            });
        }
    }

    private sealed class FlakyArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1) throw new HttpRequestException("Temporary network failure.");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive)
            });
        }
    }
}
