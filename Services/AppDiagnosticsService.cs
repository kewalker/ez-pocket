using System.IO.Compression;
using System.Text.Json;

namespace EzPocket.Services;

/// <summary>Writes minimal, local-only diagnostic events without recording target paths or exception messages.</summary>
public interface IAppDiagnostics
{
    void Info(string eventName, IReadOnlyDictionary<string, string?>? properties = null);
    void Warning(string eventName, IReadOnlyDictionary<string, string?>? properties = null);
    void Error(string eventName, Exception? exception = null, IReadOnlyDictionary<string, string?>? properties = null);
    Task<string> CreateBundleAsync(CancellationToken cancellationToken = default);
}

public sealed class AppDiagnosticsService : IAppDiagnostics
{
    private const int RetainedLogFiles = 7;
    private readonly string logDirectory;
    private readonly string exportDirectory;
    private readonly object writeLock = new();

    public AppDiagnosticsService(string? appDataDirectory = null, string? exportDirectory = null)
    {
        string appData = appDataDirectory ?? FileSystem.AppDataDirectory;
        logDirectory = Path.Combine(appData, "EzPocket", "diagnostics");
        this.exportDirectory = exportDirectory ?? FileSystem.CacheDirectory;
    }

    public void Info(string eventName, IReadOnlyDictionary<string, string?>? properties = null) => Write("Information", eventName, properties, null);

    public void Warning(string eventName, IReadOnlyDictionary<string, string?>? properties = null) => Write("Warning", eventName, properties, null);

    public void Error(string eventName, Exception? exception = null, IReadOnlyDictionary<string, string?>? properties = null) =>
        Write("Error", eventName, properties, exception?.GetType().Name);

    public async Task<string> CreateBundleAsync(CancellationToken cancellationToken = default)
    {
        Info("DiagnosticsBundleRequested");
        string destination = Path.Combine(exportDirectory, $"ez-pocket-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");
        Directory.CreateDirectory(exportDirectory);
        string[] logs;
        lock (writeLock)
        {
            Directory.CreateDirectory(logDirectory);
            logs = Directory.EnumerateFiles(logDirectory, "*.jsonl", SearchOption.TopDirectoryOnly).ToArray();
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using ZipArchive archive = ZipFile.Open(destination, ZipArchiveMode.Create);
            foreach (string log in logs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                archive.CreateEntryFromFile(log, Path.GetFileName(log), CompressionLevel.Optimal);
            }

            ZipArchiveEntry readme = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
            using StreamWriter writer = new(readme.Open());
            writer.Write("ez-pocket diagnostics\nLocal operation events only. Target paths and exception messages are intentionally excluded.");
        }, cancellationToken);
        Info("DiagnosticsBundleCreated", new Dictionary<string, string?> { ["LogFileCount"] = logs.Length.ToString() });
        return destination;
    }

    public static string TargetId(string path)
    {
        byte[] data = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()));
        return Convert.ToHexString(data)[..12];
    }

    private void Write(string level, string eventName, IReadOnlyDictionary<string, string?>? properties, string? exceptionType)
    {
        try
        {
            var entry = new DiagnosticLogEntry(DateTimeOffset.UtcNow, level, eventName, properties, exceptionType);
            string line = JsonSerializer.Serialize(entry);
            lock (writeLock)
            {
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(Path.Combine(logDirectory, $"ez-pocket-{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl"), line + Environment.NewLine);
                PruneLogs();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void PruneLogs()
    {
        foreach (FileInfo log in new DirectoryInfo(logDirectory).GetFiles("*.jsonl")
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(RetainedLogFiles))
            log.Delete();
    }

    private sealed record DiagnosticLogEntry(
        DateTimeOffset TimestampUtc,
        string Level,
        string EventName,
        IReadOnlyDictionary<string, string?>? Properties,
        string? ExceptionType);
}

public sealed class NullAppDiagnostics : IAppDiagnostics
{
    public static readonly NullAppDiagnostics Instance = new();
    private NullAppDiagnostics() { }
    public void Info(string eventName, IReadOnlyDictionary<string, string?>? properties = null) { }
    public void Warning(string eventName, IReadOnlyDictionary<string, string?>? properties = null) { }
    public void Error(string eventName, Exception? exception = null, IReadOnlyDictionary<string, string?>? properties = null) { }
    public Task<string> CreateBundleAsync(CancellationToken cancellationToken = default) => Task.FromException<string>(new NotSupportedException("Diagnostics are unavailable."));
}
