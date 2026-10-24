// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using MaldaLang.BuiltIns;
using MaldaLang.Runtime.UI;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// <c>ui.*</c> local mode (J6): the JavaScript runtime's <c>ui</c> area runs sessions, envelopes,
/// events, lifecycle hooks, state and messages with the host's rules, so one program prints the
/// same on the interpreter, C# transpile and JavaScript. In a page it draws through
/// <c>MaldaUiDom</c> and <c>ui.run</c> answers the user's clicks (checked on a fake DOM in Node);
/// the compiler writes <c>malda-ui-client.js</c> beside the program and keeps JS0008 for the
/// members that need the host.
/// </summary>
[Collection("Sequential")]
public class UiLocalModeTests
{
    private const string Program = """
        var sid = "parity";
        ui.onMount("Counter", sid);
        ui.onUpdate("Counter", sid);

        function view(state) {
            return ui.column({ "componentId": "Counter" }, [
                ui.heading({ "value": ui.t("title") }),
                ui.text({ "value": ui.t("clicks", { "count": state["count"] }) }),
                ui.button({ "label": "Add", "onClick": "add", "key": "add" }),
                ui.splitPane({ "sizes": [1, 2], "onResize": "resized" }, [
                    ui.panel({}, ["left"]),
                    ui.propertyGrid({ "properties": [{ "key": "n", "value": state["count"] }], "onChange": "edit" })
                ])
            ]);
        }

        function update(state, event) {
            io.print(event["type"] + " " + event["targetPath"] + " " + string(event["payload"]["action"]));
            if (event["payload"]["action"] == "stop") {
                return null;
            }
            return { "count": state["count"] + 1 };
        }

        ui.messages("it", {
            "title": "Contatore",
            "clicks": { "=0": "nessun clic", "one": "{count} clic", "other": "{count} clic totali" }
        });
        ui.dispatchEvent({ "type": "click", "targetPath": "/2/", "targetType": "button", "targetKey": "add", "payload": { "action": "add" } }, sid);
        ui.dispatchEvent({ "type": "click", "targetPath": "/2/", "payload": { "action": "add" } }, sid);
        ui.dispatchEvent({ "type": "click", "targetPath": "/9/", "targetType": "button", "payload": { "action": "add" } }, sid);
        ui.dispatchEvent({ "type": "click", "targetPath": "/2/", "payload": { "action": "stop" } }, sid);
        var final = ui.run({ "count": 0 }, view, update, sid);
        io.print(final["count"]);
        var snap = ui.snapshot(sid);
        io.print(snap["revision"]);
        io.print(snap["staleEventsDropped"]);
        io.print(snap["tree"]["children"][1]["props"]["value"]);
        var env = ui.render(view({ "count": 1234567 }), sid);
        io.print(env["type"] + " " + string(env["baseRevision"]) + " " + string(env["revision"]));
        io.print(env["patches"].length);
        io.print(env["patches"][0]["op"] + " " + env["patches"][0]["path"] + " " + env["patches"][0]["prop"]);
        io.print(env["patches"][1]["op"] + " " + env["patches"][1]["path"] + " " + env["patches"][1]["prop"]);
        io.print(env["treeHash"]);
        io.print(ui.t("clicks", { "count": 0 }) + " / " + ui.t("clicks", { "count": 1 }));
        io.print(ui.t("clicks", { "count": 1234567.891 }));
        ui.messages("ru", { "files": { "one": "{count} файл", "few": "{count} файла", "many": "{count} файлов" } });
        for (var n in [1, 2, 5, 11, 21, 22, 25]) {
            io.print(ui.t("files", { "count": n }));
        }
        ui.messages("en");
        io.print(ui.t("title") + " " + ui.t("hi {name}", { "name": "Ada" }));
        io.print(ui.when(true, "yes", "no") + " " + ui.choose("b", { "a": "A", "b": "B" }));
        try { ui.mount(ui.text({})); } catch (e) { io.print(e); }
        try { ui.mount(ui.panel({ "onClick": "x" })); } catch (e) { io.print(e); }
        try { ui.configure("maxEventQueueDepth", 3, sid); } catch (e) { io.print(e); }
        io.print(ui.dispatchEvent({ "type": "x" }, "seq", 1)["type"]);
        io.print(ui.dispatchEvent({ "type": "x" }, "seq", 5)["error"]["message"]);
        """;

