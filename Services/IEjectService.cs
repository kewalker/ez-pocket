using System.Runtime.InteropServices;
using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>Safely ejects removable Pocket storage after writes. Platform code stays behind this interface.</summary>
public interface IEjectService
{
    bool CanEject(PocketDrive pocket);
    Task<EjectResult> EjectAsync(PocketDrive pocket, CancellationToken cancellationToken = default);
}

public sealed class UnsupportedEjectService : IEjectService
{
    public bool CanEject(PocketDrive pocket) => false;
    public Task<EjectResult> EjectAsync(PocketDrive pocket, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EjectResult(false, "Ejecting is not supported on this platform. Use your system's eject control."));
}

/// <summary>
/// macOS and Linux: runs the system eject tool (diskutil or gio) for a mounted volume.
/// posix_spawnp is used directly because Process.Start is unavailable to Mac Catalyst apps.
/// </summary>
public sealed class UnixEjectService : IEjectService
{
    public bool CanEject(PocketDrive pocket)
    {
        string root = Path.TrimEndingDirectorySeparator(pocket.RootPath);
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            return root.StartsWith("/Volumes/", StringComparison.Ordinal) && root.Count(c => c == '/') == 2;
        if (OperatingSystem.IsLinux())
            return root.StartsWith("/media/", StringComparison.Ordinal) || root.StartsWith("/run/media/", StringComparison.Ordinal);
        return false;
    }

    public Task<EjectResult> EjectAsync(PocketDrive pocket, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (!CanEject(pocket)) return new EjectResult(false, "This target is a folder, not a removable volume. Use your system's eject control.");
        string root = Path.TrimEndingDirectorySeparator(pocket.RootPath);
        string[] arguments = OperatingSystem.IsLinux() ? ["gio", "mount", "-e", root] : ["/usr/sbin/diskutil", "eject", root];
        int exitCode = Run(arguments);
        if (exitCode == 0) return new EjectResult(true, $"{pocket.Name} was ejected. It is safe to remove the card.");
        return exitCode == -1
            ? new EjectResult(false, "The system eject tool could not be started. Eject the card from your file manager.")
            : new EjectResult(false, $"{pocket.Name} could not be ejected. Close any window or app using the card, then try again.");
    }, cancellationToken);

    private static int Run(string[] arguments)
    {
        IntPtr argv = IntPtr.Zero, envp = IntPtr.Zero;
        var allocated = new List<IntPtr>();
        try
        {
            string[] environment = System.Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .Select(entry => $"{entry.Key}={entry.Value}").ToArray();
            argv = ToNullTerminatedArray(arguments, allocated);
            envp = ToNullTerminatedArray(environment, allocated);
            if (posix_spawnp(out int pid, arguments[0], IntPtr.Zero, IntPtr.Zero, argv, envp) != 0) return -1;
            int status;
            while (waitpid(pid, out status, 0) == -1 && Marshal.GetLastWin32Error() == 4 /* EINTR */) { }
            return (status & 0x7F) == 0 ? (status >> 8) & 0xFF : 255;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return -1; }
        finally
        {
            foreach (IntPtr pointer in allocated) Marshal.FreeCoTaskMem(pointer);
            if (argv != IntPtr.Zero) Marshal.FreeHGlobal(argv);
            if (envp != IntPtr.Zero) Marshal.FreeHGlobal(envp);
        }
    }

    private static IntPtr ToNullTerminatedArray(string[] values, List<IntPtr> allocated)
    {
        IntPtr array = Marshal.AllocHGlobal(IntPtr.Size * (values.Length + 1));
        for (int i = 0; i < values.Length; i++)
        {
            IntPtr value = Marshal.StringToCoTaskMemUTF8(values[i]);
            allocated.Add(value);
            Marshal.WriteIntPtr(array, i * IntPtr.Size, value);
        }
        Marshal.WriteIntPtr(array, values.Length * IntPtr.Size, IntPtr.Zero);
        return array;
    }

    [DllImport("libc")]
    private static extern int posix_spawnp(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string file, IntPtr fileActions, IntPtr attributes, IntPtr argv, IntPtr envp);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);
}
