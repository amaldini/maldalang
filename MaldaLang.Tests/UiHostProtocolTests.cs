// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using BuiltInFunctions = MaldaLang.BuiltIns.BuiltInFunctions;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.UI;
using MaldaLang.Tests.Fuzz;
using MaldaLang.Tests.Planning;
using MaldaLang.UIHost;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Host ↔ browser protocol, end to end: a Node process runs <c>MaldaUiProtocol</c> from
/// <c>malda-ui-client.js</c> over a real WebSocket against the embedded host
/// (<see cref="EmbeddedUiHostRuntime"/>) and the standalone <c>MaldaLang.UIHost</c> process.
/// Each scenario posts <see cref="UiSession"/> envelopes and requires the client's tree to
/// equal the session's tree, without a resync storm. Skipped without Node.
/// <c>ClientProtocol_HandlesStalePatchesGapsAndVersions</c> covers the client cases a healthy
/// host never produces.
/// </summary>
[Collection("Sequential")]
public class UiHostProtocolTests : IClassFixture<UiHostProtocolTests.Hosts>
{
    private readonly Hosts _hosts;

    public UiHostProtocolTests(Hosts hosts) => _hosts = hosts;

    public static IEnumerable<object[]> HostKinds() => new[] { new object[] { "embedded" }, new object[] { "standalone" } };

    [Theory]
    [MemberData(nameof(HostKinds))]
    public async Task Mount_ReachesAConnectedClient(string host)
    {
        await using var scenario = await Scenario.StartAsync(_hosts, host);
        if (scenario == null) return;

        var tab = await scenario.ConnectAsync();
        await scenario.MountAsync(Tree("one"));

        await scenario.AssertInSyncAsync(tab);
    }

    [Theory]
    [MemberData(nameof(HostKinds))]
    public async Task PatchAfterMount_UpdatesAConnectedClient(string host)
    {
        await using var scenario = await Scenario.StartAsync(_hosts, host);
        if (scenario == null) return;

        var tab = await scenario.ConnectAsync();
        await scenario.MountAsync(Tree("one"));
        await scenario.RenderAsync(Tree("two"));

        await scenario.AssertInSyncAsync(tab);
    }

    [Theory]
    [MemberData(nameof(HostKinds))]
    public async Task LateClient_GetsTheCurrentTree(string host)
    {
        await using var scenario = await Scenario.StartAsync(_hosts, host);
        if (scenario == null) return;

        await scenario.MountAsync(Tree("one"));
        await scenario.RenderAsync(Tree("two"));
        var tab = await scenario.ConnectAsync();

        await scenario.AssertInSyncAsync(tab);
    }

    [Theory]
    [MemberData(nameof(HostKinds))]
    public async Task Reconnect_CatchesUpOnAMissedPatch(string host)
    {
        await using var scenario = await Scenario.StartAsync(_hosts, host);
        if (scenario == null) return;

        var tab = await scenario.ConnectAsync();
        await scenario.MountAsync(Tree("one"));
        await tab.CommandAsync("close");
        await scenario.RenderAsync(Tree("two"));
        await tab.CommandAsync("connect");

        await scenario.AssertInSyncAsync(tab);
    }

    [Theory]
    [MemberData(nameof(HostKinds))]
    public async Task TwoTabs_BothFollowPatches(string host)
    {
        await using var scenario = await Scenario.StartAsync(_hosts, host);
        if (scenario == null) return;

        var first = await scenario.ConnectAsync();
        var second = await scenario.ConnectAsync();
        await scenario.MountAsync(Tree("one"));
        await scenario.RenderAsync(Tree("two"));

        await scenario.AssertInSyncAsync(first);
        await scenario.AssertInSyncAsync(second);
    }

