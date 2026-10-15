// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using ValueType = MaldaLang.Interpreter.ValueType;
using System.Text.Json;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;

public partial class GraphMemoryInstance
{
    /// <summary>
    /// Normalize tags from array or CSV string: trim, lowercase, dedupe, drop empties.
    /// </summary>
    private static List<string> NormalizeTagsValue(RuntimeValue? tagsVal)
    {
        var result = new List<string>();
        if (tagsVal == null || tagsVal.Type == ValueType.Null)
            return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddOne(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;
            var tag = raw.Trim().ToLowerInvariant();
            if (seen.Add(tag))
                result.Add(tag);
        }

        if (tagsVal.Type == ValueType.Array)
        {
            foreach (var item in tagsVal.AsArray())
            {
                if (item.Type == ValueType.String)
                    AddOne(item.AsString());
            }
        }
        else if (tagsVal.Type == ValueType.String)
        {
            foreach (var part in tagsVal.AsString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                AddOne(part);
        }

        return result;
    }

    private static List<string> GetNodeTags(JsonObject nodeObj) =>
        NormalizeTagsValue(nodeObj.Get("tags", null));

    private static bool NodeMatchesTags(JsonObject nodeObj, HashSet<string>? tagsFilter, string tagsMode)
    {
        if (tagsFilter == null || tagsFilter.Count == 0)
            return true;

        var nodeTags = GetNodeTags(nodeObj);
        if (nodeTags.Count == 0)
            return false;

        var nodeSet = new HashSet<string>(nodeTags, StringComparer.OrdinalIgnoreCase);
        if (string.Equals(tagsMode, "all", StringComparison.OrdinalIgnoreCase))
            return tagsFilter.All(t => nodeSet.Contains(t));

        // default: any
        return tagsFilter.Any(t => nodeSet.Contains(t));
    }

    private string BuildNodeDescription(RuntimeValue fact, RuntimeValue? context)
    {
        var description = new System.Text.StringBuilder();
        
        if (fact.Type == ValueType.String)
        {
            description.Append(fact.AsString());
        }
        else if (fact.Type == ValueType.Object)
        {
            // Try to extract text from object
            if (fact.AsObject() is JsonObject jsonObj)
            {
                var type = jsonObj.Get("type");
                var name = jsonObj.Get("name");
                var desc = jsonObj.Get("description");
                
                if (type.Type == ValueType.String)
                    description.Append(type.AsString()).Append(" ");
                if (name.Type == ValueType.String)
                    description.Append(name.AsString()).Append(" ");
                if (desc.Type == ValueType.String)
                    description.Append(desc.AsString());
            }
        }
        
        if (context != null && context.Type == ValueType.String)
        {
            description.Append(" ").Append(context.AsString());
        }
        
        return description.ToString();
    }
    
    private static void ApplyMetadataFields(JsonObject nodeData, JsonObject metadataObj)
    {
        foreach (var field in new[] { "phase", "type", "source", "scope", "category" })
        {
            var val = metadataObj.Get(field, null);
            if (val != null && val.Type != ValueType.Null)
                nodeData.Set(field, val);
        }

        var tags = NormalizeTagsValue(metadataObj.Get("tags", null));
        if (tags.Count > 0)
            nodeData.Set("tags", RuntimeValue.Array(tags.Select(RuntimeValue.String).ToList()));
        
        var iterationVal = metadataObj.Get("iteration", null);
        if (iterationVal != null && iterationVal.Type != ValueType.Null)
            nodeData.Set("iteration", iterationVal);
        
        var consolidatedVal = metadataObj.Get("consolidated", null);
        if (consolidatedVal != null && consolidatedVal.Type != ValueType.Null)
            nodeData.Set("consolidated", consolidatedVal);
        
        var confidenceVal = metadataObj.Get("confidence", null);
        if (confidenceVal != null && confidenceVal.Type != ValueType.Null)
            nodeData.Set("confidence", confidenceVal);
        
        var importanceVal = metadataObj.Get("importance", null);
        if (importanceVal != null && importanceVal.Type != ValueType.Null)
            nodeData.Set("importance", importanceVal);
        
        var accessCountVal = metadataObj.Get("accessCount", null);
        if (accessCountVal != null && accessCountVal.Type != ValueType.Null)
            nodeData.Set("accessCount", accessCountVal);
        
        var lastAccessedVal = metadataObj.Get("lastAccessed", null);
        if (lastAccessedVal != null && lastAccessedVal.Type != ValueType.Null)
            nodeData.Set("lastAccessed", lastAccessedVal);
        
        foreach (var field in new[] { "filePath", "fileHash" })
        {
            var val = metadataObj.Get(field, null);
            if (val != null && val.Type != ValueType.Null)
                nodeData.Set(field, val);
        }
    }
    
    private static void AppendMetadataToDescription(System.Text.StringBuilder description, JsonObject metadataObj)
    {
        foreach (var field in new[] { "phase", "type", "source", "scope", "category" })
        {
            var val = metadataObj.Get(field, null);
            if (val != null && val.Type == ValueType.String && !string.IsNullOrWhiteSpace(val.AsString()))
                description.Append(' ').Append(val.AsString());
        }

        foreach (var tag in NormalizeTagsValue(metadataObj.Get("tags", null)))
            description.Append(' ').Append(tag);
        
        var iterationVal = metadataObj.Get("iteration", null);
        if (iterationVal != null && (iterationVal.Type == ValueType.Integer || iterationVal.Type == ValueType.String))
            description.Append(" iteration:").Append(iterationVal.ToString());
        
        var confidenceVal = metadataObj.Get("confidence", null);
        if (confidenceVal != null && (confidenceVal.Type == ValueType.Float || confidenceVal.Type == ValueType.Integer))
            description.Append(" confidence:").Append(confidenceVal.ToString());
    }

    private static bool MatchesMemoryFilters(
        RuntimeValue nodeValue,
        string? phaseFilter,
        string? typeFilter,
        string? scopeFilter = null,
        HashSet<string>? scopeHierarchy = null,
        string? excludeTypeFilter = null,
        HashSet<string>? includeTypesFilter = null,
        HashSet<string>? tagsFilter = null,
        string tagsMode = "any",
        QueryDiagnosticsState? diagnostics = null)
    {
        if (nodeValue.Type != ValueType.Object || nodeValue.AsObject() is not JsonObject nodeObj)
            return true;
        
        if (phaseFilter != null)
        {
            var phaseVal = nodeObj.Get("phase", null);
            if (phaseVal == null || phaseVal.Type != ValueType.String ||
                !string.Equals(phaseVal.AsString(), phaseFilter, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics?.NoteDrop("type_or_phase_filter");
                return false;
            }
        }
        
        if (typeFilter != null)
        {
            var typeVal = nodeObj.Get("type", null);
            if (typeVal == null || typeVal.Type != ValueType.String ||
                !string.Equals(typeVal.AsString(), typeFilter, StringComparison.OrdinalIgnoreCase))
            {
                if (diagnostics != null)
                    diagnostics.DroppedByTypeFilter++;
                return false;
            }
        }
        
        if (includeTypesFilter != null && includeTypesFilter.Count > 0)
        {
            var includeTypeVal = nodeObj.Get("type", null);
            var includeType = includeTypeVal != null && includeTypeVal.Type == ValueType.String
                ? includeTypeVal.AsString()
                : "";
            if (!includeTypesFilter.Contains(includeType))
            {
                if (diagnostics != null)
                    diagnostics.DroppedByTypeFilter++;
                return false;
            }
        }
        
        if (excludeTypeFilter != null)
        {
            var typeVal = nodeObj.Get("type", null);
            if (typeVal != null && typeVal.Type == ValueType.String &&
                string.Equals(typeVal.AsString(), excludeTypeFilter, StringComparison.OrdinalIgnoreCase))
            {
                if (diagnostics != null)
                    diagnostics.DroppedByTypeFilter++;
                return false;
            }
        }
        
        if (scopeHierarchy != null && scopeHierarchy.Count > 0)
        {
            var nodeScope = GetNodeScope(nodeObj);
            if (!scopeHierarchy.Contains(nodeScope))
                return false;
        }
        else if (scopeFilter != null)
        {
            var nodeScope = GetNodeScope(nodeObj);
            if (!string.Equals(nodeScope, scopeFilter, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(nodeScope, "global", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!NodeMatchesTags(nodeObj, tagsFilter, tagsMode))
        {
            if (diagnostics != null)
                diagnostics.DroppedByTagFilter++;
            return false;
        }
        
        return true;
    }
}