    private const string ExpectedOutput = """
        click /2/ add
        click /2/ add
        click /2/ stop
        2
        3
        1
        2 clic totali
        patch 3 4
        2
        SetProp /1/ value
        SetProp /3/1/ properties
        c9f80528
        nessun clic / 1 clic
        1.234.567,891 clic totali
        1 файл
        2 файла
        5 файлов
        11 файлов
        21 файл
        22 файла
        25 файлов
        title hi Ada
        yes B
        ui.text() missing required prop 'value'.
        ui.panel() does not support event 'onClick'.
        maxQueueDepth must be >= 32
        ack
        gap_expected_2
        """;

    private static void ResetHostState()
    {
        UiSessionRegistry.Clear();
        UiMessages.Reset();
    }

    [Fact]
    public async Task Interpreter_PrintsTheExpectedOutput()
    {
        ResetHostState();
        var output = await TestBase.CaptureInterpretAsync(Program);

        Assert.Equal(InterpretTranspilePair.Normalize(ExpectedOutput), InterpretTranspilePair.Normalize(output));
    }

    [Fact]
    public void CSharpTranspile_MatchesTheInterpreter()
    {
        ResetHostState();
        InterpretTranspilePair.AssertSameFromSource(Program, "ui-local-csharp");
    }

    [Fact]
    public void JavaScript_MatchesTheInterpreter()
    {
        ResetHostState();
        InterpretJsPair.AssertSameFromSource(Program, "ui-local-js");
    }

    [Fact]
    public void Plurals_FollowTheSameRulesOnBothSides()
    {
        var cases = new (string Locale, double N)[]
        {
            ("en", 1), ("en", 0), ("en", 1.5), ("fr", 0), ("fr", 1.7), ("fr", 2), ("pt", 0), ("pt-PT", 0),
            ("ru", 1), ("ru", 3), ("ru", 11), ("ru", 112), ("ru", 1.5), ("pl", 1), ("pl", 4), ("pl", 14), ("pl", 22), ("pl", 25),
            ("cs", 1), ("cs", 3), ("cs", 5), ("cs", 1.5), ("ja", 1), ("it-IT", 1)
        };
        var expected = string.Join(",", cases.Select(c => UiMessages.PluralCategory(c.Locale, c.N)));
        var script = "require(process.argv[2]); const ui = mlRuntime.ui; process.stdout.write(["
            + string.Join(",", cases.Select(c => $"ui._pluralCategory({System.Text.Json.JsonSerializer.Serialize(c.Locale)}, {c.N.ToString(System.Globalization.CultureInfo.InvariantCulture)})"))
            + "].join(\",\"));";

        var actual = RunNode(script, RuntimePath);
        if (actual == null)
            return;
        Assert.Equal(expected, actual);
        Assert.Equal("one,other,other,one,one,other,one,other,one,few,many,many,other,one,few,many,few,many,one,few,other,many,other,one", expected);
    }

    /// <summary>The runtime's control table is <see cref="UiControlSpecRegistry"/>'s: same types, same events.</summary>
    [Fact]
    public void JavaScriptControlEvents_MatchTheHostSpecs()
    {
        var json = RunNode("require(process.argv[2]); process.stdout.write(JSON.stringify(mlRuntime.ui._controlEvents));", RuntimePath);
        if (json == null)
            return;

        var table = JsonNode.Parse(json)!.AsObject();
        foreach (var (type, events) in table)
        {
            var allowed = events!.AsArray().Select(e => (string)e!).ToHashSet(StringComparer.Ordinal);
            foreach (var candidate in new[] { "onClick", "onChange", "onInput", "onSubmit", "onClose", "onSelect", "onResize", "onRowClick", "onNodeSelect", "onPointerDown", "onWheel" })
            {
                var node = new UiNode(type, new Dictionary<string, Interpreter.RuntimeValue>(StringComparer.Ordinal)
                {
                    [candidate] = Interpreter.RuntimeValue.String("x"),
                    ["value"] = Interpreter.RuntimeValue.String("v")
                });
                var hostAllows = Record.Exception(() => UiControlSpecRegistry.Validate(node)) == null;
                Assert.True(hostAllows == allowed.Contains(candidate), $"{type}.{candidate}: host {hostAllows}, JavaScript {allowed.Contains(candidate)}");
            }
        }
    }

