using EzPocket.Models;

namespace EzPocket.Services;

public sealed class PocketSelectionService
{
    private const string SelectedPocketPathKey = "selected-pocket-path";

    public PocketDrive? SelectedPocket { get; private set; }

    public void Select(PocketDrive pocket)
    {
        SelectedPocket = pocket;
        Preferences.Default.Set(SelectedPocketPathKey, pocket.RootPath);
    }

    public PocketDrive? Restore(PocketScanner scanner)
    {
        if (SelectedPocket is not null) return SelectedPocket;

        string? path = Preferences.Default.Get<string?>(SelectedPocketPathKey, null);
        if (string.IsNullOrWhiteSpace(path)) return null;

        SelectedPocket = scanner.ScanFolder(path);
        if (SelectedPocket is null) Preferences.Default.Remove(SelectedPocketPathKey);
        return SelectedPocket;
    }

    public void Clear()
    {
        SelectedPocket = null;
        Preferences.Default.Remove(SelectedPocketPathKey);
    }
}
