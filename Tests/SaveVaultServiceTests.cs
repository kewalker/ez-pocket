using System.IO.Compression;
using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class SaveVaultServiceTests
{
    [Fact]
    public async Task CreateSnapshotArchivesSavesAndMemoriesWithoutChangingTarget()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            string savePath = Path.Combine(pocketPath, "Saves", "gb", "game.sav");
            string memoryPath = Path.Combine(pocketPath, "Memories", "gb", "state.mem");
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(memoryPath)!);
            File.WriteAllBytes(savePath, [1, 2, 3]);
            File.WriteAllBytes(memoryPath, [4, 5]);
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 2, ["Assets", "Cores"], 0, []);
            var service = new SaveVaultService(Path.Combine(root, "vault"));

            SaveVaultPreview preview = service.Preview(pocket);
            SaveVaultResult result = await service.CreateSnapshotAsync(preview);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Snapshot);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(savePath));
            Assert.Equal([4, 5], File.ReadAllBytes(memoryPath));
            using ZipArchive archive = ZipFile.OpenRead(result.Snapshot!.ArchivePath);
            Assert.NotNull(archive.GetEntry("Saves/gb/game.sav"));
            Assert.NotNull(archive.GetEntry("Memories/gb/state.mem"));
            Assert.Single(service.List(pocket));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CreateSnapshotRefusesAnEmptyTarget()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            Directory.CreateDirectory(pocketPath);
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 0, [], 0, []);
            var service = new SaveVaultService(Path.Combine(root, "vault"));

            SaveVaultResult result = await service.CreateSnapshotAsync(service.Preview(pocket));

            Assert.False(result.Succeeded);
            Assert.Empty(service.List(pocket));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CreateSnapshotReportsWhatWasArchivedWhenFilesChangeAfterPreview()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            string saves = Path.Combine(pocketPath, "Saves");
            Directory.CreateDirectory(saves);
            File.WriteAllBytes(Path.Combine(saves, "before.sav"), [1]);
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 2, ["Assets", "Cores"], 0, []);
            var service = new SaveVaultService(Path.Combine(root, "vault"));

            SaveVaultPreview preview = service.Preview(pocket);
            File.WriteAllBytes(Path.Combine(saves, "after.sav"), [2, 3]);
            SaveVaultResult result = await service.CreateSnapshotAsync(preview);

            Assert.True(result.Succeeded);
            Assert.Equal(2, result.Snapshot!.FileCount);
            Assert.Equal(3, result.Snapshot.SizeBytes);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ListHandlesACorruptManifest()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            Directory.CreateDirectory(pocketPath);
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 0, [], 0, []);
            var service = new SaveVaultService(Path.Combine(root, "vault"));
            string vault = Path.Combine(root, "vault", AppDiagnosticsService.TargetId(pocketPath));
            Directory.CreateDirectory(vault);
            using (ZipArchive archive = ZipFile.Open(Path.Combine(vault, "corrupt.zip"), ZipArchiveMode.Create))
            using (StreamWriter writer = new(archive.CreateEntry("ez-pocket-save-vault.json").Open()))
                writer.Write("not json");

            SaveVaultSnapshot snapshot = Assert.Single(service.List(pocket));

            Assert.Equal(0, snapshot.FileCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RestorePreviewComparesFilesAndSelectiveRestoreKeepsTargetOnlyFiles()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            string savesPath = Path.Combine(pocketPath, "Saves", "gb");
            Directory.CreateDirectory(savesPath);
            string changedSave = Path.Combine(savesPath, "game.sav");
            File.WriteAllBytes(changedSave, [1, 2, 3]);
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 1, ["Saves"], 0, []);
            var service = new SaveVaultService(Path.Combine(root, "vault"));
            SaveVaultSnapshot snapshot = (await service.CreateSnapshotAsync(service.Preview(pocket))).Snapshot!;

            File.WriteAllBytes(changedSave, [9, 9, 9]);
            string targetOnlySave = Path.Combine(savesPath, "later.sav");
            File.WriteAllBytes(targetOnlySave, [7]);

            SaveVaultRestorePreview restorePreview = service.PreviewRestore(pocket, snapshot);

            Assert.True(restorePreview.CanRestore);
            Assert.Contains(restorePreview.Entries, entry => entry.RelativePath == "Saves/gb/game.sav" && entry.ChangeKind == SaveVaultRestoreChangeKind.Changed);
            Assert.Contains(restorePreview.Entries, entry => entry.RelativePath == "Saves/gb/later.sav" && entry.ChangeKind == SaveVaultRestoreChangeKind.TargetOnly);

            SaveVaultRestoreResult result = await service.RestoreAsync(restorePreview, ["Saves/gb/game.sav"]);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.SafetySnapshot);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(changedSave));
            Assert.Equal([7], File.ReadAllBytes(targetOnlySave));
            Assert.Equal(2, service.List(pocket).Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RestoreRefusesWhenTargetIsUnavailableAfterPreview()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            string savesPath = Path.Combine(pocketPath, "Saves");
            Directory.CreateDirectory(savesPath);
            File.WriteAllBytes(Path.Combine(savesPath, "game.sav"), [1]);
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 1, ["Saves"], 0, []);
            var service = new SaveVaultService(Path.Combine(root, "vault"));
            SaveVaultSnapshot snapshot = (await service.CreateSnapshotAsync(service.Preview(pocket))).Snapshot!;
            File.WriteAllBytes(Path.Combine(savesPath, "game.sav"), [2]);
            SaveVaultRestorePreview restorePreview = service.PreviewRestore(pocket, snapshot);
            Directory.Delete(pocketPath, true);

            SaveVaultRestoreResult result = await service.RestoreAsync(restorePreview, ["Saves/game.sav"]);

            Assert.False(result.Succeeded);
            Assert.Contains("unavailable", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RestoreRefusesWhenASnapshotOnlyFileAppearsAfterPreview()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            string savesPath = Path.Combine(pocketPath, "Saves");
            Directory.CreateDirectory(savesPath);
            string savePath = Path.Combine(savesPath, "game.sav");
            File.WriteAllBytes(savePath, [1]);
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 1, ["Saves"], 0, []);
            var service = new SaveVaultService(Path.Combine(root, "vault"));
            SaveVaultSnapshot snapshot = (await service.CreateSnapshotAsync(service.Preview(pocket))).Snapshot!;
            File.Delete(savePath);

            SaveVaultRestorePreview restorePreview = service.PreviewRestore(pocket, snapshot);
            Assert.Contains(restorePreview.Entries, entry => entry.RelativePath == "Saves/game.sav" && entry.ChangeKind == SaveVaultRestoreChangeKind.SnapshotOnly);
            File.WriteAllBytes(savePath, [9]);

            SaveVaultRestoreResult result = await service.RestoreAsync(restorePreview, ["Saves/game.sav"]);

            Assert.False(result.Succeeded);
            Assert.Contains("changed after this review", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(result.SafetySnapshot);
            Assert.Equal([9], File.ReadAllBytes(savePath));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "ez-pocket-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
