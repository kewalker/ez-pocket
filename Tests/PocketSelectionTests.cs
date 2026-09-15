using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class PocketSelectionTests
{
    [Fact]
    public void BlankFolderCanBeScannedWithoutBeingRecognized()
    {
        string path = CreateTempFolder();
        try
        {
            PocketDrive? pocket = new PocketScanner().ScanFolder(path);
            Assert.NotNull(pocket);
            Assert.False(pocket.LooksLikePocket);
            Assert.Empty(pocket.FoundFolders);
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public void InitializationPreviewListsOnlyMissingFolders()
    {
        string path = CreateTempFolder();
        try
        {
            Directory.CreateDirectory(Path.Combine(path, "Cores"));
            PocketDrive pocket = new PocketScanner().ScanFolder(path)!;
            PocketInitializationPreview preview = new PocketInitializationService().Preview(pocket);
            Assert.Equal(["Assets", "Platforms", "System"], preview.MissingFolders);
        }
        finally { Directory.Delete(path, true); }
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "ez-pocket-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
