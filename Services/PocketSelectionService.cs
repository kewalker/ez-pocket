using EzPocket.Models;

namespace EzPocket.Services;

public sealed class PocketSelectionService
{
    private const string SelectedPocketPathKey = "selected-pocket-path";

    public PocketDrive? SelectedPocket { get; private set; }
    private readonly IAppDiagnostics diagnostics;
    private readonly IPreferences preferences;

    public PocketSelectionService(IAppDiagnostics? diagnostics = null, IPreferences? preferences = null)
    {
        this.diagnostics = diagnostics ?? NullAppDiagnostics.Instance;
        this.preferences = preferences ?? Preferences.Default;
    }

    public void Select(PocketDrive pocket)
    {
        SelectedPocket = pocket;
        preferences.Set(SelectedPocketPathKey, pocket.RootPath);
        diagnostics.Info("PocketSelected", new Dictionary<string, string?> { ["TargetId"] = AppDiagnosticsService.TargetId(pocket.RootPath) });
    }

    public PocketDrive? Restore(PocketScanner scanner)
    {
        if (SelectedPocket is not null) return SelectedPocket;

        string? path = preferences.Get<string?>(SelectedPocketPathKey, null);
        if (string.IsNullOrWhiteSpace(path)) return null;

        SelectedPocket = scanner.ScanFolder(path);
        if (SelectedPocket is null) preferences.Remove(SelectedPocketPathKey);
        diagnostics.Info(SelectedPocket is null ? "PocketSelectionRestoreFailed" : "PocketSelectionRestored");
        return SelectedPocket;
    }

    public void Clear()
    {
        SelectedPocket = null;
        preferences.Remove(SelectedPocketPathKey);
        diagnostics.Info("PocketSelectionCleared");
    }
}
