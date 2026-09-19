using EzPocket.Models;

namespace EzPocket.Services;

public sealed class PocketInitializationService
{
    public static readonly IReadOnlyList<string> RequiredFolders = ["Assets", "Cores", "Platforms", "System"];
    private readonly IAppDiagnostics diagnostics;

    public PocketInitializationService(IAppDiagnostics? diagnostics = null) => this.diagnostics = diagnostics ?? NullAppDiagnostics.Instance;

    public PocketInitializationPreview Preview(PocketDrive pocket)
    {
        var missing = RequiredFolders.Where(folder => !Directory.Exists(Path.Combine(pocket.RootPath, folder))).ToArray();
        return new PocketInitializationPreview(pocket.RootPath, missing);
    }

    public PocketInitializationPreview Initialize(PocketDrive pocket)
    {
        PocketInitializationPreview preview = Preview(pocket);
        diagnostics.Info("PocketInitializationStarted", new Dictionary<string, string?>
        {
            ["TargetId"] = AppDiagnosticsService.TargetId(pocket.RootPath),
            ["FolderCount"] = preview.MissingFolders.Count.ToString()
        });
        foreach (string folder in preview.MissingFolders)
            Directory.CreateDirectory(Path.Combine(pocket.RootPath, folder));
        PocketInitializationPreview result = Preview(pocket);
        diagnostics.Info("PocketInitializationCompleted", new Dictionary<string, string?> { ["RemainingFolderCount"] = result.MissingFolders.Count.ToString() });
        return result;
    }
}
