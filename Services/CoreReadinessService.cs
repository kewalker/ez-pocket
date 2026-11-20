using System.Globalization;
using System.Text.Json;
using EzPocket.Models;

namespace EzPocket.Services;

/// <summary>
/// Read-only check of whether each installed core has the fixed-name files it loads by itself
/// (BIOS and similar), following the openFPGA core.json/data.json definitions.
/// </summary>
public sealed class CoreReadinessService
{
    private const int MaxCores = 500;
    private const long MaxMetadataBytes = 1_000_000;
    private const uint CoreSpecificBit = 1u << 1;
    private const uint FilenameFromSlotZeroBit = 1u << 2;

    public CoreReadinessReport Inspect(PocketDrive pocket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pocket);
        string root = pocket.RootPath;
        string cores = Path.Combine(root, "Cores");
        var results = new List<CoreReadiness>();
        bool limited = false;
        if (!SafeDirectory(cores)) return new(pocket, results, false);

        foreach (string directory in Directory.EnumerateDirectories(cores))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= MaxCores) { limited = true; break; }
            string identifier = Path.GetFileName(directory);
            if (!SafeSegment(identifier) || !SafeDirectory(directory)) continue;
            results.Add(InspectCore(root, directory, identifier));
        }

        return new(pocket, results
            .OrderBy(core => core.State == CoreReadinessState.Ready)
            .ThenBy(core => core.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(core => core.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToArray(), limited);
    }

    private static CoreReadiness InspectCore(string root, string directory, string identifier)
    {
        string name = identifier;
        string version = "";
        var notes = new List<string>();
        string[] platformIds;
        try
        {
            using JsonDocument coreDocument = ReadJson(Path.Combine(directory, "core.json"));
            JsonElement core = coreDocument.RootElement.GetProperty("core");
            JsonElement metadata = core.GetProperty("metadata");
            version = Text(metadata, "version") ?? "";
            name = Text(metadata, "shortname") ?? identifier;
            platformIds = metadata.TryGetProperty("platform_ids", out var ids) && ids.ValueKind == JsonValueKind.Array
                ? ids.EnumerateArray().Select(value => value.GetString() ?? "").Take(4).ToArray()
                : [];
            if (platformIds.Any(value => !SafeSegment(value)))
                return Unreadable(identifier, name, version, "core.json lists a platform name that is not a valid folder name.");
            if (platformIds.Length > 0) name = PlatformName(root, platformIds[0]) ?? name;

            if (core.TryGetProperty("cores", out var bitstreams) && bitstreams.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement bitstream in bitstreams.EnumerateArray().Take(8))
                {
                    string? file = Text(bitstream, "filename");
                    if (string.IsNullOrEmpty(file) || !SafeRelative(file)) continue;
                    if (!File.Exists(Path.Combine(directory, file)))
                        notes.Add($"Core file {file} is missing from Cores/{identifier}. Reinstall this core from Manage cores.");
                }
            }
            foreach (string platform in platformIds.Take(1))
                if (!File.Exists(Path.Combine(root, "Platforms", platform + ".json")))
                    notes.Add($"Platforms/{platform}.json is missing. Reinstalling the core usually restores it.");
        }
        catch (Exception ex) when (IsReadError(ex))
        {
            return Unreadable(identifier, name, version, "core.json is missing or cannot be read. Reinstall this core from Manage cores.");
        }

        JsonElement[] slots;
        JsonDocument? dataDocument = null;
        try
        {
            dataDocument = ReadJson(Path.Combine(directory, "data.json"));
            slots = dataDocument.RootElement.GetProperty("data").GetProperty("data_slots").EnumerateArray().Take(32).ToArray();
        }
        catch (Exception ex) when (IsReadError(ex))
        {
            dataDocument?.Dispose();
            return Unreadable(identifier, name, version, "data.json is missing or cannot be read. Reinstall this core from Manage cores.");
        }

        using (dataDocument)
        {
            var files = new List<CoreRequiredFile>();
            foreach (JsonElement slot in slots)
            {
                if (slot.ValueKind != JsonValueKind.Object) continue;
                string? fileName = Text(slot, "filename");
                // Slots without a fixed filename are chosen by the player (games); saves and variant-only slots are not prerequisites.
                if (string.IsNullOrEmpty(fileName) || Flag(slot, "nonvolatile") || Flag(slot, "secondary")) continue;
                uint parameters;
                try { parameters = slot.TryGetProperty("parameters", out var bits) ? Number(bits) : 0; }
                catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException) { continue; }
                if ((parameters & FilenameFromSlotZeroBit) != 0) continue;
                if (!SafeRelative(fileName))
                {
                    notes.Add($"Slot {SlotName(slot)} declares an unusual filename that was not checked.");
                    continue;
                }

                int platformIndex = (int)((parameters >> 24) & 3);
                if (platformIndex >= platformIds.Length)
                {
                    notes.Add($"Slot {SlotName(slot)} refers to a platform this core does not list, so it was not checked.");
                    continue;
                }
                files.Add(CheckFile(root, identifier, platformIds[platformIndex], slot, fileName, (parameters & CoreSpecificBit) != 0));
            }

            CoreReadinessState state = files.Any(file => file.Required && file.State == RequiredFileState.Missing)
                ? CoreReadinessState.Missing
                : files.Any(file => file.NeedsAction) || notes.Count > 0 ? CoreReadinessState.Attention : CoreReadinessState.Ready;
            int required = files.Count(file => file.Required);
            int missing = files.Count(file => file.Required && file.NeedsAction);
            string summary = state switch
            {
                CoreReadinessState.Missing => $"{missing} of {required} required file{(required == 1 ? "" : "s")} not ready",
                CoreReadinessState.Attention when files.Any(file => file.NeedsAction) => "A file is present but will not load as placed",
                CoreReadinessState.Attention => "Core files need attention",
                _ => required == 0 ? "No fixed files required" : $"{required} required file{(required == 1 ? "" : "s")} present"
            };
            return new(identifier, name, version, state, summary, files, notes);
        }
    }

    private static CoreRequiredFile CheckFile(string root, string identifier, string platform, JsonElement slot, string fileName, bool coreSpecific)
    {
        bool required = Flag(slot, "required");
        string expectedFolder = coreSpecific ? identifier : "common";
        string otherFolder = coreSpecific ? "common" : identifier;
        string expectedRelative = $"Assets/{platform}/{expectedFolder}/{fileName.Replace('\\', '/')}";
        string expected = Path.Combine(root, "Assets", platform, expectedFolder, fileName);
        string slotName = SlotName(slot);

        if (!File.Exists(expected))
        {
            string other = Path.Combine(root, "Assets", platform, otherFolder, fileName);
            if (File.Exists(other))
                return new(slotName, fileName, required, expectedRelative, RequiredFileState.WrongFolder,
                    $"Found in Assets/{platform}/{otherFolder}. This core only looks in {expectedFolder}, so move or copy it to {expectedRelative}.");
            return required
                ? new(slotName, fileName, true, expectedRelative, RequiredFileState.Missing, $"Add {fileName} to {expectedRelative}. ez-pocket does not supply this file.")
                : new(slotName, fileName, false, expectedRelative, RequiredFileState.OptionalAbsent, $"Optional. The core runs without it; add it to {expectedRelative} if you need it.");
        }

        long size;
        try { size = new FileInfo(expected).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { size = -1; }
        uint exact = SizeField(slot, "size_exact");
        uint maximum = SizeField(slot, "size_maximum");
        if (size >= 0 && exact != 0 && size != exact)
            return new(slotName, fileName, required, expectedRelative, RequiredFileState.WrongSize,
                $"Expected exactly {exact:N0} bytes but found {size:N0}. The Pocket will not load it; it may be the wrong file or revision.");
        if (size >= 0 && maximum != 0 && size > maximum)
            return new(slotName, fileName, required, expectedRelative, RequiredFileState.WrongSize,
                $"Larger than the {maximum:N0} bytes this slot accepts ({size:N0}). The Pocket will not load it.");
        return new(slotName, fileName, required, expectedRelative, RequiredFileState.Present, "Present.");
    }

    private static CoreReadiness Unreadable(string identifier, string name, string version, string note) =>
        new(identifier, name, version, CoreReadinessState.Unreadable, "Core definition cannot be read", [], [note]);

    private static string? PlatformName(string root, string platform)
    {
        try
        {
            using JsonDocument document = ReadJson(Path.Combine(root, "Platforms", platform + ".json"));
            return Text(document.RootElement.GetProperty("platform"), "name");
        }
        catch (Exception ex) when (IsReadError(ex)) { return null; }
    }

    private static bool IsReadError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException
        or InvalidOperationException or KeyNotFoundException or InvalidDataException;

    private static JsonDocument ReadJson(string path)
    {
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > MaxMetadataBytes)
            throw new InvalidDataException("Unsupported metadata file.");
        return JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
    }

    private static bool SafeDirectory(string path) => Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static bool SafeSegment(string value) => value.Length is > 0 and <= 100 && value is not "." and not ".."
        && !value.Any(character => character < 32 || "<>:\"/\\|?*".Contains(character));

    private static bool SafeRelative(string value)
    {
        if (value.Length is 0 or > 255 || Path.IsPathRooted(value)) return false;
        return value.Replace('\\', '/').Split('/').All(SafeSegment);
    }

    private static string SlotName(JsonElement slot) => Text(slot, "name") is { Length: > 0 } name ? name : "(unnamed)";
    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Flag(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static uint SizeField(JsonElement slot, string property)
    {
        try { return slot.TryGetProperty(property, out var value) ? Number(value) : 0; }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException) { return 0; }
    }

    private static uint Number(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number) return value.GetUInt32();
        string text = value.GetString() ?? "";
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : uint.Parse(text, CultureInfo.InvariantCulture);
    }
}
