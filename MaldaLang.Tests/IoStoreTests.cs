// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.Text;
using MaldaLang.BuiltIns;
using MaldaLang.Runtime.Actors;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// <c>io.store*</c> and the browser file calls (J7): the same store semantics on the interpreter,
/// C# transpile and JavaScript; persistence across runs on the host (<c>./.malda/store</c>) and
/// across reloads on JavaScript (a file-backed fake IndexedDB in Node); the localStorage
/// fallback; and open / save / download / drop against fake browser objects.
/// </summary>
[Collection("Sequential")]
public class IoStoreTests : IDisposable
{
    private readonly string _storeDirectory;
    private readonly string? _previousStoreDirectory;

    public IoStoreTests()
    {
        _storeDirectory = Path.Combine(Path.GetTempPath(), "malda_store_tests", Guid.NewGuid().ToString("N"));
        _previousStoreDirectory = Environment.GetEnvironmentVariable("MALDA_STORE_DIR");
        Environment.SetEnvironmentVariable("MALDA_STORE_DIR", _storeDirectory);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MALDA_STORE_DIR", _previousStoreDirectory);
        if (Directory.Exists(_storeDirectory))
            Directory.Delete(_storeDirectory, recursive: true);
    }

    /// <summary>Starts from an empty store, so a leftover from another run cannot change the output.</summary>
    private const string Program = """
        type Shape = Circle(r) | Square(side);
        for (var old in io.storeKeys()) {
            io.storeRemove(old);
        }
        io.print(io.storeGet("missing"));
        io.print(io.storeGet("missing", 7));
        io.storeSet("count", 41);
        io.storeSet("count", io.storeGet("count") + 1);
        io.print(io.storeGet("count"));
        io.storeSet("doc", { "title": "Notes", "tags": ["a", "b"], "shape": Circle(3), "meta": dict { "k": 1.5 }, "none": null });
        var doc = io.storeGet("doc");
        io.print(doc["title"]);
        io.print(doc["tags"]);
        io.print(doc["shape"]);
        io.print(doc["meta"]["k"]);
        io.print(typeOf(doc["meta"]));
        io.print(doc["none"]);
        doc["tags"].append("c");
        io.print(io.storeGet("doc")["tags"]);
        var samples = buf.float64(3);
        samples[2] = 2.5;
        io.storeSet("samples", samples);
        io.print(io.storeGet("samples"));
        io.print(buf.kind(io.storeGet("samples")));
        io.storeSet("bytes", buf.fromUtf8("ciao"));
        io.print(buf.toUtf8(io.storeGet("bytes")));
        io.storeSet("Key", "upper");
        io.storeSet("key", "lower");
        io.print(io.storeGet("Key") + " " + io.storeGet("key"));
        io.print(io.storeKeys());
        io.print(io.storeRemove("bytes"));
        io.print(io.storeRemove("bytes"));
        io.print(io.storeKeys().length);
        try { io.storeSet("f", function (x) { return x; }); } catch (e) { io.print(e); }
        try { io.storeSet("", 1); } catch (e) { io.print(e); }
        try { io.storeGet(3); } catch (e) { io.print(e); }
        """;

    private const string ExpectedOutput = """
        null
        7
        42
        Notes
        [a, b]
        Circle(3)
        1.5
        dict
        null
        [a, b]
        [0, 0, 2.5]
        float64
        ciao
        upper lower
        [Key, bytes, count, doc, key, samples]
        true
        false
        5
        A function cannot be stored: io.storeSet() takes null, bools, numbers, strings, arrays, dicts, variants and buffers.
        io.storeSet() key must be a non-empty string of at most 256 characters.
        io.storeGet() key must be a non-empty string of at most 256 characters.
        """;

    /// <summary>
    /// Not on C#: converting a list that contains itself to a RuntimeValue overflows the stack
    /// there before io.storeSet sees it.
    /// </summary>
    private const string SelfContaining = """
        var loop = [1];
        loop.append(loop);
        try { io.storeSet("loop", loop); } catch (e) { io.print(e); }
        """;

