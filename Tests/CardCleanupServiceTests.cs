using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class CardCleanupServiceTests : IDisposable
{
    private static readonly byte[] AppleDouble = [0x00, 0x05, 0x16, 0x07, 0x00, 0x02, 0x00, 0x00, 1, 2, 3];
    private static readonly byte[] DsStore = [0x00, 0x00, 0x00, 0x01, 0x42, 0x75, 0x64, 0x31, 9, 9];
    private readonly string root = Path.Combine(Path.GetTempPath(), "ez-pocket-cleanup-" + Guid.NewGuid().ToString("N"));

    public CardCleanupServiceTests()
    {
        foreach (string folder in new[] { "Assets", "Cores", "Platforms", "System", "Saves" }) Directory.CreateDirectory(Path.Combine(root, folder));
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void PreviewFindsOnlyVerifiedMacMetadata()
    {
        Write("Assets/gb/common/._Tetris.gb", AppleDouble);
        Write("Cores/._Author.Core", AppleDouble);
        Write("Assets/gb/common/._notreally.gb", [1, 2, 3, 4, 5]);
        Write("Assets/gb/common/Tetris.gb", [1, 2, 3]);
        Write(".DS_Store", DsStore);
        Write("Saves/.DS_Store", [0, 0, 0, 0]);

        CardCleanupPreview preview = Preview();

        Assert.Equal(["Assets/gb/common/._Tetris.gb", "Cores/._Author.Core"], Group(preview, CleanupCategory.AppleDouble).Items.Select(item => item.RelativePath));
        Assert.Equal(".DS_Store", Assert.Single(Group(preview, CleanupCategory.DesktopServicesStore).Items).RelativePath);
        Assert.Empty(Group(preview, CleanupCategory.MacTrash).Items);
    }

    [Fact]
    public void ApplyRemovesSelectedCategoriesAndKeepsEverythingElse()
    {
        Write("Assets/gb/common/._Tetris.gb", AppleDouble);
        Write("Assets/gb/common/Tetris.gb", [1, 2, 3]);
        Write(".DS_Store", DsStore);

        var service = new CardCleanupService();
        CardCleanupPreview preview = service.Preview(Pocket());
        CardCleanupResult result = service.Apply(preview, [CleanupCategory.AppleDouble]);

        Assert.Equal(1, result.Removed);
        Assert.True(result.Succeeded);
        Assert.False(File.Exists(Path.Combine(root, "Assets/gb/common/._Tetris.gb")));
        Assert.True(File.Exists(Path.Combine(root, "Assets/gb/common/Tetris.gb")));
        Assert.True(File.Exists(Path.Combine(root, ".DS_Store")));
    }

    [Fact]
    public void ApplySkipsFilesThatChangedAfterTheReview()
    {
        Write("._changed", AppleDouble);
        Write("._replaced", AppleDouble);
        var service = new CardCleanupService();
        CardCleanupPreview preview = service.Preview(Pocket());
        Write("._changed", [.. AppleDouble, 4, 5]);
        Write("._replaced", [9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9]);

        CardCleanupResult result = service.Apply(preview, [CleanupCategory.AppleDouble]);

        Assert.Equal(0, result.Removed);
        Assert.Equal(2, result.Skipped);
        Assert.True(File.Exists(Path.Combine(root, "._changed")));
        Assert.True(File.Exists(Path.Combine(root, "._replaced")));
    }

    [Fact]
    public void TrashIsOptInAndLeftoverAssetFoldersAreReportOnly()
    {
        Write(".Trashes/501/old-rom.gba", new byte[100]);
        Write(".Trashes/501/._old-rom.gba", AppleDouble);
        Directory.CreateDirectory(Path.Combine(root, "Cores", "Author.Installed"));
        Write("Assets/gba/Author.Installed/bios.bin", new byte[4]);
        Write("Assets/gba/Author.Removed/game.gba", new byte[10]);
        Write("Assets/gba/common/game.gba", new byte[10]);

        CardCleanupPreview preview = Preview();

        CleanupGroup trash = Group(preview, CleanupCategory.MacTrash);
        Assert.False(trash.SelectedByDefault);
        Assert.Equal(100 + AppleDouble.Length, Assert.Single(trash.Items).Bytes);
        Assert.Empty(Group(preview, CleanupCategory.AppleDouble).Items);
        LeftoverAssetFolder leftover = Assert.Single(preview.LeftoverFolders);
        Assert.Equal("Assets/gba/Author.Removed", leftover.RelativePath);
        Assert.Equal(1, leftover.FileCount);

        new CardCleanupService().Apply(preview, [CleanupCategory.MacTrash]);
        Assert.False(Directory.Exists(Path.Combine(root, ".Trashes")));
        Assert.True(File.Exists(Path.Combine(root, "Assets/gba/Author.Removed/game.gba")));
    }

    [Fact]
    public void ApplyRefusesItemsOutsideTheTarget()
    {
        string outside = Path.Combine(Path.GetTempPath(), "._outside-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(outside, AppleDouble);
        try
        {
            var forged = new CardCleanupPreview(Pocket(),
                [new CleanupGroup(CleanupCategory.AppleDouble, "", "", true, [new CleanupItem(outside, "x", CleanupCategory.AppleDouble, AppleDouble.Length, false)])], [], 0, false);

            CardCleanupResult result = new CardCleanupService().Apply(forged, [CleanupCategory.AppleDouble]);

            Assert.Equal(1, result.Skipped);
            Assert.True(File.Exists(outside));
        }
        finally { File.Delete(outside); }
    }

    private CardCleanupPreview Preview() => new CardCleanupService().Preview(Pocket());
    private PocketDrive Pocket() => new(root, "Pocket", DriveType.Unknown, 0, 0, 4, [], 0, []);
    private static CleanupGroup Group(CardCleanupPreview preview, CleanupCategory category) => preview.Groups.Single(group => group.Category == category);

    private void Write(string relative, byte[] content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }
}
