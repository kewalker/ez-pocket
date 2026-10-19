using System.Globalization;
using System.Text.Json;
using Extism.Sdk;
using Extism.Sdk.Native;
using EzPocket.Models;

namespace EzPocket.Services;

public interface IPocketPluginRuntime
{
    Task RunAsync(string wasmPath, PocketPlugin descriptor, string pocketFolder, string hostFolder,
        Func<PocketPluginPrompt, CancellationToken, Task<string?>> prompt,
        Action<string> log, Action<Uri> offerUrl, CancellationToken cancellationToken);
}

/// <summary>Implements pocket-plugin's Extism ABI. Only local working folders are mounted.</summary>
public sealed class PocketPluginRuntime : IPocketPluginRuntime
{
    public async Task RunAsync(string wasmPath, PocketPlugin descriptor, string pocketFolder, string hostFolder,
        Func<PocketPluginPrompt, CancellationToken, Task<string?>> prompt,
        Action<string> log, Action<Uri> offerUrl, CancellationToken cancellationToken)
    {
        var manifest = new Manifest(new PathWasmSource(wasmPath))
        {
            Timeout = TimeSpan.FromMinutes(5),
            MemoryOptions = new MemoryOptions { MaxPages = 16384, MaxHttpResponseBytes = 128 * 1024 * 1024, MaxVarBytes = 16 * 1024 * 1024 }
        };
        manifest.AllowedPaths.Add(Path.GetFullPath(pocketFolder), "pocket");
        manifest.AllowedPaths.Add(Path.GetFullPath(hostFolder), "host");
        foreach (string host in descriptor.AllowedHosts) manifest.AllowedHosts.Add(host);
        manifest.Config["locale"] = CultureInfo.CurrentUICulture.Name;

        using var print = new HostFunction("print_msg", [ExtismValType.PTR], [ExtismValType.PTR], null,
            (CurrentPlugin current, Span<ExtismVal> inputs, Span<ExtismVal> outputs) =>
            {
                // Never allow UI callback exceptions to cross the native boundary.
                try { log(current.ReadString(new nint(inputs[0].v.ptr))); } catch { }
                outputs[0].v.ptr = current.WriteString(string.Empty);
            });
        using var open = new HostFunction("open_url", [ExtismValType.PTR], [ExtismValType.PTR], null,
            (CurrentPlugin current, Span<ExtismVal> inputs, Span<ExtismVal> outputs) =>
            {
                try
                {
                    string url = current.ReadString(new nint(inputs[0].v.ptr));
                    if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo))
                        offerUrl(uri);
                    else log("Plugin requested an unsupported browser link.\n");
                }
                catch { }
                outputs[0].v.ptr = current.WriteString(string.Empty);
            });

        using var plugin = await Task.Run(() => new Plugin(manifest, [print, open], withWasi: true), cancellationToken);
        if (!plugin.FunctionExists("start")) throw new InvalidDataException("This plugin does not export start.");
        string output = await CallAsync("start", string.Empty);
        for (int step = 0; step < 1000; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PocketPluginPrompt? question = ParseMessage(output);
            if (question is null) return;
            if (!plugin.FunctionExists("handle_response")) throw new InvalidDataException("This plugin cannot handle its prompts.");
            string? answer = await prompt(question, cancellationToken);
            if (answer is null)
            {
                // Give cooperative plugins a chance to clean up; discard the entire run regardless.
                await CallAsync("handle_response", "\"Kill\"");
                throw new OperationCanceledException("Plugin cancelled. Its staged changes were discarded.");
            }
            if (question.Choices is not null && !question.Choices.Contains(answer, StringComparer.Ordinal))
                throw new InvalidDataException("The answer is not one of the plugin's choices.");
            output = await CallAsync("handle_response", JsonSerializer.Serialize(new { Answer = new { name = question.Name, value = answer } }));
        }
        throw new InvalidDataException("The plugin exceeded the interaction limit.");

        async Task<string> CallAsync(string function, string input)
        {
            try { return await Task.Run(() => plugin.Call(function, input, cancellationToken), cancellationToken); }
            catch (ExtismException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        }
    }

    public static PocketPluginPrompt? ParseMessage(string output)
    {
        if (output.Length > 64 * 1024) throw new InvalidDataException("The plugin returned an oversized prompt.");
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        if (root.ValueKind == JsonValueKind.String && root.GetString() == "Exit") return null;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1)
            throw new InvalidDataException("Unsupported Pocket Plugin response.");
        JsonProperty variant = root.EnumerateObject().Single();
        if (variant.Name is not ("Choice" or "Text") || variant.Value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Unsupported Pocket Plugin response.");
        string name = variant.Value.GetProperty("name").GetString() ?? throw new InvalidDataException("Missing prompt name.");
        string query = variant.Value.GetProperty("query").GetString() ?? throw new InvalidDataException("Missing prompt text.");
        string[]? choices = variant.Name == "Choice" ? variant.Value.GetProperty("choices").Deserialize<string[]>() : null;
        if (variant.Name == "Choice" && (choices is null || choices.Length is 0 or > 100 || choices.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidDataException("Unsupported plugin choices.");
        return new PocketPluginPrompt(name, query, choices is null ? null : Array.AsReadOnly(choices));
    }
}
