using EzPocket.Models;

namespace EzPocket.Services;

public static class PaletteCodec
{
    public static StudioPalette Read(string name, string extension, byte[] bytes)
    {
        extension = extension.ToLowerInvariant();
        bool apgb = extension == ".pal";
        if ((!apgb && extension != ".gbp") || bytes.Length != (apgb ? 56 : 16)
            || (apgb ? !bytes.AsSpan(51).SequenceEqual(new byte[] { 0x81, 65, 80, 71, 66 })
                : !bytes.AsSpan(12).SequenceEqual(new byte[4])))
            throw new InvalidDataException("Choose a valid 16-byte GBP or 56-byte APGB palette.");

        var colors = new uint[apgb ? 17 : 4];
        for (int i = 0; i < colors.Length; i++)
        {
            int index = apgb && i < 16 ? (i / 4 * 4 + 3 - i % 4) * 3 : i * 3;
            colors[i] = (uint)(bytes[index] << 16 | bytes[index + 1] << 8 | bytes[index + 2]);
        }
        return new(name, extension, colors);
    }

    public static byte[] Write(StudioPalette palette)
    {
        if (palette is null || palette.Colors is null) throw new InvalidDataException("Palette data is missing.");
        bool apgb = palette.Extension == ".pal";
        if ((!apgb && palette.Extension != ".gbp") || palette.Colors.Length != (apgb ? 17 : 4)
            || palette.Colors.Any(color => color > 0xFFFFFF))
            throw new InvalidDataException("Palette colors or format are invalid.");
        byte[] bytes = new byte[apgb ? 56 : 16];
        for (int i = 0; i < palette.Colors.Length; i++)
        {
            int index = apgb && i < 16 ? (i / 4 * 4 + 3 - i % 4) * 3 : i * 3;
            uint color = palette.Colors[i];
            bytes[index] = (byte)(color >> 16);
            bytes[index + 1] = (byte)(color >> 8);
            bytes[index + 2] = (byte)color;
        }
        if (apgb) new byte[] { 0x81, 65, 80, 71, 66 }.CopyTo(bytes, 51);
        return bytes;
    }
}