    [Fact]
    public async Task LinkedSession_BrowserEventReachesPullEvent_AndRenderReachesTheBrowser()
    {
        await using var scenario = await Scenario.StartAsync(_hosts, "embedded");
        if (scenario == null) return;
        Assert.True(await UiHostLink.StartAsync(), "UiHostLink did not attach to the embedded host");

        // A registry session publishes by itself, as ui.mount / ui.render do in a program.
        var session = UiSessionRegistry.GetOrCreate(scenario.SessionId);
        try
        {
            var tab = await scenario.ConnectAsync();
            session.Mount(Tree("one"));
            await Scenario.AssertShowsAsync(tab, Tree("one"));

            await tab.CommandAsync("""event {"eventType":"click","targetPath":"/1/","payload":{"action":"go"}}""");
            var received = await PullEventAsync(session);
            Assert.Equal("click", received.Type);
            Assert.Equal("/1/", received.TargetPath);
            Assert.Equal("go", received.Payload.AsObject().Get("action").AsString());

            session.Render(Tree("two"));
            await Scenario.AssertShowsAsync(tab, Tree("two"));

            // A click on the button at /1/ is kept and answered by the next render; one that names
            // a node /1/ no longer holds is dropped.
            await tab.CommandAsync("""event {"eventType":"click","targetPath":"/1/","payload":{},"target":{"key":"gone","type":"button"}}""");
            var reply = await tab.CommandAsync("""event {"eventType":"click","targetPath":"/1/","payload":{},"target":{"key":null,"type":"button"}}""");
            var kept = await PullEventAsync(session);
            Assert.Equal("button", kept.TargetType);
            var answered = Assert.IsType<MaldaLang.BuiltIns.JsonObject>(session.Render(Tree("three")).AsObject());
            Assert.Equal((string)reply["id"]!, answered.Get("answersEventId", null).AsString());
            Assert.Equal(1, Assert.IsType<MaldaLang.BuiltIns.JsonObject>(session.SnapshotAsRuntimeValue().AsObject()).Get("staleEventsDropped", null).AsInteger());
        }
        finally
        {
            UiSessionRegistry.Remove(scenario.SessionId);
        }
    }

    public static IEnumerable<object[]> Programs() =>
        from backend in new[] { "interpret", "transpile" }
        from style in new[] { "loop", "run" }
        select new object[] { backend, style };

    /// <summary>The counter as a hand-written pull loop, ending at count 2.</summary>
    private const string LoopCounter = """
var sid = "counter";

function view(n) {
    return ui.column({}, [
        ui.text({"value": "count=" + string(n)}),
        ui.button({"label": "Inc"})
    ]);
}

var count = 0;
ui.mount(view(count), sid);
io.print("mounted");
while (count < 2) {
    var e = ui.pullEvent(sid);
    if (e == null) {
        sleep(20);
    } else {
        if (e["type"] == "click") {
            count = count + 1;
            ui.render(view(count), sid);
        }
    }
}
io.print("done");
""";

    /// <summary>The counter on <c>ui.run</c>, ending on a close event.</summary>
    private const string RunCounter = """
var sid = "counter";

function view(n) {
    return ui.column({}, [
        ui.text({"value": "count=" + string(n)}),
        ui.button({"label": "Inc"})
    ]);
}

function update(n, e) {
    if (e["type"] == "close") {
        return null;
    }
    if (e["type"] == "click") {
        return n + 1;
    }
    return n;
}

io.print("mounted");
var last = ui.run(0, view, update, sid);
io.print("done");
""";

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task Program_AnswersBrowserClicks(string backend, string style)
    {
        if (!NodeTab.NodeAvailable())
            return;

        const string sessionId = "counter";
        var url = $"http://127.0.0.1:{Hosts.FreePort()}";
        var folder = Path.Combine(Path.GetTempPath(), "malda_ui_host", "program_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var programPath = Path.Combine(folder, "counter.malda");
        File.WriteAllText(programPath, style == "run" ? RunCounter : LoopCounter);

        var startInfo = new ProcessStartInfo(backend == "transpile" ? CompileTranspiled(programPath) : "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = folder
        };
        if (backend == "interpret")
        {
            startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "malda.dll"));
            startInfo.ArgumentList.Add(programPath);
        }
        startInfo.Environment["MALDA_UI_HOST_URL"] = url;
        startInfo.Environment.Remove("MALDA_UI_AUTH_TOKEN");
        using var program = Process.Start(startInfo)!;
        var stderr = program.StandardError.ReadToEndAsync();
        await using var scenario = Scenario.At(url, sessionId);
        try
        {
            Assert.Equal("mounted", await ReadLineAsync(program, stderr));
            var tab = await scenario.ConnectAsync();
            await WaitForTextAsync(tab, "count=0");

            await tab.CommandAsync("""event {"eventType":"click","targetPath":"/1/","payload":{}}""");
            await WaitForTextAsync(tab, "count=1");

            await tab.CommandAsync("""event {"eventType":"click","targetPath":"/1/","payload":{}}""");
            await WaitForTextAsync(tab, "count=2");
            // Ends ui.run; the loop program has already stopped by itself.
            await tab.CommandAsync("""event {"eventType":"close","targetPath":"/","payload":{}}""");
            Assert.Equal("done", await ReadLineAsync(program, stderr));
        }
        finally
        {
            try { program.Kill(entireProcessTree: true); } catch { }
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }

