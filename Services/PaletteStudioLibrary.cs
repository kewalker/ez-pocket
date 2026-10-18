using System.Security.Cryptography;
using System.Text.Json;
using EzPocket.Models;

namespace EzPocket.Services;

public sealed class PaletteStudioLibrary
{
    private readonly string favoritesPath;
    public PaletteStudioLibrary(string? directory = null) =>
        favoritesPath = Path.Combine(directory ?? FileSystem.AppDataDirectory, "palette-favorites.json");

    public static string Key(StudioPalette palette) => Convert.ToHexString(SHA256.HashData(PaletteCodec.Write(palette)));

    public IReadOnlyList<StudioPalette> LoadFavorites()
    {
        if (!File.Exists(favoritesPath)) return [];
        if (new FileInfo(favoritesPath).Length > 2_000_000)
            throw new InvalidDataException("The saved palette library is too large.");
        var palettes = JsonSerializer.Deserialize<List<StudioPalette>>(File.ReadAllText(favoritesPath)) ?? [];
        foreach (var palette in palettes)
        {
            _ = PaletteCodec.Write(palette);
            _ = FileName(palette.Name, palette.Extension);
        }
        return palettes;
    }

    public void SaveFavorites(IEnumerable<StudioPalette> palettes)
    {
        var items = palettes.Select(palette => palette.Copy()).ToArray();
        foreach (var palette in items) { _ = PaletteCodec.Write(palette); _ = FileName(palette.Name, palette.Extension); }
        if (items.Length > 1000) throw new InvalidDataException("The library supports up to 1,000 favorites.");
        Directory.CreateDirectory(Path.GetDirectoryName(favoritesPath)!);
        string temporary = favoritesPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(items));
        File.Move(temporary, favoritesPath, true);
    }

    public static StudioPalette ReadFile(string path)
    {
        if (new FileInfo(path).Length > 56) throw new InvalidDataException("This is not a supported palette file.");
        return PaletteCodec.Read(Path.GetFileNameWithoutExtension(path), Path.GetExtension(path), File.ReadAllBytes(path));
    }

    public static void Export(StudioPalette palette, string folder, string? pocketRoot = null)
    {
        string path = Path.GetFullPath(Path.Combine(folder, FileName(palette.Name, palette.Extension)));
        if (pocketRoot is not null)
        {
            string relative = Path.GetRelativePath(pocketRoot, path);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Use REVIEW INSTALL for the selected Pocket. Choose a local folder for export.");
        }
        byte[] bytes = PaletteCodec.Write(palette);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        output.Write(bytes);
    }

    public static string FileName(string name, string extension)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Enter a palette name.");
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))
            || name.EndsWith('.') || name is "." or "..")
            throw new InvalidDataException("Use a palette name of 1–80 letters, numbers, spaces, or safe punctuation.");
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem))
            throw new InvalidDataException("Choose a different palette name.");
        if (extension is not (".gbp" or ".pal")) throw new InvalidDataException("Choose a supported palette format.");
        return name + extension;
    }
}
