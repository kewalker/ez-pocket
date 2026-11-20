using System.ComponentModel;
using System.Runtime.InteropServices;
using EzPocket.Models;
using EzPocket.Services;
using Microsoft.Win32.SafeHandles;

namespace EzPocket;

/// <summary>
/// Flushes and dismounts a removable volume (lock → dismount → allow removal → eject media).
/// Locking fails while another program holds files open, which is exactly when removal is unsafe.
/// </summary>
public sealed class WindowsEjectService : IEjectService
{
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    private const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;
    private const uint FsctlLockVolume = 0x00090018, FsctlDismountVolume = 0x00090020;
    private const uint IoctlStorageMediaRemoval = 0x002D4804, IoctlStorageEjectMedia = 0x002D4808;

    public bool CanEject(PocketDrive pocket)
    {
        string? letter = DriveLetter(pocket.RootPath);
        if (letter is null) return false;
        try { return new DriveInfo(letter).DriveType == DriveType.Removable; }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return false; }
    }

    public Task<EjectResult> EjectAsync(PocketDrive pocket, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        string? letter = DriveLetter(pocket.RootPath);
        if (letter is null || !CanEject(pocket))
            return new EjectResult(false, "This target is a folder or fixed drive, not a removable card. Use Windows' eject control.");

        using SafeFileHandle volume = CreateFile($@"\\.\{letter}", GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (volume.IsInvalid)
            return new EjectResult(false, $"Windows did not allow ez-pocket to eject {letter} ({new Win32Exception(Marshal.GetLastWin32Error()).Message}). Use Safely Remove Hardware instead.");

        bool locked = false;
        for (int attempt = 0; attempt < 10 && !locked; attempt++)
        {
            locked = DeviceIoControl(volume, FsctlLockVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            if (!locked) await Task.Delay(300, cancellationToken);
        }
        if (!locked)
            return new EjectResult(false, $"{pocket.Name} is in use. Close File Explorer windows or apps using the card, then try again.");

        if (!DeviceIoControl(volume, FsctlDismountVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            return new EjectResult(false, $"{pocket.Name} could not be dismounted. Use Safely Remove Hardware instead.");

        // The volume is flushed and dismounted at this point, so removal is safe even if the reader cannot eject media.
        IntPtr allowRemoval = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(allowRemoval, 0);
            DeviceIoControl(volume, IoctlStorageMediaRemoval, allowRemoval, 1, IntPtr.Zero, 0, out _, IntPtr.Zero);
        }
        finally { Marshal.FreeHGlobal(allowRemoval); }
        DeviceIoControl(volume, IoctlStorageEjectMedia, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        return new EjectResult(true, $"{pocket.Name} was ejected. It is safe to remove the card.");
    }, cancellationToken);

    private static string? DriveLetter(string root)
    {
        string full;
        try { full = Path.GetFullPath(root); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        string? pathRoot = Path.GetPathRoot(full);
        if (pathRoot is not { Length: 3 } || pathRoot[1] != ':' || !char.IsAsciiLetter(pathRoot[0])) return null;
        // Only offer ejecting when the selected target is the card itself, not a folder on it.
        return string.Equals(Path.TrimEndingDirectorySeparator(full), Path.TrimEndingDirectorySeparator(pathRoot), StringComparison.OrdinalIgnoreCase)
            ? pathRoot[..2].ToUpperInvariant() : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr inBuffer, uint inSize, IntPtr outBuffer, uint outSize, out uint bytesReturned, IntPtr overlapped);
}
