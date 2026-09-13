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
                    string[] installedCoreNames = Directory.Exists(coresPath)
                        ? Directory.EnumerateDirectories(coresPath).Select(Path.GetFileName).Where(name => name is not null).Cast<string>().OrderBy(name => name).ToArray()
                        : [];
                    int coreCount = installedCoreNames.Length;
                    results.Add(new PocketDrive(drive.RootDirectory.FullName,
                        string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Removable drive" : drive.VolumeLabel,
                        drive.DriveType, drive.TotalSize, drive.AvailableFreeSpace, foundFolders.Length, foundFolders, coreCount, installedCoreNames));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return results;
    }
}
