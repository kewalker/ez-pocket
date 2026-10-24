using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class PocketMountDiscoveryTests
{
    [Fact]
    public void MountedVolumeScanExcludesStartupLayoutAndNestedTargets()
    {
        string root = Path.Combine(Path.GetTempPath(), "ez-pocket-tests", Guid.NewGuid().ToString("N"));
        try
        {
            // macOS's startup volume matches the old two-folder heuristic.
            AddFolders("Macintosh HD", "Cores", "System");
            AddFolders("Pocket SD", "Assets", "Cores", "Platforms", "System");
            AddFolders("Other disk", "Documents");
            AddFolders(Path.Combine("Other disk", "Nested sample"), "Assets", "Cores");

            var results = new PocketScanner().ScanMountedVolumes(root);

            var pocket = Assert.Single(results);
            Assert.Equal(Path.Combine(root, "Pocket SD"), pocket.RootPath);
            Assert.Equal(4, pocket.PocketFolderCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        void AddFolders(string volume, params string[] folders)
        {
            foreach (string folder in folders) Directory.CreateDirectory(Path.Combine(root, volume, folder));
        }
    }

    [Fact]
    public void MissingMountDirectoryReturnsNoTargets()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Empty(new PocketScanner().ScanMountedVolumes(missing));
    }
}
