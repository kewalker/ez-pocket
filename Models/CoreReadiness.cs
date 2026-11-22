namespace EzPocket.Models;

public enum CoreReadinessState
{
    Ready,
    Attention,
    Missing,
    Unreadable
}

public enum RequiredFileState
{
    Present,
    Missing,
    WrongFolder,
    WrongSize,
    OptionalAbsent
}

/// <summary>A fixed-name file a core loads by itself, such as a BIOS, as declared by its data.json.</summary>
public sealed record CoreRequiredFile(
    string SlotName,
    string FileName,
    bool Required,
    string ExpectedRelativePath,
    RequiredFileState State,
    string Detail)
{
    public bool NeedsAction => State is RequiredFileState.Missing or RequiredFileState.WrongFolder or RequiredFileState.WrongSize;
    public string StateLabel => State switch
    {
        RequiredFileState.Present => "PRESENT",
        RequiredFileState.Missing => "MISSING",
        RequiredFileState.WrongFolder => "WRONG FOLDER",
        RequiredFileState.WrongSize => "WRONG SIZE",
        _ => "OPTIONAL"
    };
}

public sealed record CoreReadiness(
    string Identifier,
    string Name,
    string Version,
    CoreReadinessState State,
    string Summary,
    IReadOnlyList<CoreRequiredFile> Files,
    IReadOnlyList<string> Notes)
{
    public string StateLabel => State switch
    {
        CoreReadinessState.Ready => "READY",
        CoreReadinessState.Attention => "CHECK",
        CoreReadinessState.Missing => "MISSING FILES",
        _ => "UNREADABLE"
    };
    public bool NeedsAction => State != CoreReadinessState.Ready;
}

public sealed record CoreReadinessReport(PocketDrive Pocket, IReadOnlyList<CoreReadiness> Cores, bool Limited)
{
    public int ReadyCount => Cores.Count(core => core.State == CoreReadinessState.Ready);
    public int ActionCount => Cores.Count(core => core.NeedsAction);
    public string Summary => Cores.Count == 0
        ? "No installed cores found"
        : ActionCount == 0
            ? $"All {Cores.Count} installed core{(Cores.Count == 1 ? "" : "s")} have their fixed files"
            : $"{ActionCount} of {Cores.Count} core{(Cores.Count == 1 ? "" : "s")} need attention";
}
