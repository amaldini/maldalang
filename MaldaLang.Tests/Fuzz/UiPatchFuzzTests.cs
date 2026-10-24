// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.UI;
using MaldaLang.Tests.Planning;
using MaldaLang.UIHost;
using Xunit;

namespace MaldaLang.Tests.Fuzz;

/// <summary>
/// Server/client UI protocol fuzzing: a seeded generator builds a tree and an edited copy
/// (props changed, children inserted, removed, swapped or shuffled, keyed and unkeyed). The
/// patches from <see cref="UiDiffEngine.Diff"/>, applied to the first tree by
/// <see cref="UiPatchApplier"/> and by <c>MaldaUiCore</c> from <c>malda-ui-client.js</c> under
/// Node, must give the second tree. Under Node, <c>MaldaUiDom</c> also applies them to a fake
/// DOM rendered from the first tree: the result, and the paths its listeners report, must equal
/// a fresh render of the second tree. The second tree's <c>treeHash</c> must be the same from
/// the session's <see cref="UiNode"/>, the host's JSON tree and the client's tree, and
/// <c>MaldaUiCore.diff</c> (local mode, the program in the browser) must return the same patches
/// as <see cref="UiDiffEngine.Diff"/>. Set <c>MALDA_UI_FUZZ_SEEDS</c> (e.g. <c>1-200</c>) to
/// explore more locally; a failure writes the trees and patches to
/// <c>%TEMP%/malda_ui_fuzz/seed&lt;N&gt;/</c>. Without Node only the C# applier runs.
/// </summary>
public class UiPatchFuzzTests
{
    private const int CasesPerSeed = 40;

    private static readonly string[] ContainerTypes = { "column", "row", "panel" };
    private static readonly string[] LeafTypes = { "text", "button", "badge", "textField" };
    private static readonly string[] Keys = { "a", "b", "c", "d", "e", "f", "g", "h" };

