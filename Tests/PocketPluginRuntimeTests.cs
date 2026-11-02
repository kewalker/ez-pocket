using System.Text;
using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class PocketPluginRuntimeTests
{
    [Fact]
    public async Task NativeRuntimeReadsWritesAndDeletesOnSelectedTarget()
    {
        var runtime = new PocketPluginRuntime();
        using var files = new RuntimeFiles(Encoding.UTF8.GetBytes("""
            (module
              (import "wasi_snapshot_preview1" "path_open" (func $open (param i32 i32 i32 i32 i32 i64 i64 i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "fd_read" (func $read (param i32 i32 i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "fd_write" (func $write (param i32 i32 i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "fd_close" (func $close (param i32) (result i32)))
              (import "wasi_snapshot_preview1" "path_unlink_file" (func $unlink (param i32 i32 i32) (result i32)))
              (import "extism:host/env" "alloc" (func $alloc (param i64) (result i64)))
              (import "extism:host/env" "store_u8" (func $store (param i64 i32)))
              (import "extism:host/env" "output_set" (func $output (param i64 i64)))
              (memory (export "memory") 1)
              (data (i32.const 0) "input.txt")
              (data (i32.const 32) "output.json")
              (data (i32.const 64) "new")
              (func $check (param $e i32) local.get $e if unreachable end)
              (func (export "start") (result i32) (local $fd i32) (local $p i64)
                i32.const 4 i32.const 0 i32.const 0 i32.const 9 i32.const 0 i64.const 2 i64.const 0 i32.const 0 i32.const 128 call $open call $check
                i32.const 128 i32.load local.set $fd
                i32.const 160 i32.const 4096 i32.store i32.const 164 i32.const 3 i32.store
                local.get $fd i32.const 160 i32.const 1 i32.const 168 call $read call $check
                i32.const 4096 i32.load8_u i32.const 111 i32.ne if unreachable end
                local.get $fd call $close call $check
                i32.const 4 i32.const 0 i32.const 32 i32.const 11 i32.const 1 i64.const 64 i64.const 0 i32.const 0 i32.const 128 call $open call $check
                i32.const 128 i32.load local.set $fd
                i32.const 160 i32.const 64 i32.store
                local.get $fd i32.const 160 i32.const 1 i32.const 168 call $write call $check
                local.get $fd call $close call $check
                i32.const 4 i32.const 0 i32.const 9 call $unlink call $check
                i64.const 6 call $alloc local.set $p
                local.get $p i32.const 34 call $store
                local.get $p i64.const 1 i64.add i32.const 69 call $store
                local.get $p i64.const 2 i64.add i32.const 120 call $store
                local.get $p i64.const 3 i64.add i32.const 105 call $store
                local.get $p i64.const 4 i64.add i32.const 116 call $store
                local.get $p i64.const 5 i64.add i32.const 34 call $store
                local.get $p i64.const 6 call $output i32.const 0)
            )
            """));
        File.WriteAllText(Path.Combine(files.Pocket, "input.txt"), "old");
        // Extism sorts mount keys: host is fd 3 and pocket is fd 4 in this fixture.
        await runtime.RunAsync(files.Wasm, files.Plugin, files.Pocket, files.Host,
            (_, _) => throw new Exception("Unexpected prompt"), _ => { }, _ => { }, CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(files.Pocket, "input.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(files.Pocket, "output.json")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(files.Host));
    }

    [Fact]
    public async Task NativeRuntimeHandlesPromptsLogsAndBrowserRequests()
    {
        using var files = new RuntimeFiles(BuildModule("""{"Choice":{"name":"menu","query":"Generate files?","choices":["Build","Exit"]}}""", "https://example.com/setup"));
        var log = new StringBuilder();
        var urls = new List<Uri>();
        int questions = 0;
        await new PocketPluginRuntime().RunAsync(files.Wasm, files.Plugin, files.Pocket, files.Host,
            (prompt, _) =>
            {
                questions++;
                Assert.Equal("menu", prompt.Name);
                Assert.Equal(new[] { "Build", "Exit" }, prompt.Choices);
                return Task.FromResult<string?>("Build");
            }, message => log.Append(message), urls.Add, CancellationToken.None);
        Assert.Equal(1, questions);
        Assert.Contains("Native plugin log", log.ToString());
        Assert.Equal("https://example.com/setup", Assert.Single(urls).AbsoluteUri);
    }

    [Fact]
    public async Task NativeRuntimeRejectsUnsafeBrowserSchemes()
    {
        using var files = new RuntimeFiles(BuildModule("\"Exit\"", "file:///C:/Windows/notepad.exe"));
        var urls = new List<Uri>();
        await new PocketPluginRuntime().RunAsync(files.Wasm, files.Plugin, files.Pocket, files.Host,
            (_, _) => throw new Exception("Unexpected prompt"), _ => { }, urls.Add, CancellationToken.None);
        Assert.Empty(urls);
    }

    [Fact]
    public async Task NativeRuntimeCanInterruptAnExecutingPlugin()
    {
        using var files = new RuntimeFiles(BuildModule("\"Exit\"", "https://example.com", loop: true));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PocketPluginRuntime().RunAsync(files.Wasm,
            files.Plugin, files.Pocket, files.Host, (_, _) => throw new Exception("Unexpected prompt"), _ => { }, _ => { }, cancellation.Token));
    }

    // A tiny, original WebAssembly fixture emitted in binary form keeps native ABI tests
    // offline and avoids checking in a third-party executable or requiring a compiler.
    private static byte[] BuildModule(string response, string url, bool loop = false)
    {
        var module = new List<byte> { 0, 97, 115, 109, 1, 0, 0, 0 };
        Section(1, [4, 0x60, 1, 0x7e, 1, 0x7e, 0x60, 2, 0x7e, 0x7f, 0, 0x60, 2, 0x7e, 0x7e, 0, 0x60, 0, 1, 0x7f]);
        var imports = new List<byte> { 5 };
        Import("extism:host/env", "alloc", 0);
        Import("extism:host/env", "store_u8", 1);
        Import("extism:host/env", "output_set", 2);
        Import("extism:host/user", "print_msg", 0);
        Import("extism:host/user", "open_url", 0);
        Section(2, imports);
        Section(3, [2, 3, 3]);
        var exports = new List<byte> { 2 };
        Name(exports, "start"); exports.AddRange([0, 5]);
        Name(exports, "handle_response"); exports.AddRange([0, 6]);
        Section(7, exports);
        var start = new List<byte> { 1, 1, 0x7e };
        if (loop) start.AddRange([0x03, 0x40, 0x0c, 0, 0x0b]);
        String(start, "Native plugin log\n"); start.AddRange([0x20, 0, 0x10, 3, 0x1a]);
        String(start, url); start.AddRange([0x20, 0, 0x10, 4, 0x1a]);
        Output(start, response);
        var reply = new List<byte> { 1, 1, 0x7e };
        Output(reply, "\"Exit\"");
        var code = new List<byte> { 2 };
        Unsigned(code, start.Count); code.AddRange(start);
        Unsigned(code, reply.Count); code.AddRange(reply);
        Section(10, code);
        return module.ToArray();

        void Section(byte id, List<byte> content) { module.Add(id); Unsigned(module, content.Count); module.AddRange(content); }
        void Import(string space, string name, byte type) { Name(imports, space); Name(imports, name); imports.AddRange([0, type]); }
    }

    private static void Output(List<byte> body, string value)
    {
        String(body, value);
        body.AddRange([0x20, 0, 0x42]); Signed(body, Encoding.UTF8.GetByteCount(value));
        body.AddRange([0x10, 2, 0x41, 0, 0x0b]);
    }

    private static void String(List<byte> body, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        body.Add(0x42); Signed(body, bytes.Length);
        body.AddRange([0x10, 0, 0x21, 0]);
        for (int i = 0; i < bytes.Length; i++)
        {
            body.AddRange([0x20, 0, 0x42]); Signed(body, i);
            body.AddRange([0x7c, 0x41]); Signed(body, bytes[i]);
            body.AddRange([0x10, 1]);
        }
    }

    private static void Name(List<byte> output, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); Unsigned(output, bytes.Length); output.AddRange(bytes); }
    private static void Unsigned(List<byte> output, int value)
    {
        do { byte part = (byte)(value & 127); value >>= 7; output.Add(value > 0 ? (byte)(part | 128) : part); } while (value > 0);
    }
    private static void Signed(List<byte> output, int value)
    {
        bool more;
        do { byte part = (byte)(value & 127); value >>= 7; more = value != 0 || (part & 64) != 0; output.Add(more ? (byte)(part | 128) : part); } while (more);
    }

    private sealed class RuntimeFiles : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "ez-pocket-runtime-tests", Guid.NewGuid().ToString("N"));
        public string Wasm => Path.Combine(root, "plugin.wasm");
        public string Pocket => Path.Combine(root, "pocket");
        public string Host => Path.Combine(root, "host");
        public PocketPlugin Plugin => new("fixture", "Fixture", "Original test module", "1", "test/fixture", []);
        public RuntimeFiles(byte[] module) { Directory.CreateDirectory(Pocket); Directory.CreateDirectory(Host); File.WriteAllBytes(Wasm, module); }
        public void Dispose() => Directory.Delete(root, true);
    }
}
