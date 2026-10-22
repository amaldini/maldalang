// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json.Nodes;
using MaldaLang.Runtime.UI;
using BuiltInFunctions = MaldaLang.BuiltIns.BuiltInFunctions;

namespace MaldaLang.Tests.Fuzz;

/// <summary>
/// Applies UI patches to a JSON tree with the same rules as <c>MaldaUiCore.applyPatches</c> in
/// <c>MaldaLang.UIHost/wwwroot/malda-ui-client.js</c>, so a diff can be checked without Node:
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

    public static JsonNode? Apply(JsonNode? root, JsonArray patches)
    {
        foreach (var patch in patches)
        {
            if (patch is not JsonObject p || p["op"] is null)
                continue;

            var path = (string?)p["path"] ?? "/";
            switch ((string?)p["op"])
            {
                case "ReplaceNode":
                    if (path == "/")
                    {
                        root = p["value"]?.DeepClone();
                        break;
                    }
                    var segments = ParsePath(path);
                    var parent = NodeAt(root, segments.Take(segments.Count - 1));
                    if (parent?["children"] is JsonArray siblings && segments[^1] < siblings.Count)
                        siblings[segments[^1]] = p["value"]?.DeepClone();
                    break;
                case "SetProp":
                    if (NodeAt(root, ParsePath(path)) is JsonObject setTarget)
                    {
                        if (setTarget["props"] is not JsonObject props)
                            setTarget["props"] = props = new JsonObject();
                        props[(string)p["prop"]!] = p["value"]?.DeepClone();
                    }
                    break;
                case "RemoveProp":
                    if (NodeAt(root, ParsePath(path))?["props"] is JsonObject removeFrom)
                        removeFrom.Remove((string)p["prop"]!);
                    break;
                case "InsertChild":
                    if (NodeAt(root, ParsePath(path)) is JsonObject insertTarget)
                    {
                        if (insertTarget["children"] is not JsonArray children)
                            insertTarget["children"] = children = new JsonArray();
                        // Array.splice clamps the insert position.
                        var index = p["index"] is JsonValue i ? Math.Clamp((int)i, 0, children.Count) : children.Count;
                        children.Insert(index, p["value"]?.DeepClone());
                    }
                    break;
                case "RemoveChild":
                    if (NodeAt(root, ParsePath(path))?["children"] is JsonArray removeChildren
                        && p["index"] is JsonValue r && (int)r >= 0 && (int)r < removeChildren.Count)
                        removeChildren.RemoveAt((int)r);
                    break;
                case "MoveChild":
                    if (NodeAt(root, ParsePath(path))?["children"] is JsonArray moveChildren
                        && p["from"] is JsonValue from && p["index"] is JsonValue to
                        && (int)from >= 0 && (int)from < moveChildren.Count)
                    {
                        var moved = moveChildren[(int)from];
                        moveChildren.RemoveAt((int)from);
                        moveChildren.Insert(Math.Clamp((int)to, 0, moveChildren.Count), moved);
                    }
                    break;
            }
        }

        return root;
    }

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

    private static JsonNode? NodeAt(JsonNode? root, IEnumerable<int> segments)
    {
        var current = root;
        foreach (var index in segments)
        {
            if (current?["children"] is not JsonArray children || index < 0 || index >= children.Count)
                return null;
            current = children[index];
        }

        return current;
    }

    private static List<int> ParsePath(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
}
