namespace EzPocket.Models;

public enum PocketHealthAction
{
    None,
    InitializeTarget,
    ManageCores,
    ManageAssets,
    ReturnToTarget
}

public sealed record PocketHealthFinding(
    string Severity,
    string Title,
    string Detail,
    string Key = "",
    PocketHealthAction Action = PocketHealthAction.None)
{
    public bool RequiresAttention => Severity is "ATTENTION" or "BLOCKED";
    public bool IsBlocking => Severity == "BLOCKED";
}

public sealed record PocketHealthReport(PocketDrive Pocket, IReadOnlyList<PocketHealthFinding> Findings)
{
    public int AttentionCount => Findings.Count(finding => finding.RequiresAttention);
    public bool CanWrite => Findings.All(finding => !finding.IsBlocking);
    public bool IsReady => AttentionCount == 0;
    public string Summary => IsReady ? "Ready for reviewed maintenance" : $"{AttentionCount} item{(AttentionCount == 1 ? string.Empty : "s")} need attention";
}