    [Fact]
    public async Task Interpreter_PrintsTheExpectedOutput()
    {
        var output = await TestBase.CaptureInterpretAsync(Program);

        Assert.Equal(InterpretTranspilePair.Normalize(ExpectedOutput), InterpretTranspilePair.Normalize(output));
    }

    [Fact]
    public async Task AValueThatContainsItself_IsAnError()
    {
        Assert.Equal("io.storeSet() cannot store a value that contains itself.",
            InterpretTranspilePair.Normalize(await TestBase.CaptureInterpretAsync(SelfContaining)));
        InterpretJsPair.AssertSameFromSource(SelfContaining, "store-cycle-js");
    }

    [Fact]
    public void CSharpTranspile_MatchesTheInterpreter()
    {
        InterpretTranspilePair.AssertSameFromSource(Program, "store-csharp");
    }

    [Fact]
    public void JavaScript_MatchesTheInterpreter()
    {
        InterpretJsPair.AssertSameFromSource(Program, "store-js");
    }

    [Fact]
    public async Task Host_StoreSurvivesTheRun_InOneFilePerKey()
    {
        const string counter = """
            var runs = io.storeGet("runs", 0) + 1;
            io.storeSet("runs", runs);
            io.print(runs);
            """;

        Assert.Equal("1", InterpretTranspilePair.Normalize(await TestBase.CaptureInterpretAsync(counter)));
        Assert.Equal("2", InterpretTranspilePair.Normalize(await TestBase.CaptureInterpretAsync(counter)));

        var file = Assert.Single(Directory.GetFiles(_storeDirectory, "*.json"));
        Assert.Equal("{\"key\":\"runs\",\"value\":2}", File.ReadAllText(file));
        Assert.Empty(Directory.GetFiles(_storeDirectory, "*.tmp"));
    }

    [Fact]
    public async Task Host_BrowserFileCalls_ExplainThatTheyNeedTheBrowser()
    {
        var output = await TestBase.CaptureInterpretAsync("""
            try { io.openFile(); } catch (e) { io.print(e); }
            try { io.onFileDrop("#app", null); } catch (e) { io.print(e); }
            """);

        Assert.Equal(
            IoStore.BrowserOnlyMessage("openFile") + "\n" + IoStore.BrowserOnlyMessage("onFileDrop"),
            InterpretTranspilePair.Normalize(output));
        Assert.Contains("runs only on the JavaScript backend", IoStore.BrowserOnlyMessage("saveFile"));
    }

    [Fact]
    public void Rules_AWorkerActorCannotUseTheStore()
    {
        var parser = new Parser.Parser(new Lexer("""
            function remember(x) { io.storeSet("x", x); }
            @worker()
            actor W {
                on go(x) { remember(x); }
            }
            """).Tokenize());
        var violations = WorkerActorRules.Check(parser.Parse());

        Assert.Equal(
            "Worker actor 'W' calls io.storeSet(). The store and the browser files belong to the main thread: send the value to an actor on the main thread instead.",
            Assert.Single(violations).Message);
    }

