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
                if (foundFolders.Length >= 2)
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

    public PocketDrive? ScanFolder(string folderPath)
    {
        try
        {
            string rootPath = Path.GetFullPath(folderPath.Trim());
            if (!Directory.Exists(rootPath)) return null;

            var directory = new DirectoryInfo(rootPath);
            var foundFolders = PocketFolders.Where(folder => Directory.Exists(Path.Combine(rootPath, folder))).ToArray();

            string coresPath = Path.Combine(rootPath, "Cores");
            string[] installedCoreNames = Directory.Exists(coresPath)
                ? Directory.EnumerateDirectories(coresPath).Select(Path.GetFileName).Where(name => name is not null).Cast<string>().OrderBy(name => name).ToArray()
                : [];
            DriveInfo? drive = DriveInfo.GetDrives()
                .Where(candidate => rootPath.StartsWith(candidate.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.RootDirectory.FullName.Length)
                .FirstOrDefault();

            return new PocketDrive(rootPath, directory.Name, drive?.DriveType ?? DriveType.Unknown,
                drive?.TotalSize ?? 0, drive?.AvailableFreeSpace ?? 0, foundFolders.Length,
                foundFolders, installedCoreNames.Length, installedCoreNames);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }
}
