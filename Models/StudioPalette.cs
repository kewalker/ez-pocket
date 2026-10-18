namespace EzPocket.Models;

// Colors are stored light to dark in each group. APGB reverses each group on disk.
public sealed record StudioPalette(string Name, string Extension, uint[] Colors)
{
    public StudioPalette Copy(string? name = null) => new(name ?? Name, Extension, (uint[])Colors.Clone());
    public uint[] Group(int group) => Colors.Skip(group * 4).Take(4).ToArray();
}