    [Fact]
    public void JavaScript_LoadsTheStoreBeforeMain_AndAwaitsTheFileDialogs()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            io.storeSet("a", 1);
            function pick() {
                var file = io.openFile({ "as": "bytes" });
                var saved = io.saveFile("x.txt", "hi");
                io.downloadFile("y.txt", "yo");
            }
            """);

        Assert.Contains("async function main() {\n        await mlRuntime.io.storeReady();", js.Replace("\r", string.Empty));
        Assert.Contains("let file = await mlRuntime.io.openFile(", js);
        Assert.Contains("let saved = await mlRuntime.io.saveFile(", js);
        Assert.Contains("mlRuntime.io.downloadFile(\"y.txt\", \"yo\");", js);
        Assert.DoesNotContain("await mlRuntime.io.downloadFile", js);

        var plain = new Compiler.Compiler().TranspileToJavaScriptFromSource("io.print(1);");
        Assert.DoesNotContain("storeReady", plain);
    }

    [Fact]
    public void JavaScript_CapabilityChecker_AllowsTheNewIoCalls()
    {
        var diagnostics = Compiler.JsCapabilityChecker.Check(Parse("""
            io.storeSet("a", 1);
            var f = io.openFile();
            io.readFile("x.txt");
            """));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("io.readFile", diagnostic.Message);
    }

    /// <summary>The draft written by one page load is there after a reload (a second Node process).</summary>
    [Fact]
    public void JavaScript_IndexedDb_KeepsTheStoreAcrossReloads()
    {
        const string counter = """
            var visits = io.storeGet("visits", 0) + 1;
            io.storeSet("visits", visits);
            io.storeSet("draft", { "text": "line " + string(visits), "bytes": buf.fromUtf8("é") });
            io.print(visits);
            io.print(io.storeGet("draft")["text"]);
            io.print(io.storeKeys());
            """;

        var directory = NewDirectory();
        try
        {
            var dbPath = Path.Combine(directory, "fake-idb.json");
            Assert.Equal("1\nline 1\n[draft, visits]", RunProgram(counter, directory, FakeIndexedDb(dbPath)));
            Assert.Equal("2\nline 2\n[draft, visits]", RunProgram(counter, directory, FakeIndexedDb(dbPath)));
            var saved = File.ReadAllText(dbPath);
            Assert.Contains("\\\"visits\\\"", saved);
            Assert.Contains("\\\"$t\\\":\\\"buffer\\\"", saved);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JavaScript_WithoutIndexedDb_UsesLocalStorage()
    {
        var directory = NewDirectory();
        try
        {
            var output = RunProgram("""
                io.storeSet("a", [1, 2]);
                io.print(io.storeGet("a"));
                """, directory, """
                const items = new Map([["malda.store.old", JSON.stringify({ key: "old", value: "kept" })], ["other", "x"]]);
                globalThis.localStorage = {
                  get length() { return items.size; },
                  key(i) { return Array.from(items.keys())[i]; },
                  getItem(k) { return items.has(k) ? items.get(k) : null; },
                  setItem(k, v) { items.set(k, String(v)); },
                  removeItem(k) { items.delete(k); }
                };
                globalThis.__after = () => process.stdout.write(globalThis.mlRuntime.io.storeGet("old") + " " + items.get("malda.store.a") + "\n");
                """);

            Assert.Equal("[1, 2]\nkept {\"key\":\"a\",\"value\":[1,2]}", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JavaScript_FileCalls_OpenSaveDownloadAndDrop()
    {
        var directory = NewDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "files.js"), FilesScript);
            var output = RunNode(Path.Combine(directory, "files.js"), directory,
                PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js"));
            Assert.Equal("ok", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NotesExample_CompilesToJavaScript()
    {
        var path = PlanningPaths.ResolveRepoFile("Examples", "Web", "js", "notes_editor.malda");
        var js = new Compiler.Compiler().TranspileToJavaScript(path);

        Assert.Contains("await mlRuntime.io.storeReady();", js);
        Assert.Contains("await mlRuntime.io.openFile(", js);
        Assert.Contains("await mlRuntime.io.saveFile(", js);
        Assert.Contains("mlRuntime.io.onFileDrop(", js);
    }

    /// <summary>Fake browser objects for every route of the file calls.</summary>
    private const string FilesScript = """
        require(process.argv[2]);
        const io = globalThis.mlRuntime.io;
        const check = (ok, what) => { if (!ok) throw new Error(what); };
        function fakeFile(name, text) {
          const bytes = new TextEncoder().encode(text);
          return { name, size: bytes.length, type: "text/plain", lastModified: 5, text: async () => text, arrayBuffer: async () => bytes.buffer };
        }
        const appended = [];
        let inputHook = null;
        const body = { appendChild(el) { appended.push(el); el.parentNode = body; return el; }, removeChild(el) { el.parentNode = null; } };
        globalThis.document = {
          body,
          documentElement: body,
          createElement(tag) {
            const listeners = {};
            const el = { tag, style: {}, listeners, addEventListener(type, fn) { listeners[type] = fn; } };
            el.click = () => { if (tag === "input" && inputHook) inputHook(el); if (tag === "a") el.clicked = true; };
            return el;
          },
          querySelector(sel) { return sel === "#drop" ? dropTarget : null; }
        };
        const dropListeners = {};
        const classes = new Set();
        const dropTarget = { addEventListener(type, fn) { dropListeners[type] = fn; }, classList: { add: (c) => classes.add(c), remove: (c) => classes.delete(c) } };
        globalThis.window = {};
        const revoked = [];
        URL.createObjectURL = (blob) => { URL.lastBlob = blob; return "blob:fake"; };
        URL.revokeObjectURL = (u) => revoked.push(u);

