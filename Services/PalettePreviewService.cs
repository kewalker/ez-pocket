using SkiaSharp;

namespace EzPocket.Services;

public sealed record PaletteScreenshot(int Width, int Height, byte[] Shades, byte[] Alpha);

public static class PalettePreviewService
{
    public static PaletteScreenshot CalibrationPattern()
    {
        const int width = 160, height = 144;
        byte[] shades = new byte[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            shades[y * width + x] = (byte)(y < 32 ? x / 40 : y >= 112
                ? ((x / 8 + y / 8) % 2) * 3
                : Math.Max(Math.Abs(x - 80), Math.Abs(y - 72)) / 12 % 4);
        return new(width, height, shades, Enumerable.Repeat((byte)255, shades.Length).ToArray());
    }

    public static PaletteScreenshot Load(Stream stream)
    {
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Choose a PNG or JPEG screenshot.");
        var info = codec.Info;
        if (info.Width < 1 || info.Height < 1 || (long)info.Width * info.Height > 16_000_000)
            throw new InvalidDataException("Choose a screenshot with at most 16 million pixels.");
        using var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("The screenshot could not be decoded.");
        double scale = Math.Min(1, 640d / Math.Max(bitmap.Width, bitmap.Height));
        int width = Math.Max(1, (int)(bitmap.Width * scale));
        int height = Math.Max(1, (int)(bitmap.Height * scale));
        byte[] shades = new byte[width * height];
        byte[] alpha = new byte[shades.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var color = bitmap.GetPixel((int)(x / scale), (int)(y / scale));
            double luminance = (color.Red * .2126 + color.Green * .7152 + color.Blue * .0722) / 255;
            int index = y * width + x;
            shades[index] = (byte)Math.Clamp((int)Math.Round((1 - luminance) * 3), 0, 3);
            alpha[index] = color.Alpha;
        }
        return new(width, height, shades, alpha);
    }

    public static byte[] Render(PaletteScreenshot screenshot, uint[] colors)
    {
        if (colors.Length != 4) throw new ArgumentException("Four colors are required.", nameof(colors));
        using var bitmap = new SKBitmap(screenshot.Width, screenshot.Height);
        var pixels = new SKColor[screenshot.Shades.Length];
        for (int y = 0; y < screenshot.Height; y++)
        for (int x = 0; x < screenshot.Width; x++)
        {
            int index = y * screenshot.Width + x;
            uint rgb = colors[screenshot.Shades[index]];
            pixels[index] = new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, screenshot.Alpha[index]);
        }
        bitmap.Pixels = pixels;
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
