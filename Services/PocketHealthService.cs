using System.Text.Json;
using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>Performs read-only checks that explain whether a target is ready for a reviewed change.</summary>
public sealed class PocketHealthService
{
    private static readonly string[] RequiredFolders = ["Assets", "Cores", "Platforms", "System"];
    private readonly IAppDiagnostics diagnostics;

    public PocketHealthService(IAppDiagnostics? diagnostics = null) => this.diagnostics = diagnostics ?? NullAppDiagnostics.Instance;

    public PocketHealthReport Inspect(PocketDrive pocket)
    {
        ArgumentNullException.ThrowIfNull(pocket);
        var findings = new List<PocketHealthFinding>();
        if (!Directory.Exists(pocket.RootPath))
        {
            findings.Add(new("BLOCKED", "Target is unavailable", "Reconnect the Pocket storage, then scan it again before making changes.", "target-unavailable", PocketHealthAction.ReturnToTarget));
            return new PocketHealthReport(pocket, findings);
        }

        string[] missingFolders = RequiredFolders.Where(folder => !Directory.Exists(Path.Combine(pocket.RootPath, folder))).ToArray();
        if (missingFolders.Length > 0)
            findings.Add(new("BLOCKED", "Pocket folders are incomplete", $"Missing: {string.Join(", ", missingFolders)}. Initialize the target before syncing so PocketOS and core files have their required structure.", "folders-incomplete", PocketHealthAction.InitializeTarget));

        InspectCoreMetadata(pocket, findings);
        if (pocket.TotalBytes > 0 && pocket.FreeBytes < Math.Max(512L * 1024 * 1024, pocket.TotalBytes / 10))
            findings.Add(new("ATTENTION", "Storage is running low", $"{pocket.CapacitySummary}. Free space may not be sufficient for staging and backups.", "storage-low", PocketHealthAction.ManageAssets));

        if (findings.Count == 0)
            findings.Add(new("READY", "Target structure looks healthy", "Required folders, installed core metadata, and available storage passed the local checks.", "target-healthy"));

        diagnostics.Info("PocketHealthChecked", new Dictionary<string, string?>
        {
            ["TargetId"] = AppDiagnosticsService.TargetId(pocket.RootPath),
            ["AttentionCount"] = findings.Count(finding => finding.RequiresAttention).ToString()
        });
        return new PocketHealthReport(pocket, findings);
    }

    private static void InspectCoreMetadata(PocketDrive pocket, ICollection<PocketHealthFinding> findings)
    {
        string coresPath = Path.Combine(pocket.RootPath, "Cores");
        if (!Directory.Exists(coresPath)) return;
        try
        {
            foreach (string corePath in Directory.EnumerateDirectories(coresPath))
            {
                string identifier = Path.GetFileName(corePath);
                string metadataPath = Path.Combine(corePath, "core.json");
                if (!File.Exists(metadataPath))
                {
                    findings.Add(new("ATTENTION", $"{identifier} is incomplete", "The installed core folder does not contain core.json. Reinstall this core from Manage cores.", $"core-incomplete:{identifier}", PocketHealthAction.ManageCores));
                    continue;
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath));
                    string? version = document.RootElement.GetProperty("core").GetProperty("metadata").GetProperty("version").GetString();
                    if (string.IsNullOrWhiteSpace(version)) throw new JsonException();
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException)
                {
                    findings.Add(new("ATTENTION", $"{identifier} metadata cannot be read", "core.json is missing required metadata. Reinstall this core from Manage cores.", $"core-metadata:{identifier}", PocketHealthAction.ManageCores));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            findings.Add(new("ATTENTION", "Core inventory could not be read", "Reconnect the target and scan it again before making changes.", "core-inventory-unavailable", PocketHealthAction.ReturnToTarget));
        }
    }
}
