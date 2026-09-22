using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class AssetServiceTests
{
    [Fact]
    public void PrepareAndApplyImportsApgbPaletteAndBacksUpReplacement()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            string source = Path.Combine(root, "Forest.pal");
            Directory.CreateDirectory(Path.Combine(pocketPath, "Assets", "gb", "common", "palettes"));
            File.WriteAllBytes(source, Apgb(0x22));
            string existing = Path.Combine(pocketPath, "Assets", "gb", "common", "palettes", "Forest.pal");
            File.WriteAllBytes(existing, Apgb(0x11));
            var service = new AssetService(Path.Combine(root, "backups"));
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 2, ["Assets", "Cores"], 0, []);

            AssetImportPreview preview = service.PreparePaletteImport(pocket, [source]);

            Assert.True(preview.CanApply);
            Assert.True(Assert.Single(preview.Changes).ReplacesExisting);
            AssetImportResult result = service.Apply(preview);
            Assert.True(result.Succeeded);
            Assert.Equal(Apgb(0x22), File.ReadAllBytes(existing));
            Assert.NotNull(result.BackupPath);
            Assert.Equal(Apgb(0x11), File.ReadAllBytes(Path.Combine(result.BackupPath!, "Assets", "gb", "common", "palettes", "Forest.pal")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PrepareBlocksInvalidApgbButAllowsGbp()
    {
        string root = CreateTempFolder();
        try
        {
            string pocketPath = Path.Combine(root, "Pocket");
            string invalid = Path.Combine(root, "bad.pal");
            string gbp = Path.Combine(root, "core.gbp");
            File.WriteAllBytes(invalid, [1, 2, 3]);
            File.WriteAllBytes(gbp, [1, 2, 3, 4]);
            var service = new AssetService(Path.Combine(root, "backups"));
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 0, [], 0, []);

            AssetImportPreview preview = service.PreparePaletteImport(pocket, [invalid, gbp]);

            Assert.False(preview.CanApply);
            Assert.Contains("valid 56-byte APGB", Assert.Single(preview.Blockers));
            Assert.Equal("core", Assert.Single(preview.Changes).Name);
        }
        finally { Directory.Delete(root, true); }
    }

    private static byte[] Apgb(byte color)
    {
        byte[] value = Enumerable.Repeat(color, 56).ToArray();
        value[^5] = 0x81; value[^4] = (byte)'A'; value[^3] = (byte)'P'; value[^2] = (byte)'G'; value[^1] = (byte)'B';
        return value;
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "ez-pocket-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
