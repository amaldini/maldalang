// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.UI;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests.Fuzz;

/// <summary>
/// Server/client UI protocol fuzzing: a seeded generator builds a tree and an edited copy
/// (props changed, children inserted, removed, swapped or shuffled, keyed and unkeyed). The
/// patches from <see cref="UiDiffEngine.Diff"/>, applied to the first tree by
/// <see cref="UiPatchApplier"/> and by <c>MaldaUiCore</c> from <c>malda-ui-client.js</c> under
/// Node, must give the second tree. Set <c>MALDA_UI_FUZZ_SEEDS</c> (e.g. <c>1-200</c>) to
/// explore more locally; a failure writes the trees and patches to
/// <c>%TEMP%/malda_ui_fuzz/seed&lt;N&gt;/</c>. Without Node only the C# applier runs.
/// </summary>
public class UiPatchFuzzTests
{
    private const int CasesPerSeed = 40;

    private static readonly string[] ContainerTypes = { "column", "row", "panel" };
    private static readonly string[] LeafTypes = { "text", "button", "badge" };
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

            cases.Add(new JsonObject
            {
                ["previous"] = UiPatchApplier.ToJson(previous),
                ["patches"] = patches.DeepClone(),
                ["expected"] = expected
            });
        }

        var casesJson = cases.ToJsonString();
        var clientResults = RunClientInNode(casesJson);
        if (clientResults != null)
        {
            for (var i = 0; i < CasesPerSeed; i++)
            {
                var expected = (string)cases[i]!["expected"]!;
                var actual = UiPatchApplier.Canonical(JsonNode.Parse(clientResults[i]));
                if (actual != expected)
                    failures.Add($"case {i} (malda-ui-client.js)");
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

    private const string Harness = """
const core = require(process.argv[2]);
const cases = JSON.parse(require("fs").readFileSync(process.argv[3], "utf8"));
const results = cases.map((c) => JSON.stringify(core.applyPatches(c.previous, c.patches)));
process.stdout.write(JSON.stringify(results));
""";

    /// <summary>Each case's tree after <c>MaldaUiCore.applyPatches</c>, or null without Node.</summary>
    private static List<string>? RunClientInNode(string casesJson)
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
                return JsonNode.Parse(stdout.Result)!.AsArray().Select(r => (string)r!).ToList();
            }
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }
}
