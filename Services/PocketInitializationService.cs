using EzPocket.Models;

namespace EzPocket.Services;

public sealed class PocketInitializationService
{
    public static readonly IReadOnlyList<string> RequiredFolders = ["Assets", "Cores", "Platforms", "System"];

    public PocketInitializationPreview Preview(PocketDrive pocket)
    {
        var missing = RequiredFolders.Where(folder => !Directory.Exists(Path.Combine(pocket.RootPath, folder))).ToArray();
        return new PocketInitializationPreview(pocket.RootPath, missing);
    }

    public PocketInitializationPreview Initialize(PocketDrive pocket)
    {
        PocketInitializationPreview preview = Preview(pocket);
        foreach (string folder in preview.MissingFolders)
            Directory.CreateDirectory(Path.Combine(pocket.RootPath, folder));
        return Preview(pocket);
    }
}
