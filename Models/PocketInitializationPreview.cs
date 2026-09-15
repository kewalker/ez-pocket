namespace EzPocket.Models;

public sealed record PocketInitializationPreview(string RootPath, IReadOnlyList<string> MissingFolders)
{
    public bool IsRequired => MissingFolders.Count > 0;
}