    /// <summary>The checker's local-mode list is the runtime's ui object, and the host has every member.</summary>
    [Fact]
    public void LocalModeMembers_AreTheRuntimeUiApi()
    {
        var json = RunNode("require(process.argv[2]); process.stdout.write(JSON.stringify(Object.keys(mlRuntime.ui).filter((k) => !k.startsWith('_'))));", RuntimePath);
        if (json == null)
            return;

        var runtimeMembers = JsonNode.Parse(json)!.AsArray().Select(n => (string)n!).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(Compiler.JsCapabilityChecker.JsUiMembers.OrderBy(n => n, StringComparer.Ordinal), runtimeMembers.OrderBy(n => n, StringComparer.Ordinal));
        var ui = new UiFrameworkInstance();
        foreach (var member in runtimeMembers)
            Assert.Null(Record.Exception(() => ui.Get(member)));
    }

    [Fact]
    public void CapabilityChecker_AllowsLocalMode_AndKeepsHostOnlyMembers()
    {
        var parser = new Parser.Parser(new Lexer("""
            var tree = ui.column({}, [ui.text({ "value": "x" })]);
            ui.mount(tree);
            ui.run(0, function (s) { return tree; }, function (s, e) { return null; });
            ui.messages("en", {});
            var html = ui.template("<p>{{x}}</p>", {});
            var flat = uiRow({});
            """).Tokenize());
        var diagnostics = Compiler.JsCapabilityChecker.Check(parser.Parse());

        Assert.Equal(new[] { "JS0008", "JS0008" }, diagnostics.Select(d => d.Code));
        Assert.Contains("ui.template(…) is host-only", diagnostics[0].Message);
        Assert.Contains("uiRow(…) is a flat alias: call ui.row(…)", diagnostics[1].Message);
    }

    [Fact]
    public void Compile_WritesTheClientBeside_AndLoadsItInLocalMode()
    {
        var folder = Path.Combine(Path.GetTempPath(), "malda_ui_local", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "app.malda");
            File.WriteAllText(source, """ui.mount(ui.text({ "value": "hi" }));""");
            var result = new Compiler.Compiler().CompileToJavaScript(source, Path.Combine(folder, "out", "app.js"));
            Assert.True(result.Success, result.ErrorMessage);

            var output = Path.Combine(folder, "out");
            Assert.Equal(Compiler.Compiler.GetUiClientAssetContent(), File.ReadAllText(Path.Combine(output, "malda-ui-client.js")));
            Assert.Equal(File.ReadAllText(PlanningPaths.ResolveRepoFile("MaldaLang.UIHost", "wwwroot", "malda-ui-client.js")), Compiler.Compiler.GetUiClientAssetContent());
            var html = File.ReadAllText(Path.Combine(output, "index.html"));
            Assert.Contains("<script src=\"./malda-ui-client.js\" data-ui-mode=\"local\"></script>", html);
            Assert.True(html.IndexOf("malda-ui-client.js", StringComparison.Ordinal) < html.IndexOf("malda-js-runtime.js", StringComparison.Ordinal));
            Assert.Contains("<div id=\"status\" hidden>", html);
            Assert.Contains("ui", Compiler.JsRuntimeAreas.KeptBy(Path.Combine(output, "malda-js-runtime.js"))!);

            File.WriteAllText(source, """io.print(1);""");
            var plainOutput = Path.Combine(folder, "plain");
            Assert.True(new Compiler.Compiler().CompileToJavaScript(source, Path.Combine(plainOutput, "app.js")).Success);
            Assert.False(File.Exists(Path.Combine(plainOutput, "malda-ui-client.js")));
            Assert.DoesNotContain("malda-ui-client.js", File.ReadAllText(Path.Combine(plainOutput, "index.html")));
            Assert.DoesNotContain("ui", Compiler.JsRuntimeAreas.KeptBy(Path.Combine(plainOutput, "malda-js-runtime.js"))!);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }

