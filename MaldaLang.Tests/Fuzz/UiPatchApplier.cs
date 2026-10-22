// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json.Nodes;
using MaldaLang.Runtime.UI;
using MaldaLang.UIHost;
using BuiltInFunctions = MaldaLang.BuiltIns.BuiltInFunctions;

namespace MaldaLang.Tests.Fuzz;

/// <summary>
/// Test helpers over the host's <see cref="UiTreePatcher"/>, which applies UI patches with the
/// same rules as <c>MaldaUiCore.applyPatches</c>, so a diff can be checked without Node:
/// applying <c>Diff(previous, current)</c> to <c>previous</c> must give <c>current</c>.
/// </summary>
internal static class UiPatchApplier
{
    public static JsonNode? ToJson(UiNode? node) =>
        node == null ? null : JsonNode.Parse(BuiltInFunctions.SerializeToJson(node.ToRuntimeValue()));

    public static JsonNode? ToJson(IEnumerable<UiPatch> patches) =>
        JsonNode.Parse("[" + string.Join(",", patches.Select(p => BuiltInFunctions.SerializeToJson(p.ToRuntimeValue()))) + "]");

    /// <summary>Applies <c>Diff(previous, current)</c> to <c>previous</c> and returns the canonical result.</summary>
    public static string DiffAndApply(UiNode? previous, UiNode? current)
    {
        var patches = UiDiffEngine.Diff(previous, current);
        return Canonical(Apply(ToJson(previous), ToJson(patches)!.AsArray()));
    }

    public static JsonNode? Apply(JsonNode? root, JsonArray patches) => UiTreePatcher.Apply(root, patches);

    /// <summary>JSON text with object keys sorted, so trees compare independently of prop order.</summary>
    public static string Canonical(JsonNode? node) => Sorted(node)?.ToJsonString() ?? "null";

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => KeyValuePair.Create(kv.Key, Sorted(kv.Value)))),
        JsonArray arr => new JsonArray(arr.Select(Sorted).ToArray()),
        null => null,
        _ => node.DeepClone()
    };
}