    public static IEnumerable<object[]> Seeds()
    {
        var configured = System.Environment.GetEnvironmentVariable("MALDA_UI_FUZZ_SEEDS");
        var seeds = string.IsNullOrWhiteSpace(configured)
            ? new[] { 1, 2, 3 }
            : BackendDifferentialFuzzTests.ParseSeeds(configured);
        return seeds.Select(seed => new object[] { seed });
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void DiffPatches_RebuildTheNewTree_OnServerAndClient(int seed)
    {
        var random = new Random(seed);
        var cases = new JsonArray();
        var failures = new List<string>();

        for (var i = 0; i < CasesPerSeed; i++)
        {
            var previous = RandomNode(random, depth: 0);
            var current = random.Next(10) == 0 ? RandomNode(random, depth: 0) : Edit(random, previous, depth: 0);
            var expected = UiPatchApplier.Canonical(UiPatchApplier.ToJson(current));
            var patches = UiPatchApplier.ToJson(UiDiffEngine.Diff(previous, current))!.AsArray();

            var applied = UiPatchApplier.Canonical(UiPatchApplier.Apply(UiPatchApplier.ToJson(previous), patches));
            if (applied != expected)
                failures.Add($"case {i} (C# applier)");
            var hash = UiNode.Hash(current);
            var jsonHash = UiTreeHash.Of(JsonNode.Parse(expected));
            if (jsonHash != hash)
                failures.Add($"case {i} (treeHash: UiNode {hash} != JSON tree {jsonHash})");

            cases.Add(new JsonObject
            {
                ["previous"] = UiPatchApplier.ToJson(previous),
                ["current"] = UiPatchApplier.ToJson(current),
                ["patches"] = patches.DeepClone(),
                ["expected"] = expected,
                ["hash"] = hash
            });
        }

        var casesJson = cases.ToJsonString();
        var clientResults = RunClientInNode(casesJson);
        if (clientResults != null)
        {
            for (var i = 0; i < CasesPerSeed; i++)
            {
                var expected = (string)cases[i]!["expected"]!;
                var result = clientResults[i];
                var actual = UiPatchApplier.Canonical(JsonNode.Parse((string)result["tree"]!));
                if (actual != expected)
                    failures.Add($"case {i} (malda-ui-client.js)");
                var serverPatches = UiPatchApplier.Canonical(cases[i]!["patches"]);
                var clientPatches = UiPatchApplier.Canonical(JsonNode.Parse((string)result["patches"]!));
                if (clientPatches != serverPatches)
                    failures.Add($"case {i} (MaldaUiCore.diff): {Truncate(clientPatches)} != UiDiffEngine {Truncate(serverPatches)}");
                if ((string?)result["hash"] != (string?)cases[i]!["hash"])
                    failures.Add($"case {i} (treeHash: client {result["hash"]} != server {cases[i]!["hash"]})");
                if ((string?)result["patchedDom"] != (string?)result["freshDom"])
                    failures.Add($"case {i} (DOM patch): {Truncate((string?)result["patchedDom"] ?? "")} != fresh {Truncate((string?)result["freshDom"] ?? "")}");
                else if ((string?)result["patchedEvents"] != (string?)result["freshEvents"])
                    failures.Add($"case {i} (DOM listeners): {result["patchedEvents"]} != fresh {result["freshEvents"]}");
            }
        }

        if (failures.Count == 0)
            return;

        var folder = Path.Combine(Path.GetTempPath(), "malda_ui_fuzz", $"seed{seed}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "cases.json"), casesJson, Encoding.UTF8);
        Assert.Fail(
            $"Seed {seed}: patches do not rebuild the new tree. Cases: {Path.Combine(folder, "cases.json")}"
            + System.Environment.NewLine + string.Join(System.Environment.NewLine, failures));
    }

    /// <summary>
    /// The values where a hash of JSON text could drift between C# and JavaScript: number forms,
    /// escapes, non-ASCII and lone surrogates, prop order, nested objects. The session's published
    /// mount envelope carries the hash; the host's JSON tree and the client's tree must hash to it.
    /// </summary>
    [Fact]
    public void TreeHash_IsTheSameOnSessionHostAndClient()
    {
        var nested = new MaldaLang.BuiltIns.JsonObject();
        nested.Set("z", RuntimeValue.Float(1e21));
        nested.Set("a", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Null(), RuntimeValue.Boolean(false), RuntimeValue.Float(-0.0) }));
        nested.Set("é", RuntimeValue.Integer(int.MinValue));
        var props = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal)
        {
            ["value"] = RuntimeValue.String("é 😀 \u0001 \u007f \u2028 a\"b\\c\n\t</script>"),
            ["lone"] = RuntimeValue.String("x\ud800y"),
            ["Upper"] = RuntimeValue.Integer(-1),
            ["floats"] = RuntimeValue.Array(new[] { 0.1, 1.5, 100.0, 1e-7, 1.5e-6, 123456789012345680000.0, 1e300, -2.5e-300, 5e-324, double.MaxValue }
                .Select(RuntimeValue.Float).ToList()),
            ["meta"] = RuntimeValue.Object(nested)
        };
        var root = new UiNode("column", new Dictionary<string, RuntimeValue>(StringComparer.Ordinal) { ["gap"] = RuntimeValue.Integer(4) },
            new List<UiNode> { new("text", props, key: "ké"), new("button", key: " ") });

        string? published = null;
        new UiSession("hash", (_, _, json) => published = json).Mount(root);
        var envelope = JsonNode.Parse(published!)!;
        var hash = (string)envelope["treeHash"]!;
        Assert.Equal(UiNode.Hash(root), hash);
        Assert.Equal(hash, UiTreeHash.Of(envelope["patches"]![0]!["value"]));
        Assert.Equal("1e+21", UiTreeHash.JavaScriptNumber(1e21));
        Assert.Equal("1.5e-7", UiTreeHash.JavaScriptNumber(1.5e-7));
        Assert.Equal("0.0000015", UiTreeHash.JavaScriptNumber(1.5e-6));
        Assert.Equal("123456789012345680000", UiTreeHash.JavaScriptNumber(123456789012345680000.0));
        Assert.Equal("5e-324", UiTreeHash.JavaScriptNumber(5e-324));

