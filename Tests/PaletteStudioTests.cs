using EzPocket.Models;
using EzPocket.Services;
using SkiaSharp;
using Xunit;

namespace EzPocket.Tests;

public sealed class PaletteStudioTests
{
    [Fact]
    public void GbpUsesRgbLightToDarkAndFourZeroFooterBytes()
    {
        byte[] bytes = [255, 238, 221, 170, 153, 136, 85, 68, 51, 17, 34, 51, 0, 0, 0, 0];
        var palette = PaletteCodec.Read("Fixture", ".gbp", bytes);
        Assert.Equal(new uint[] { 0xFFEEDD, 0xAA9988, 0x554433, 0x112233 }, palette.Colors);
        Assert.Equal(bytes, PaletteCodec.Write(palette));
    }

    [Fact]
    public void EditingApgbBackgroundPreservesSpritesWindowAndLcdOff()
    {
        // Independent on-disk fixture: four dark-to-light RGB groups, LCD off, footer.
        byte[] bytes = Enumerable.Range(0, 51).Select(i => (byte)i).Concat(new byte[] { 0x81, 65, 80, 71, 66 }).ToArray();
        var palette = PaletteCodec.Read("Layers", ".pal", bytes);
        Assert.Equal(0x090A0Bu, palette.Colors[0]);
        Assert.Equal(0x000102u, palette.Colors[3]);
        Assert.Equal(0x303132u, palette.Colors[16]);
        Assert.Equal(bytes, PaletteCodec.Write(palette));
        palette.Colors[0] = 0xFFEECC;
        byte[] changed = PaletteCodec.Write(palette);
        Assert.Equal(new byte[] { 255, 238, 204 }, changed[9..12]);
        Assert.Equal(bytes[12..], changed[12..]);
        Assert.Equal(bytes[..9], changed[..9]);
    }

    [Theory]
    [InlineData(".gbp", 4)]
    [InlineData(".pal", 55)]
    [InlineData(".png", 16)]
    [InlineData(".pal", 56)]
    public void InvalidPaletteFilesAreRejected(string extension, int length) =>
        Assert.Throws<InvalidDataException>(() => PaletteCodec.Read("Invalid", extension, new byte[length]));

    [Fact]
    public void GbpRejectsInvalidFooter() => Assert.Throws<InvalidDataException>(() =>
        PaletteCodec.Read("Invalid", ".gbp", Enumerable.Repeat((byte)1, 16).ToArray()));

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:\\file")]
    [InlineData("NUL")]
    [InlineData("COM1.test")]
    [InlineData("bad.")]
    [InlineData("")]
    public void ExportNamesCannotEscapeTheChosenFolder(string name) =>
        Assert.Throws<InvalidDataException>(() => PaletteStudioLibrary.FileName(name, ".gbp"));

    [Fact]
    public void ScreenshotMapsFourGrayLevelsAndPreservesTransparency()
    {
        using var source = new SKBitmap(4, 1);
        source.Pixels = [new(255, 255, 255), new(170, 170, 170), new(85, 85, 85), new(0, 0, 0, 100)];
        using var image = SKImage.FromBitmap(source);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(encoded.ToArray());
        var screenshot = PalettePreviewService.Load(stream);
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, screenshot.Shades);
        using var result = SKBitmap.Decode(PalettePreviewService.Render(screenshot, [0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF]));
        Assert.Equal(new SKColor(255, 0, 0), result.GetPixel(1, 0));
        Assert.Equal(new SKColor(0, 255, 0), result.GetPixel(2, 0));
        Assert.Equal((byte)100, result.GetPixel(3, 0).Alpha);
        Assert.Equal((byte)255, result.GetPixel(3, 0).Blue);
    }

    [Fact]
    public void FavoritesSurviveRestartWithoutTheOriginalPaletteFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "ez-pocket-studio-test-" + Guid.NewGuid());
        try
        {
            var palette = new StudioPalette("My palette", ".gbp", [0xFFFFFF, 0xAAAAAA, 0x555555, 0]);
            new PaletteStudioLibrary(root).SaveFavorites([palette]);
            var restored = Assert.Single(new PaletteStudioLibrary(root).LoadFavorites());
            Assert.Equal(palette.Name, restored.Name);
            Assert.Equal(PaletteCodec.Write(palette), PaletteCodec.Write(restored));
            new PaletteStudioLibrary(root).SaveFavorites([]);
            Assert.Empty(new PaletteStudioLibrary(root).LoadFavorites());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ExportPreservesExistingFilesAndRequiresReviewForPocketPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "ez-pocket-studio-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            var palette = new StudioPalette("Paper", ".gbp", [0xFFFFFF, 0xAAAAAA, 0x555555, 0]);
            Assert.Throws<InvalidDataException>(() => PaletteStudioLibrary.Export(palette, root, root));
            Assert.Empty(Directory.GetFiles(root));
            PaletteStudioLibrary.Export(palette, root);
            byte[] exported = File.ReadAllBytes(Path.Combine(root, "Paper.gbp"));
            Assert.Equal(PaletteCodec.Write(palette), exported);
            palette.Colors[0] = 0;
            Assert.Throws<IOException>(() => PaletteStudioLibrary.Export(palette, root));
            Assert.Equal(exported, File.ReadAllBytes(Path.Combine(root, "Paper.gbp")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