        (async () => {
          // <input type=file> route: picked, then cancelled.
          inputHook = (input) => { check(input.accept === ".txt,.md" && !input.multiple, "input options"); input.files = [fakeFile("a.txt", "hello")]; input.listeners.change(); };
          const opened = await io.openFile({ accept: ".txt,.md" });
          check(opened.name === "a.txt" && opened.text === "hello" && opened.size === 5 && opened.handle === null, "opened text");
          check(appended[0].tag === "input" && appended[0].parentNode === null, "input removed");
          inputHook = (input) => { input.files = [fakeFile("b.bin", "AB")]; input.listeners.change(); };
          const bytes = await io.openFile({ as: "bytes" });
          check(bytes.bytes instanceof Uint8Array && bytes.bytes[0] === 65 && bytes.text === undefined, "opened bytes");
          inputHook = (input) => input.listeners.cancel();
          check(await io.openFile() === null, "cancel gives null");
          inputHook = (input) => { input.files = [fakeFile("1.txt", "1"), fakeFile("2.txt", "2")]; input.listeners.change(); };
          const many = await io.openFile({ multiple: true });
          check(many.length === 2 && many[1].text === "2", "multiple");

          // File System Access route: open a handle, write it back, then save as.
          const written = [];
          const handle = (name) => ({ name, getFile: async () => fakeFile(name, "from disk"),
            createWritable: async () => ({ write: async (blob) => written.push([name, await blob.text()]), close: async () => written.push([name, "closed"]) }) });
          let pickerArgs = null;
          window.showOpenFilePicker = async (args) => { pickerArgs = args; return [handle("doc.md")]; };
          const doc = await io.openFile({ accept: [".md", "text/plain"] });
          check(doc.name === "doc.md" && doc.text === "from disk" && doc.handle.name === "doc.md", "picked with a handle");
          check(JSON.stringify(pickerArgs.types[0].accept) === JSON.stringify({ "application/octet-stream": [".md"], "text/plain": [] }), "picker types");
          const back = await io.saveFile(doc, "edited");
          check(back.method === "overwrite" && written[0][1] === "edited" && written[1][1] === "closed", "written back");
          window.showSaveFilePicker = async (args) => { check(args.suggestedName === "new.txt", "suggested name"); return handle("chosen.txt"); };
          const saveAs = await io.saveFile("new.txt", new Uint8Array([104, 105]));
          check(saveAs.method === "saveAs" && saveAs.name === "chosen.txt" && written[2][1] === "hi", "save as bytes");
          window.showSaveFilePicker = async () => { const e = new Error("cancel"); e.name = "AbortError"; throw e; };
          check(await io.saveFile("new.txt", "x") === null, "save as cancelled");
          window.showOpenFilePicker = async () => { const e = new Error("cancel"); e.name = "AbortError"; throw e; };
          check(await io.openFile() === null, "picker cancelled");

          // Download route.
          delete window.showSaveFilePicker;
          const downloaded = await io.saveFile("plain.txt", "text");
          const link = appended[appended.length - 1];
          check(downloaded.method === "download" && link.tag === "a" && link.download === "plain.txt" && link.clicked, "download link");
          check(await URL.lastBlob.text() === "text", "download blob");
          check(io.downloadFile("raw.bin", new Uint8Array([1])) === null && URL.lastBlob.type === "application/octet-stream", "downloadFile");
          let threw = false;
          try { io.downloadFile("x", 5); } catch (e) { threw = /data must be a string or a buffer/.test(e.message); }
          check(threw, "download data type");

          // Drop.
          const dropped = [];
          io.onFileDrop("#drop", (files) => dropped.push(files), { hoverClass: "over" });
          let prevented = 0;
          const dataTransfer = { files: [fakeFile("d.txt", "dropped")], dropEffect: "" };
          dropListeners.dragover({ preventDefault() { prevented++; }, dataTransfer });
          check(classes.has("over") && dataTransfer.dropEffect === "copy", "hover");
          dropListeners.drop({ preventDefault() { prevented++; }, dataTransfer });
          await new Promise((r) => setTimeout(r, 10));
          check(prevented === 2 && !classes.has("over") && dropped[0][0].text === "dropped", "dropped files");
          threw = false;
          try { io.onFileDrop("#missing", () => {}); } catch (e) { threw = /could not find the drop target #missing/.test(e.message); }
          check(threw, "missing target");
          setTimeout(() => { check(revoked.length >= 1, "blob URLs revoked"); process.stdout.write("ok\n"); }, 1100);
        })().catch((e) => { process.stderr.write(e.stack + "\n"); process.exit(1); });
        """;

    /// <summary>
    /// A file-backed IndexedDB with the calls the runtime uses: open with an upgrade, readonly
    /// getAll / getAllKeys, readwrite put / delete, transaction complete. Writes reach the file
    /// when the transaction completes.
    /// </summary>
    private static string FakeIndexedDb(string path) => $$"""
        const fs = require("fs");
        const dbPath = {{System.Text.Json.JsonSerializer.Serialize(path)}};
        const load = () => fs.existsSync(dbPath) ? JSON.parse(fs.readFileSync(dbPath, "utf8")) : null;
        const later = (fn) => setTimeout(fn, 1);
        function request(result) {
          const r = { result };
          later(() => r.onsuccess && r.onsuccess());
          return r;
        }
        globalThis.indexedDB = {
          open(name, version) {
            const saved = load();
            const stores = saved || {};
            const db = {
              objectStoreNames: { contains: (n) => Object.prototype.hasOwnProperty.call(stores, n) },
              createObjectStore(n) { stores[n] = {}; },
              transaction(storeName, mode) {
                const tx = {};
                const data = stores[storeName];
                let pendingWrites = 0;
                tx.objectStore = () => ({
                  getAllKeys: () => request(Object.keys(data)),
                  getAll: () => request(Object.values(data)),
                  put(value, key) { data[key] = value; pendingWrites++; },
                  delete(key) { delete data[key]; pendingWrites++; }
                });
                later(() => {
                  if (mode === "readwrite") fs.writeFileSync(dbPath, JSON.stringify(stores));
                  tx.oncomplete && tx.oncomplete();
                });
                return tx;
              }
            };
            const r = { result: db };
            later(() => {
              if (!saved) { r.onupgradeneeded && r.onupgradeneeded(); fs.writeFileSync(dbPath, JSON.stringify(stores)); }
              r.onsuccess && r.onsuccess();
            });
            return r;
          }
        };
        """;

    /// <summary>Compiles a program and runs it in Node after <paramref name="setup"/>, flushing the store at the end.</summary>
    private static string RunProgram(string source, string directory, string setup)
    {
        File.WriteAllText(Path.Combine(directory, "program.js"), new Compiler.Compiler().TranspileToJavaScriptFromSource(source));
        File.WriteAllText(Path.Combine(directory, "run.js"), setup + """

            require(process.argv[2]);
            (async () => {
              const app = require("./program.js");
              await app.main();
              await globalThis.mlRuntime.io.storeFlush();
              if (globalThis.__after) globalThis.__after();
            })().catch((e) => { process.stderr.write(e.stack + "\n"); process.exit(1); });
            """);
        return RunNode(Path.Combine(directory, "run.js"), directory,
            PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js"));
    }

    private static string RunNode(string scriptPath, string directory, string runtimePath)
    {
        InterpretJsPair.SkipIfJavaScriptUnavailable();
        var node = Environment.GetEnvironmentVariable("MALDA_NODE_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(node) ? "node" : node,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(runtimePath);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30_000), "Node timed out.");
        Assert.True(process.ExitCode == 0, stderr.Result);
        return stdout.Result.Replace("\r", string.Empty).TrimEnd('\n');
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "malda_store_js", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static List<Parser.AST.Statements.Statement> Parse(string source)
    {
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }
}
