using System.Text.Json.Serialization;

namespace EzPocket.Models;

public sealed record PocketPluginManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("allowed_hosts")] string[]? AllowedHosts);

public sealed record PocketPlugin(string Id, string Name, string Description, string Version,
    string Repository, IReadOnlyList<string> AllowedHosts)
{
    public string NetworkSummary => AllowedHosts.Count == 0 ? "Network: none" : $"Network: {string.Join(", ", AllowedHosts)}";
    public override string ToString() => Name;
}

public sealed record PocketPluginPrompt(string Name, string Query, IReadOnlyList<string>? Choices);
public sealed record PocketPluginChange(string RelativePath, string Action, long SizeBytes, string? BeforeHash, string? AfterHash);
public sealed record PocketPluginResult(bool Succeeded, string Message, string? BackupPath = null);

public sealed class PocketPluginPreview
{
    internal PocketPluginPreview(Guid id, PocketPlugin plugin, string targetPath, string workingPath,
        DateTime targetCreatedUtc, IReadOnlyList<string> targetDirectories,
        IReadOnlyDictionary<string, string> baseline, IEnumerable<PocketPluginChange> changes, IEnumerable<string> blockers)
    {
        Id = id;
        Plugin = plugin;
        TargetPath = targetPath;
        WorkingPath = workingPath;
        TargetCreatedUtc = targetCreatedUtc;
        TargetDirectories = targetDirectories;
        Baseline = baseline;
        Changes = Array.AsReadOnly(changes.ToArray());
        Blockers = Array.AsReadOnly(blockers.ToArray());
    }

    internal Guid Id { get; }
    internal string WorkingPath { get; }
    internal DateTime TargetCreatedUtc { get; }
    internal IReadOnlyList<string> TargetDirectories { get; }
    internal IReadOnlyDictionary<string, string> Baseline { get; }
    public PocketPlugin Plugin { get; }
    public string TargetPath { get; }
    public IReadOnlyList<PocketPluginChange> Changes { get; }
    public IReadOnlyList<string> Blockers { get; }
    public bool CanApply => Changes.Count > 0 && Blockers.Count == 0;
}
