// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json.Nodes;

namespace MaldaLang.UIHost;

/// <summary>
/// Applies UI patches to a JSON tree with the same rules as <c>MaldaUiCore.applyPatches</c> in
/// <c>wwwroot/malda-ui-client.js</c>. The host keeps each session's tree with it, so a client
/// that reconnects gets the whole tree; <c>UiPatchFuzzTests</c> checks both appliers agree.
/// </summary>
public static class UiTreePatcher
{
    /// <summary>The tree after <paramref name="patches"/>; <paramref name="root"/> is changed in place.</summary>
    public static JsonNode? Apply(JsonNode? root, JsonArray patches)
    {
        foreach (var patch in patches)
        {
            if (patch is not JsonObject p || p["op"] is null)
                continue;

            var path = (string?)p["path"] ?? "/";
            if (!TryParsePath(path, out var segments))
                continue;

            switch ((string?)p["op"])
            {
                case "ReplaceNode":
                    if (segments.Count == 0)
                    {
                        root = p["value"]?.DeepClone();
                        break;
                    }
                    var parent = NodeAt(root, segments.Take(segments.Count - 1));
                    if (parent?["children"] is JsonArray siblings && segments[^1] < siblings.Count)
                        siblings[segments[^1]] = p["value"]?.DeepClone();
                    break;
                case "SetProp":
                    if (NodeAt(root, segments) is JsonObject setTarget && p["prop"] is JsonValue setName)
                    {
                        if (setTarget["props"] is not JsonObject props)
                            setTarget["props"] = props = new JsonObject();
                        props[(string)setName!] = p["value"]?.DeepClone();
                    }
                    break;
                case "RemoveProp":
                    if (NodeAt(root, segments)?["props"] is JsonObject removeFrom && p["prop"] is JsonValue removeName)
                        removeFrom.Remove((string)removeName!);
                    break;
                case "InsertChild":
                    if (NodeAt(root, segments) is JsonObject insertTarget)
                    {
                        if (insertTarget["children"] is not JsonArray children)
                            insertTarget["children"] = children = new JsonArray();
                        // Array.splice clamps the insert position.
                        var index = p["index"] is JsonValue i ? Math.Clamp((int)i, 0, children.Count) : children.Count;
                        children.Insert(index, p["value"]?.DeepClone());
                    }
                    break;
                case "RemoveChild":
                    if (NodeAt(root, segments)?["children"] is JsonArray removeChildren
                        && p["index"] is JsonValue r && (int)r >= 0 && (int)r < removeChildren.Count)
                        removeChildren.RemoveAt((int)r);
                    break;
                case "MoveChild":
                    if (NodeAt(root, segments)?["children"] is JsonArray moveChildren
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

    private static bool TryParsePath(string path, out List<int> segments)
    {
        segments = new List<int>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out var index))
                return false;
            segments.Add(index);
        }

        return true;
    }
}