    [Fact]
    public void JavaScript_AwaitsUiRun()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            var last = ui.run(0, function (s) { return ui.text({ "value": string(s) }); }, function (s, e) { return null; });
            dom.copyText("x");
            """);
        Assert.Contains("await mlRuntime.ui.run(", js);
        Assert.Contains("await mlRuntime.dom.copyText(", js);
    }

    private const string DomProgram = """
        function view(state) {
            return ui.column({}, [
                ui.text({ "value": "count=" + string(state["count"]) }),
                ui.button({ "label": "Add", "onClick": "add" }),
                ui.button({ "label": "Quit", "onClick": "quit" }),
                ui.textField({ "value": state["name"], "onChange": "rename" })
            ]);
        }
        function update(state, event) {
            io.print(event["type"] + " " + event["targetPath"] + " " + event["payload"]["action"]);
            if (event["payload"]["action"] == "quit") {
                return null;
            }
            if (event["payload"]["action"] == "add") {
                return { "count": state["count"] + 1, "name": state["name"] };
            }
            return { "count": state["count"], "name": event["payload"]["value"] };
        }
        var final = ui.run({ "count": 0, "name": "a" }, view, update);
        io.print("final " + string(final["count"]) + " " + final["name"]);
        """;

    /// <summary>
    /// The compiled program in a page (a fake document with <c>#app</c>): ui.run mounts, waits for
    /// the user, patches the DOM after each click (the typed field keeps its element) and returns
    /// when update returns null.
    /// </summary>
    [Fact]
    public void Page_UiRunAnswersClicks_AndPatchesTheDom()
    {
        var folder = Path.Combine(Path.GetTempPath(), "malda_ui_local", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "app.malda");
            File.WriteAllText(source, DomProgram);
            var result = new Compiler.Compiler().CompileToJavaScript(source, Path.Combine(folder, "app.js"));
            Assert.True(result.Success, result.ErrorMessage);
            File.WriteAllText(Path.Combine(folder, "harness.js"), PageHarness, new UTF8Encoding(false));

            var output = RunNode(File.ReadAllText(Path.Combine(folder, "harness.js")), folder);
            if (output == null)
                return;
            Assert.Equal(
                string.Join("\n",
                    "mounted count=0 Add,Quit value=a",
                    "click /1/ add",
                    "after add count=1 same-input=true",
                    "change /3/ rename",
                    "click /1/ add",
                    "click /2/ quit",
                    "final 2 typed",
                    "connection local"),
                output.Replace("\r", string.Empty).Trim());
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }

    private const string PageHarness = """
        const path = require("path");
        const folder = process.argv[2];
        class Node { constructor() { this.childNodes = []; this.parentNode = null; } get firstChild() { return this.childNodes[0] || null; }
          appendChild(c) { return this.insertBefore(c, null); }
          insertBefore(c, ref) { if (c.parentNode) c.parentNode.removeChild(c); const i = ref ? this.childNodes.indexOf(ref) : this.childNodes.length; this.childNodes.splice(i, 0, c); c.parentNode = this; return c; }
          removeChild(c) { this.childNodes.splice(this.childNodes.indexOf(c), 1); c.parentNode = null; return c; }
          replaceChild(n, o) { this.insertBefore(n, o); this.removeChild(o); return o; }
          contains(n) { while (n) { if (n === this) return true; n = n.parentNode; } return false; }
          get textContent() { return this.childNodes.map((c) => c.textContent).join(""); } }
        class Text extends Node { constructor(t) { super(); this.nodeType = 3; this.nodeValue = t; } get textContent() { return this.nodeValue; } }
        class El extends Node { constructor(tag) { super(); this.nodeType = 1; this.tagName = tag.toUpperCase(); this.attrs = new Map(); this.listeners = {}; this.dataset = {}; this.value = ""; this.checked = false;
            this.style = { cssText: "", setProperty() {}, removeProperty() {} }; const el = this; this.classList = { add(n) { el.className = (el.className + " " + n).trim(); } }; }
          get attributes() { return Array.from(this.attrs, ([name, value]) => ({ name, value })); }
          setAttribute(n, v) { this.attrs.set(n, String(v)); } getAttribute(n) { return this.attrs.has(n) ? this.attrs.get(n) : null; }
          hasAttribute(n) { return this.attrs.has(n); } removeAttribute(n) { this.attrs.delete(n); }
          get className() { return this.getAttribute("class") || ""; } set className(v) { this.setAttribute("class", v); }
          addEventListener(t, l) { (this.listeners[t] = this.listeners[t] || []).push(l); }
          fire(t) { for (const l of this.listeners[t] || []) l({ preventDefault() {}, target: this }); }
          all(out = []) { for (const c of this.childNodes) if (c.nodeType === 1) { out.push(c); c.all(out); } return out; }
          querySelector(sel) { const m = /^\[([\w-]+)="(.*)"\]$/.exec(sel); return m ? this.all().find((e) => e.getAttribute(m[1]) === m[2]) || null : null; } }
        const app = new El("div"); app.setAttribute("id", "app");
        const head = new El("head");
        global.document = { head, activeElement: null, createElement: (t) => new El(t), createTextNode: (t) => new Text(t),
          getElementById: (id) => (id === "app" ? app : null), querySelector: (sel) => app.querySelector(sel), addEventListener() {} };
        global.window = global;
        // As loaded by index.html: <script src="./malda-ui-client.js" data-ui-mode="local">.
        document.currentScript = { getAttribute: (name) => (name === "data-ui-mode" ? "local" : null) };
        require(path.join(folder, "malda-ui-client.js"));
        document.currentScript = null;
        require(path.join(folder, "malda-js-runtime.js"));
        const program = require(path.join(folder, "app.js"));
        const tick = () => new Promise((resolve) => setTimeout(resolve, 5));
        const find = (type) => app.all().filter((e) => e.getAttribute("data-ui") === type);
        (async () => {
          const done = program.main();
          await tick();
          const input = find("textField")[0];
          console.log("mounted " + app.textContent + " " + find("button").map((b) => b.getAttribute("label")).join(",") + " value=" + input.value);
          find("button")[0].fire("click");
          await tick();
          console.log("after add " + app.textContent + " same-input=" + (find("textField")[0] === input));
          input.value = "typed";
          input.fire("change");
          await tick();
          find("button")[0].fire("click");
          await tick();
          find("button")[1].fire("click");
          await done;
          console.log("connection " + app.getAttribute("data-ui-connection"));
        })().catch((e) => { console.error(e.stack || e); process.exit(1); });
        """;

    private static string RuntimePath => PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");

    /// <summary>Runs a Node script with <paramref name="argument"/>; null when Node is not installed.</summary>
    private static string? RunNode(string script, string argument)
    {
        var folder = Path.Combine(Path.GetTempPath(), "malda_ui_local", "node_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var scriptPath = Path.Combine(folder, "script.js");
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
            var startInfo = new ProcessStartInfo
            {
                FileName = System.Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } node ? node : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(argument);
            startInfo.Environment["MALDA_UI_CLIENT_PATH"] = PlanningPaths.ResolveRepoFile("MaldaLang.UIHost", "wwwroot", "malda-ui-client.js");

            Process? process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Win32Exception)
            {
                return null;
            }

            using (process!)
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                Assert.True(process.WaitForExit(30_000), "Node timed out.");
                Assert.True(process.ExitCode == 0, "Node failed: " + stderr.Result);
                return stdout.Result;
            }
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }
}
