using EzPocket.Models;

namespace EzPocket.Services;

public sealed class PocketScanner
{
    private static readonly string[] PocketFolders = ["Assets", "Cores", "Platforms", "System"];

    public IReadOnlyList<PocketDrive> Scan()
    {
        var results = new List<PocketDrive>();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var foundFolders = PocketFolders.Where(folder => Directory.Exists(Path.Combine(drive.RootDirectory.FullName, folder))).ToArray();
                if (drive.DriveType == DriveType.Removable || foundFolders.Length >= 2)
                {
                    string coresPath = Path.Combine(drive.RootDirectory.FullName, "Cores");
                    int coreCount = Directory.Exists(coresPath) ? Directory.EnumerateDirectories(coresPath).Count() : 0;
                    results.Add(new PocketDrive(drive.RootDirectory.FullName,
                        string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Removable drive" : drive.VolumeLabel,
                        drive.DriveType, drive.TotalSize, drive.AvailableFreeSpace, foundFolders.Length, foundFolders, coreCount));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return results;
    }
}