    private static string CompileTranspiled(string programPath)
    {
        var result = new MaldaLang.Compiler.Compiler().Compile(
            programPath,
            Path.ChangeExtension(programPath, ".exe"),
            MaldaLang.Compiler.CompilationMode.TranspileToCSharp,
            includeLLamaSharp: false,
            includeUiHost: false,
            profilingOptions: null,
            typedTranspileLevel: 1,
            includeOptionalPacks: false);
        Assert.True(result.Success && File.Exists(result.OutputPath), "transpile failed: " + result.ErrorMessage);
        return result.OutputPath!;
    }

    private static async Task<UiEvent> PullEventAsync(UiSession session)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (session.TryDequeueEvent(out var uiEvent) && uiEvent != null)
                return uiEvent;
            await Task.Delay(20);
        }
        throw new Xunit.Sdk.XunitException("the browser event never reached the session");
    }

    private static async Task<string?> ReadLineAsync(Process program, Task<string> stderr)
    {
        try
        {
            var line = await program.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
            if (line == null)
                throw new Xunit.Sdk.XunitException("the program exited early: " + await stderr);
            return line.Trim();
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException("the program printed nothing for 60 s");
        }
    }

    /// <summary>Dumps the tab until its tree contains <paramref name="text"/> (5 s at most).</summary>
    private static async Task WaitForTextAsync(NodeTab tab, string text)
    {
        string? tree = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            tree = (await tab.CommandAsync("dump"))["tree"]?.ToJsonString();
            if (tree != null && tree.Contains(text, StringComparison.Ordinal))
                return;
        }
        throw new Xunit.Sdk.XunitException($"the tab never showed '{text}'; last tree: {tree ?? "null"}");
    }

    [Fact]
    public void ClientProtocol_HandlesStalePatchesGapsAndVersions()
    {
        if (!NodeTab.NodeAvailable())
            return;

        NodeTab.RunScript("""
const assert = require("assert");
const { MaldaUiProtocol: P } = require(process.argv[2]);
const kinds = (actions) => actions.map((a) => a.apply ? "apply" : a.send ? "send:" + a.send.type : "warn:" + a.warn.kind);
const msg = (type, sequence, payload) => ({ type, version: P.version, sequence, payload });

const s = P.createState("t");
assert.deepStrictEqual(kinds(P.onOpen(s)), ["send:resync"]);
assert.deepStrictEqual(kinds(P.receive(s, msg("connected", 1))), []);
assert.deepStrictEqual(kinds(P.receive(s, msg("resync", 2, null))), []);
assert.deepStrictEqual(kinds(P.receive(s, msg("mount", 3, { revision: 1, patches: [] }))), ["apply"]);
assert.deepStrictEqual(kinds(P.receive(s, msg("patch", 4, { revision: 2, baseRevision: 1, patches: [] }))), ["apply"]);
assert.strictEqual(s.revision, 2);

// A patch on a revision the client never had asks for one resync, not one per patch.
assert.deepStrictEqual(kinds(P.receive(s, msg("patch", 5, { revision: 4, baseRevision: 3, patches: [] }))), ["send:resync"]);
assert.deepStrictEqual(kinds(P.receive(s, msg("patch", 6, { revision: 5, baseRevision: 4, patches: [] }))), []);
assert.deepStrictEqual(kinds(P.receive(s, msg("resync", 7, { revision: 5, patches: [] }))), ["apply"]);
assert.strictEqual(s.revision, 5);

// A sequence gap is reported once and the count follows the host.
assert.deepStrictEqual(kinds(P.receive(s, msg("ping", 9))), ["warn:sequence_gap", "send:resync", "send:pong"]);
assert.deepStrictEqual(kinds(P.receive(s, msg("ping", 10))), ["send:pong"]);

// Another protocol version is reported and never answered with a resync.
assert.deepStrictEqual(kinds(P.receive(s, { type: "patch", version: "1.0", sequence: 11, payload: { patches: [] } })), ["warn:version_mismatch"]);

// Outgoing sequence numbers restart with each socket.
assert.strictEqual(P.outbound(s, { type: "pong" }).sequence, 1);
P.onOpen(s);
assert.strictEqual(P.outbound(s, { type: "pong" }).sequence, 1);
""");
    }

    private static UiNode Tree(string label) =>
        new("column", children: new List<UiNode>
        {
            new("text", new Dictionary<string, RuntimeValue> { ["value"] = RuntimeValue.String(label) }),
            new("button", new Dictionary<string, RuntimeValue> { ["label"] = RuntimeValue.String("go " + label) })
        });

    /// <summary>One session on one host, plus the Node tabs connected to it.</summary>
    private sealed class Scenario : IAsyncDisposable
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private readonly string _baseUrl;
        private readonly string _sessionId;
        private readonly UiSession _session;
        private readonly List<NodeTab> _tabs = new();
        private UiNode? _current;

        private Scenario(string baseUrl, string? sessionId = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _sessionId = sessionId ?? "proto-" + Guid.NewGuid().ToString("N")[..8];
            // A bare session: the scenario posts its envelopes over HTTP itself.
            _session = new UiSession(_sessionId);
        }

        public string SessionId => _sessionId;

        /// <summary>Null when Node is not installed.</summary>
        public static async Task<Scenario?> StartAsync(Hosts hosts, string host)
        {
            if (!NodeTab.NodeAvailable())
                return null;
            return new Scenario(await hosts.BaseUrlAsync(host));
        }

        /// <summary>Tabs on <paramref name="sessionId"/> of a host someone else drives, such as a running program.</summary>
        public static Scenario At(string baseUrl, string sessionId) => new(baseUrl, sessionId);

        public async Task<NodeTab> ConnectAsync()
        {
            var wsUrl = _baseUrl.Replace("http://", "ws://", StringComparison.Ordinal) + "/ui/ws/" + _sessionId;
            var tab = NodeTab.Start(wsUrl, _sessionId);
            _tabs.Add(tab);
            await tab.CommandAsync("connect");
            return tab;
        }

        public Task MountAsync(UiNode root)
        {
            _current = root;
            return PostAsync("mount", _session.Mount(root));
        }

        public Task RenderAsync(UiNode next)
        {
            _current = next;
            return PostAsync("patch", _session.Render(next));
        }

        public Task AssertInSyncAsync(NodeTab tab) => AssertShowsAsync(tab, _current);

        public static async Task AssertShowsAsync(NodeTab tab, UiNode? tree)
        {
            var dump = await tab.CommandAsync("dump");
            var expected = UiPatchApplier.Canonical(UiPatchApplier.ToJson(tree));
            var actual = UiPatchApplier.Canonical(dump["tree"]);
            var received = dump["received"]!.AsArray().Select(m => (string?)m).ToList();
            var trace = $"received [{string.Join(", ", received.Take(12))}{(received.Count > 12 ? $", … {received.Count} in all" : "")}], resyncs sent {(int)dump["resyncs"]!}";

            Assert.True((bool)dump["quiet"]!, "the client never went quiet (resync storm?): " + trace);
            Assert.True(expected == actual, $"client tree {actual} != session tree {expected}; {trace}");
        }

        private async Task PostAsync(string kind, RuntimeValue envelope)
        {
            using var content = new StringContent(BuiltInFunctions.SerializeToJson(envelope), Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync($"{_baseUrl}/ui/{kind}/{_sessionId}", content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public ValueTask DisposeAsync()
        {
            foreach (var tab in _tabs)
                tab.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// A Node process playing one browser tab: <c>connect</c>, <c>close</c>, <c>dump</c> and
    /// <c>event {json}</c> on stdin, one JSON line back per command. The protocol state survives reconnects,
    /// as in the browser.
    /// </summary>
    private sealed class NodeTab : IDisposable
    {
        private const string Harness = """
const { MaldaUiProtocol } = require(process.argv[2]);
const wsUrl = process.argv[3];
const state = MaldaUiProtocol.createState(process.argv[4]);
let tree = null;
let ws = null;
let resyncs = 0;
let lastActivity = Date.now();
const received = [];
const reply = (obj) => process.stdout.write(JSON.stringify(obj) + "\n");

function run(actions) {
  for (const action of actions) {
    if (action.apply !== undefined) {
      tree = MaldaUiProtocol.applyPayload(tree, action.apply);
    } else if (action.send) {
      if (action.send.type === "resync") resyncs++;
      if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(MaldaUiProtocol.outbound(state, action.send)));
    }
  }
}

const commands = {
  connect() {
    ws = new WebSocket(wsUrl);
    let answered = false;
    ws.onopen = () => {
      lastActivity = Date.now();
      run(MaldaUiProtocol.onOpen(state));
      answered = true;
      reply({ ok: true });
    };
    ws.onerror = () => {
      if (!answered) { answered = true; reply({ ok: false, error: "websocket error" }); }
    };
    ws.onmessage = (evt) => {
      lastActivity = Date.now();
      const message = JSON.parse(evt.data);
      received.push(message.type + ":" + message.sequence);
      run(MaldaUiProtocol.receive(state, message));
    };
  },
  close() {
    ws.onclose = () => reply({ ok: true });
    ws.close();
  },
  dump() {
    const started = Date.now();
    const tick = () => {
      const quiet = Date.now() - lastActivity >= 300;
      if (quiet || Date.now() - started > 3000) reply({ ok: true, tree, resyncs, received, quiet });
      else setTimeout(tick, 50);
    };
    setTimeout(tick, 50);
  }
};

require("readline").createInterface({ input: process.stdin }).on("line", (line) => {
  // "event {json}" plays a browser event: { eventType, targetPath, payload, target? }; the
  // reply carries its id.
  if (line.startsWith("event ")) {
    const e = JSON.parse(line.slice(6));
    const message = MaldaUiProtocol.event(state, e.eventType, e.targetPath, e.payload, e.target);
    run([{ send: message }]);
    reply({ ok: true, id: message.envelopeId });
    return;
  }
  const command = commands[line.trim()];
  if (command) command();
  else reply({ ok: false, error: "unknown command " + line });
});
""";

        private static readonly UTF8Encoding NoBom = new(encoderShouldEmitUTF8Identifier: false);
        private static bool? _nodeAvailable;

        private readonly Process _process;
        private readonly string _folder;

        private NodeTab(Process process, string folder)
        {
            _process = process;
            _folder = folder;
        }

        private static string ClientPath => Path.Combine(PlanningPaths.RepoRoot, "MaldaLang.UIHost", "wwwroot", "malda-ui-client.js");

        private static string NodePath =>
            System.Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } node ? node : "node";

        public static bool NodeAvailable()
        {
            if (_nodeAvailable is { } known)
                return known;
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(NodePath, "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                probe!.WaitForExit(10_000);
                _nodeAvailable = probe.ExitCode == 0;
            }
            catch (Win32Exception)
            {
                _nodeAvailable = false;
            }
            return _nodeAvailable.Value;
        }

        public static NodeTab Start(string wsUrl, string sessionId)
        {
            var folder = Path.Combine(Path.GetTempPath(), "malda_ui_host", "tab_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var harnessPath = Path.Combine(folder, "tab.js");
            File.WriteAllText(harnessPath, Harness, NoBom);

            var startInfo = new ProcessStartInfo
            {
                FileName = NodePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(harnessPath);
            startInfo.ArgumentList.Add(ClientPath);
            startInfo.ArgumentList.Add(wsUrl);
            startInfo.ArgumentList.Add(sessionId);
            return new NodeTab(Process.Start(startInfo)!, folder);
        }

        /// <summary>Runs a Node script with the client path as its argument; fails the test on a non-zero exit.</summary>
        public static void RunScript(string script)
        {
            var folder = Path.Combine(Path.GetTempPath(), "malda_ui_host", "script_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var scriptPath = Path.Combine(folder, "script.js");
                File.WriteAllText(scriptPath, script, NoBom);
                var startInfo = new ProcessStartInfo
                {
                    FileName = NodePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add(scriptPath);
                startInfo.ArgumentList.Add(ClientPath);
                using var process = Process.Start(startInfo)!;
                var stderr = process.StandardError.ReadToEndAsync();
                process.StandardOutput.ReadToEnd();
                Assert.True(process.WaitForExit(30_000), "Node script timed out.");
                Assert.True(process.ExitCode == 0, "Node script failed: " + stderr.Result);
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch { }
            }
        }

        public async Task<JsonObject> CommandAsync(string command)
        {
            await _process.StandardInput.WriteLineAsync(command);
            await _process.StandardInput.FlushAsync();
            string? line;
            try
            {
                line = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                throw new Xunit.Sdk.XunitException($"Node tab did not answer '{command}'.");
            }

            if (line == null)
                throw new Xunit.Sdk.XunitException($"Node tab exited on '{command}': " + await _process.StandardError.ReadToEndAsync());

            var reply = JsonNode.Parse(line)!.AsObject();
            Assert.True((bool)reply["ok"]!, $"'{command}' failed: {line}");
            return reply;
        }

        public void Dispose()
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
            _process.Dispose();
            try { Directory.Delete(_folder, recursive: true); } catch { }
        }
    }

    /// <summary>Starts each host once per test class, on a free loopback port.</summary>
    public sealed class Hosts : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private string? _embeddedUrl;
        private string? _standaloneUrl;
        private Process? _standalone;

        public async Task<string> BaseUrlAsync(string host)
        {
            await _gate.WaitAsync();
            try
            {
                return host == "embedded" ? _embeddedUrl ??= await StartEmbeddedAsync() : _standaloneUrl ??= await StartStandaloneAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        private static async Task<string> StartEmbeddedAsync()
        {
            if (EmbeddedUiHostRuntime.BaseUrl is { } running)
                return running;

            var previous = System.Environment.GetEnvironmentVariable("MALDA_UI_HOST_URL");
            System.Environment.SetEnvironmentVariable("MALDA_UI_HOST_URL", $"http://127.0.0.1:{FreePort()}");
            try
            {
                Assert.True(await EmbeddedUiHostRuntime.TryStartAsync(), "embedded UI host did not start");
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("MALDA_UI_HOST_URL", previous);
            }
            return EmbeddedUiHostRuntime.BaseUrl!;
        }

        private async Task<string> StartStandaloneAsync()
        {
            var url = $"http://127.0.0.1:{FreePort()}";
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "MaldaLang.UIHost.dll"));
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(url);
            startInfo.Environment.Remove("MALDA_UI_AUTH_TOKEN");
            _standalone = Process.Start(startInfo)!;
            _standalone.OutputDataReceived += (_, _) => { };
            _standalone.ErrorDataReceived += (_, _) => { };
            _standalone.BeginOutputReadLine();
            _standalone.BeginErrorReadLine();

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            for (var i = 0; i < 100; i++)
            {
                try
                {
                    if ((await http.GetAsync(url + "/health")).IsSuccessStatusCode)
                        return url;
                }
                catch (HttpRequestException)
                {
                    // Still starting.
                }
                catch (TaskCanceledException)
                {
                    // Still starting.
                }
                await Task.Delay(100);
            }
            throw new Xunit.Sdk.XunitException("standalone MaldaLang.UIHost did not answer /health");
        }

        public static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            if (_standalone != null)
            {
                try { _standalone.Kill(entireProcessTree: true); } catch { }
                _standalone.Dispose();
            }
        }
    }
}
