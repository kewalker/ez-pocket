using EzPocket.Models;
using System.Text.Json;

namespace EzPocket.Services;

public sealed class PocketSelectionService
{
    private const string SelectedPocketPathKey = "selected-pocket-path";
    private const string SelectedPocketSnapshotKey = "selected-pocket-snapshot";
    public DateTimeOffset? LastScannedAt { get; private set; }
    public PocketHealthReport? LastHealthReport { get; private set; }
    public DateTimeOffset? LastHealthCheckedAt { get; private set; }

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
        LastScannedAt = DateTimeOffset.Now;
        LastHealthReport = null;
        LastHealthCheckedAt = null;
        preferences.Set(SelectedPocketPathKey, pocket.RootPath);
        string snapshot = JsonSerializer.Serialize(new RememberedTarget(pocket, LastScannedAt.Value));
        // Windows preferences limit each value's size; inventories can contain hundreds of names.
        int previousParts = preferences.Get(SelectedPocketSnapshotKey, 0);
        int parts = (snapshot.Length + 1999) / 2000;
        for (int part = 0; part < parts; part++)
            preferences.Set($"{SelectedPocketSnapshotKey}-{part}", snapshot.Substring(part * 2000, Math.Min(2000, snapshot.Length - part * 2000)));
        for (int part = parts; part < previousParts; part++) preferences.Remove($"{SelectedPocketSnapshotKey}-{part}");
        preferences.Set(SelectedPocketSnapshotKey, parts);
        diagnostics.Info("PocketSelected", new Dictionary<string, string?> { ["TargetId"] = AppDiagnosticsService.TargetId(pocket.RootPath) });
    }

    public PocketDrive? Restore()
    {
        if (SelectedPocket is not null) return SelectedPocket;

        string? path = preferences.Get<string?>(SelectedPocketPathKey, null);
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            int parts = preferences.Get(SelectedPocketSnapshotKey, 0);
            string json = parts is > 0 and <= 1000
                ? string.Concat(Enumerable.Range(0, parts).Select(part => preferences.Get($"{SelectedPocketSnapshotKey}-{part}", ""))) : "null";
            var saved = JsonSerializer.Deserialize<RememberedTarget>(json);
            if (saved is not null && TargetPaths.Equal(saved.Pocket.RootPath, path))
            {
                SelectedPocket = saved.Pocket;
                LastScannedAt = saved.ScannedAt;
            }
        }
        catch (JsonException) { preferences.Remove(SelectedPocketSnapshotKey); }
        // Older installations have only a path. Do not scan it during startup.
        SelectedPocket ??= new PocketDrive(path, Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), DriveType.Unknown, 0, 0, 0, [], 0, []);
        diagnostics.Info("PocketSelectionRestored");
        return SelectedPocket;
    }

    public void RecordHealth(PocketHealthReport report)
    {
        if (!TargetPaths.Equal(SelectedPocket?.RootPath, report.Pocket.RootPath)) return;
        LastHealthReport = report;
        LastHealthCheckedAt = DateTimeOffset.Now;
    }

    public void Disconnect()
    {
        SelectedPocket = null;
        LastScannedAt = null;
        LastHealthReport = null;
        LastHealthCheckedAt = null;
    }

    public void Clear()
    {
        Disconnect();
        preferences.Remove(SelectedPocketPathKey);
        int parts = preferences.Get(SelectedPocketSnapshotKey, 0);
        for (int part = 0; part < parts; part++) preferences.Remove($"{SelectedPocketSnapshotKey}-{part}");
        preferences.Remove(SelectedPocketSnapshotKey);
        diagnostics.Info("PocketSelectionCleared");
    }

    private sealed record RememberedTarget(PocketDrive Pocket, DateTimeOffset ScannedAt);
}