        RunNodeScript($$"""
const assert = require("assert");
const client = require(process.argv[2]);
const envelope = JSON.parse({{System.Text.Json.JsonSerializer.Serialize(published)}});
const tree = client.applyPatches(null, envelope.patches);
assert.strictEqual(client.treeHash(tree), envelope.treeHash);
""");
    }

    [Fact]
    public void DomPatch_KeepsTheEditedInputAndItsListener()
    {
        RunNodeScript(FakeDom + """
const assert = require("assert");
const events = [];
const sent = [];
const dom = renderer(events, sent);
const app = fakeDocument.createElement("div");
const text = (value) => ({ type: "text", props: { value } });
let tree = { type: "column", props: {}, children: [{ type: "textField", props: { value: "a" } }, text("x")] };
dom.renderInto(app, tree);
const input = app.firstChild.childNodes[0];
input.value = "typed"; // the user is typing

// A patch elsewhere leaves the input alone.
tree = dom.applyPayload(app, tree, { patches: [{ op: "SetProp", path: "/1/", prop: "value", value: "y" }] });
assert.strictEqual(app.firstChild.childNodes[0], input);
assert.strictEqual(input.value, "typed");
assert.strictEqual(app.firstChild.childNodes[1].nodeValue, "y");

// An insert before it moves it: same element, new path, and its listener reports the new path.
tree = dom.applyPayload(app, tree, { patches: [{ op: "InsertChild", path: "/", index: 0, value: text("new") }] });
assert.strictEqual(app.firstChild.childNodes[1], input);
assert.strictEqual(input.getAttribute("data-ui-path"), "/1/");
input.fire("input");
assert.deepStrictEqual(events, ["input /1/"]);

// Another prop of the input keeps what the user typed.
input.value = "typed again";
tree = dom.applyPayload(app, tree, { patches: [{ op: "SetProp", path: "/1/", prop: "className", value: "wide" }] });
assert.strictEqual(input.className, "wide");
assert.strictEqual(input.value, "typed again");

// Its own value from the server, answering the input event, lands on the same element.
tree = dom.applyPayload(app, tree, { patches: [{ op: "SetProp", path: "/1/", prop: "value", value: "b" }], answersEventId: sent[0].id });
assert.strictEqual(app.firstChild.childNodes[1], input);
assert.strictEqual(input.value, "b");

// A patch the DOM cannot follow falls back to a full render of the patched tree.
tree = dom.applyPayload(app, tree, { patches: [{ op: "MoveChild", path: "/", from: 7, index: 0 }] });
assert.strictEqual(serialize(app.firstChild), serialize((() => { const c = fakeDocument.createElement("div"); dom.renderInto(c, tree); return c.firstChild; })()));
""");
    }

    [Fact]
    public void DomInput_CoalescesKeystrokes_AndSendsThemBeforeOtherEvents()
    {
        RunNodeScript(FakeDom + """
const assert = require("assert");
const events = [];
const sent = [];
const clock = manualClock();
const dom = renderer(events, sent, clock);
const app = fakeDocument.createElement("div");
const tree = { type: "column", props: {}, children: [
  { type: "textField", key: "title", props: { value: "" } },
  { type: "button", props: { label: "Save" } },
  { type: "textField", props: { value: "", inputDebounceMs: 0 } }
] };
dom.renderInto(app, tree);
const [input, button, instant] = app.firstChild.childNodes;
assert.strictEqual(input.getAttribute("inputDebounceMs"), null);

// Three keystrokes inside 150 ms: one event, sent 150 ms after the last, with the last value.
for (const value of ["a", "ab", "abc"]) {
  input.value = value;
  input.fire("input");
  clock.advance(100);
}
assert.deepStrictEqual(events, []);
clock.advance(50);
assert.deepStrictEqual(events, ["input /0/"]);
assert.strictEqual(sent[0].payload.value, "abc");
assert.deepStrictEqual(sent[0].target, { key: "title", type: "textField" });

// A click while an input waits sends the input first.
input.value = "abcd";
input.fire("input");
button.fire("click");
assert.deepStrictEqual(events, ["input /0/", "input /0/", "click /1/"]);
assert.strictEqual(sent[1].payload.value, "abcd");
assert.deepStrictEqual(sent[2].target, { key: null, type: "button" });
clock.advance(1000);
assert.strictEqual(events.length, 3);

// inputDebounceMs: 0 sends every keystroke.
instant.fire("input");
instant.fire("input");
assert.deepStrictEqual(events.slice(3), ["input /2/", "input /2/"]);
""");
    }

    [Fact]
    public void DomInput_HoldsBackAServerValueThatPredatesTheTyping()
    {
        RunNodeScript(FakeDom + """
const assert = require("assert");
const events = [];
const sent = [];
const clock = manualClock();
const dom = renderer(events, sent, clock);
const app = fakeDocument.createElement("div");
let tree = { type: "column", props: {}, children: [{ type: "textField", props: { value: "" } }] };
dom.renderInto(app, tree);
const input = app.firstChild.childNodes[0];
const setValue = (value) => ({ op: "SetProp", path: "/0/", prop: "value", value });

// Typing not sent yet: a server value would roll it back, so it waits.
input.value = "ab";
input.fire("input");
tree = dom.applyPayload(app, tree, { patches: [setValue("a")] });
assert.strictEqual(input.value, "ab");

// Sent but not answered: still held back.
clock.advance(150);
const typed = sent[0].id;
tree = dom.applyPayload(app, tree, { patches: [setValue("a")], answersEventId: "someone-else" });
assert.strictEqual(input.value, "ab");

// The render that answers the typing applies, and so does a held-back value once answered.
tree = dom.applyPayload(app, tree, { patches: [setValue("AB")], answersEventId: typed });
assert.strictEqual(input.value, "AB");
input.value = "ABC";
input.fire("input");
clock.advance(150);
tree = dom.applyPayload(app, tree, { patches: [setValue("trimmed")] });
assert.strictEqual(input.value, "ABC");
tree = dom.applyPayload(app, tree, { patches: [], answersEventId: sent[1].id });
assert.strictEqual(input.value, "trimmed");

// With nothing typed, a server value lands at once.
tree = dom.applyPayload(app, tree, { patches: [setValue("server")] });
assert.strictEqual(input.value, "server");
""");
    }

    private static UiNode RandomNode(Random random, int depth)
    {
        if (depth >= 3 || random.Next(3) == 0)
            return new UiNode(Pick(random, LeafTypes), RandomProps(random), key: null);

        var children = new List<UiNode>();
        var count = random.Next(0, 6);
        var keying = random.Next(4); // 0-1 keyed, 2 unkeyed, 3 mixed or duplicate keys
        var keys = Keys.OrderBy(_ => random.Next()).ToList();
        for (var i = 0; i < count; i++)
        {
            var child = RandomNode(random, depth + 1);
            children.Add(WithKey(child, ChildKey(random, keying, keys, i)));
        }

        return new UiNode(Pick(random, ContainerTypes), RandomProps(random), children);
    }

    private static string? ChildKey(Random random, int keying, List<string> keys, int index) => keying switch
    {
        0 or 1 => keys[index],
        2 => null,
        _ => random.Next(3) == 0 ? null : Pick(random, Keys)
    };

    /// <summary>A copy of <paramref name="node"/> with some props and children changed.</summary>
    private static UiNode Edit(Random random, UiNode node, int depth)
    {
        if (random.Next(12) == 0)
            return RandomNode(random, depth);

        var props = new Dictionary<string, RuntimeValue>(node.Props, StringComparer.Ordinal);
        if (random.Next(3) == 0)
        {
            var extra = RandomProps(random);
            foreach (var name in props.Keys.ToList())
            {
                if (random.Next(3) == 0)
                    props.Remove(name);
            }
            foreach (var kv in extra)
                props[kv.Key] = kv.Value;
        }

        var children = node.Children.Select(child => Edit(random, child, depth + 1)).ToList();
        var keyed = children.Count > 0 && children.All(c => c.Key != null);
        var unusedKeys = Keys.Where(k => children.All(c => c.Key != k)).ToList();

        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (random.Next(4) == 0)
                children.RemoveAt(i);
        }

        var inserts = random.Next(0, 3);
        for (var i = 0; i < inserts && unusedKeys.Count > 0; i++)
        {
            var key = keyed ? unusedKeys[random.Next(unusedKeys.Count)] : null;
            unusedKeys.Remove(key ?? "");
            children.Insert(random.Next(children.Count + 1), WithKey(RandomNode(random, depth + 1), key));
        }

        switch (random.Next(4))
        {
            case 0 when children.Count > 1:
                var a = random.Next(children.Count);
                var b = random.Next(children.Count);
                (children[a], children[b]) = (children[b], children[a]);
                break;
            case 1:
                children = children.OrderBy(_ => random.Next()).ToList();
                break;
        }

        return new UiNode(node.Type, props, children, node.Key);
    }

    private static Dictionary<string, RuntimeValue> RandomProps(Random random)
    {
        var props = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
        if (random.Next(2) == 0)
            props["value"] = RuntimeValue.String("v" + random.Next(4));
        if (random.Next(3) == 0)
            props["className"] = RuntimeValue.String("c" + random.Next(3));
        if (random.Next(4) == 0)
            props["size"] = RuntimeValue.Integer(random.Next(3));
        if (random.Next(5) == 0)
            props["disabled"] = RuntimeValue.Boolean(random.Next(2) == 0);
        return props;
    }

    private static UiNode WithKey(UiNode node, string? key) => new(node.Type, node.Props, node.Children, key);

    private static string Pick(Random random, string[] values) => values[random.Next(values.Length)];

    // Node reads a UTF-8 byte order mark as part of the first token.
    private static readonly UTF8Encoding NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static string Truncate(string text) => text.Length <= 2000 ? text : text[..2000] + "...";

    /// <summary>
    /// A document with just what <c>MaldaUiDom</c> uses on the fuzzed control types, and a
    /// serializer for comparing two DOMs. Prepended to the Node scripts below.
    /// </summary>
    private const string FakeDom = """
const fs = require("fs");
const client = require(process.argv[2]);

class FakeNode {
  constructor() { this.childNodes = []; this.parentNode = null; }
  get firstChild() { return this.childNodes[0] || null; }
  appendChild(child) { return this.insertBefore(child, null); }
  insertBefore(child, ref) {
    if (child.parentNode) child.parentNode.removeChild(child);
    const index = ref ? this.childNodes.indexOf(ref) : this.childNodes.length;
    if (index < 0) throw new Error("insertBefore: reference is not a child");
    this.childNodes.splice(index, 0, child);
    child.parentNode = this;
    return child;
  }
  removeChild(child) {
    const index = this.childNodes.indexOf(child);
    if (index < 0) throw new Error("removeChild: not a child");
    this.childNodes.splice(index, 1);
    child.parentNode = null;
    return child;
  }
  replaceChild(next, old) { this.insertBefore(next, old); this.removeChild(old); return old; }
}

class FakeText extends FakeNode {
  constructor(text) { super(); this.nodeType = 3; this.nodeValue = text; }
}

class FakeElement extends FakeNode {
  constructor(tag) {
    super();
    this.nodeType = 1;
    this.tagName = tag.toUpperCase();
    this.attrs = new Map();
    this.listeners = {};
    this.dataset = {};
    this.style = { cssText: "", setProperty(name, value) { this.cssText += name + ": " + value + ";"; } };
    this.classList = { add: (name) => { this.className = (this.className + " " + name).trim(); } };
    if (["INPUT", "TEXTAREA", "SELECT"].includes(this.tagName)) this.value = "";
    if (this.tagName === "INPUT") { this.checked = false; this.type = "text"; }
  }
  get attributes() { return Array.from(this.attrs, ([name, value]) => ({ name, value })); }
  setAttribute(name, value) { this.attrs.set(name, String(value)); }
  getAttribute(name) { return this.attrs.has(name) ? this.attrs.get(name) : null; }
  hasAttribute(name) { return this.attrs.has(name); }
  removeAttribute(name) { this.attrs.delete(name); }
  get className() { return this.getAttribute("class") || ""; }
  set className(value) { this.setAttribute("class", value); }
  addEventListener(type, listener) { (this.listeners[type] = this.listeners[type] || []).push(listener); }
  fire(type) { for (const listener of this.listeners[type] || []) listener({ preventDefault() {} }); }
}

const fakeDocument = { createElement: (tag) => new FakeElement(tag), createTextNode: (text) => new FakeText(text) };

function serialize(node) {
  if (node.nodeType === 3) return JSON.stringify(node.nodeValue);
  const attrs = node.attributes.map((a) => a.name + "=" + JSON.stringify(a.value)).sort().join(" ");
  const state = node.tagName === "INPUT" ? " {" + node.type + " " + JSON.stringify(node.value) + " " + node.checked + "}"
    : "value" in node ? " {" + JSON.stringify(node.value) + "}" : "";
  const style = node.style.cssText ? " style=" + JSON.stringify(node.style.cssText) : "";
  return "<" + node.tagName + " " + attrs + style + state + ">" + node.childNodes.map(serialize).join("") + "</" + node.tagName + ">";
}

function elements(node, out = []) {
  if (node.nodeType === 1) out.push(node);
  node.childNodes.forEach((child) => elements(child, out));
  return out;
}

// A renderer whose UI events land in `events` as "type path", and in `sent` in full; each gets
// the id "e<N>". Timers fire at once unless `clock` (manualClock()) times them.
let nextEventId = 1;
function renderer(events, sent, clock) {
  return client.MaldaUiDom.create(fakeDocument, {
    sendEvent: (type, path, payload, target) => {
      const id = "e" + nextEventId++;
      events.push(type + " " + path);
      if (sent) sent.push({ id, type, path, payload, target });
      return id;
    },
    setTimeout: clock ? clock.setTimeout : (fn) => { fn(); return 0; },
    clearTimeout: clock ? clock.clearTimeout : () => {}
  });
}

function manualClock() {
  let now = 0;
  let nextId = 1;
  const timers = new Map();
  return {
    setTimeout: (fn, ms) => { timers.set(nextId, { at: now + ms, fn }); return nextId++; },
    clearTimeout: (id) => { timers.delete(id); },
    advance(ms) {
      now += ms;
      for (const [id, timer] of Array.from(timers)) {
        if (timer.at <= now) { timers.delete(id); timer.fn(); }
      }
    }
  };
}

// Fires click and input on every element under the container; the events sent, in order.
function listenerPaths(container, events) {
  events.length = 0;
  for (const el of elements(container).slice(1)) { el.fire("click"); el.fire("input"); }
  return events.join(", ");
}
""";

    private const string Harness = FakeDom + """
const cases = JSON.parse(fs.readFileSync(process.argv[3], "utf8"));
const results = cases.map((c) => {
  const events = [];
  const dom = renderer(events);
  const patched = fakeDocument.createElement("div");
  let tree = JSON.parse(JSON.stringify(c.previous));
  dom.renderInto(patched, tree);
  tree = dom.applyPayload(patched, tree, { patches: c.patches });
  const fresh = fakeDocument.createElement("div");
  dom.renderInto(fresh, JSON.parse(c.expected));
  return {
    tree: JSON.stringify(tree),
    patches: JSON.stringify(client.diff(c.previous, c.current)),
    hash: client.treeHash(tree),
    patchedDom: serialize(patched),
    freshDom: serialize(fresh),
    patchedEvents: listenerPaths(patched, events),
    freshEvents: listenerPaths(fresh, events)
  };
});
process.stdout.write(JSON.stringify(results));
""";

    /// <summary>Runs a Node script with the client path as its argument; skipped without Node.</summary>
    private static void RunNodeScript(string script)
    {
        var folder = Path.Combine(Path.GetTempPath(), "malda_ui_fuzz", "script_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var scriptPath = Path.Combine(folder, "script.js");
            File.WriteAllText(scriptPath, script, NoBom);
            var startInfo = new ProcessStartInfo
            {
                FileName = System.Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } node ? node : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(Path.Combine(PlanningPaths.RepoRoot, "MaldaLang.UIHost", "wwwroot", "malda-ui-client.js"));

            Process? process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Win32Exception)
            {
                return;
            }

            using (process!)
            {
                var stderr = process.StandardError.ReadToEndAsync();
                process.StandardOutput.ReadToEnd();
                Assert.True(process.WaitForExit(30_000), "Node script timed out.");
                Assert.True(process.ExitCode == 0, "Node script failed: " + Truncate(stderr.Result));
            }
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }

    /// <summary>Each case's tree and DOMs after the client applied the patches, or null without Node.</summary>
    private static List<JsonObject>? RunClientInNode(string casesJson)
    {
        var folder = Path.Combine(Path.GetTempPath(), "malda_ui_fuzz", "run_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var harnessPath = Path.Combine(folder, "harness.js");
            var casesPath = Path.Combine(folder, "cases.json");
            File.WriteAllText(harnessPath, Harness, NoBom);
            File.WriteAllText(casesPath, casesJson, NoBom);

            var clientPath = Path.Combine(PlanningPaths.RepoRoot, "MaldaLang.UIHost", "wwwroot", "malda-ui-client.js");
            var startInfo = new ProcessStartInfo
            {
                FileName = System.Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } node ? node : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(harnessPath);
            startInfo.ArgumentList.Add(clientPath);
            startInfo.ArgumentList.Add(casesPath);

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
                if (!process.WaitForExit(30_000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    Assert.Fail("malda-ui-client.js harness timed out.");
                }

                Assert.True(process.ExitCode == 0, "malda-ui-client.js harness failed: " + Truncate(stderr.Result));
                return JsonNode.Parse(stdout.Result)!.AsArray().Select(r => r!.AsObject()).ToList();
            }
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }
}
