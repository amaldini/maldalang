// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInUiGenerate(List<RuntimeValue> args, Interpreter? interpreter)
    {
        return BuiltInUiGenerateAsync(args, interpreter).GetAwaiter().GetResult();
    }

    private static async Task<RuntimeValue> BuiltInUiGenerateAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 1 || args.Count > 3 || args[0].Type != ValueType.String)
            throw new Exception("uiGenerate() expects description and optional agent/cache arguments");

        var description = args[0].AsString();
        HTMLCacheInstance? cache = null;
        AgentInstance? agent = null;

        if (args.Count >= 2)
        {
            if (args[1].Type != ValueType.Object)
                throw new Exception("uiGenerate() optional argument #2 must be an Agent or HTMLCache object");

            var objectArg = args[1].AsObject();
            if (objectArg is AgentInstance agentInstance)
                agent = agentInstance;
            else if (objectArg is HTMLCacheInstance cacheInstance)
                cache = cacheInstance;
            else
                throw new Exception("uiGenerate() optional argument #2 must be an Agent or HTMLCache object");
        }

        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.Object)
                throw new Exception("uiGenerate() optional argument #3 must be an Agent or HTMLCache object");

            var objectArg = args[2].AsObject();
            if (objectArg is AgentInstance agentInstance)
            {
                if (agent != null)
                    throw new Exception("uiGenerate() received multiple Agent arguments");
                agent = agentInstance;
            }
            else if (objectArg is HTMLCacheInstance cacheInstance)
            {
                if (cache != null)
                    throw new Exception("uiGenerate() received multiple HTMLCache arguments");
                cache = cacheInstance;
            }
            else
            {
                throw new Exception("uiGenerate() optional argument #3 must be an Agent or HTMLCache object");
            }
        }

        var cacheKey = "ui:" + description;
        if (cache != null)
        {
            var cached = cache.CallMethod("get", new List<RuntimeValue> { RuntimeValue.String(cacheKey) });
            if (cached.Type == ValueType.String)
            {
                var cachedTree = BuiltInParseJSON(new List<RuntimeValue> { cached });
                UiNode.FromRuntimeValue(cachedTree);
                return cachedTree;
            }
        }

        if (agent == null)
        {
            var defaultLlama = DefaultLocalLlm.GetDefaultLocalClient();
            agent = new AgentInstance();
            agent.Initialize(
                "UiTreeGenerator",
                "UI tree designer",
                "You output valid MALDA server UI trees as JSON only.",
                null,
                defaultLlama,
                null,
                null
            );
        }

        var prompt = BuildUiGeneratePrompt(description);
        var response = await Task.Run(() => agent.Think(RuntimeValue.String(prompt)));
        var responseText = ExtractAgentContentAsString(response);
        var jsonPayload = ExtractJsonObjectPayload(responseText);
        RuntimeValue parsedTree;
        try
        {
            parsedTree = BuiltInParseJSON(new List<RuntimeValue> { RuntimeValue.String(jsonPayload) });
        }
        catch (Exception ex)
        {
            throw new Exception("ui.generate could not parse agent response as JSON: " + ex.Message);
        }

        UiNode.FromRuntimeValue(parsedTree);

        if (cache != null)
        {
            var serialized = BuiltInToJSON(new List<RuntimeValue> { parsedTree });
            if (serialized.Type == ValueType.String)
            {
                cache.CallMethod("set", new List<RuntimeValue>
                {
                    RuntimeValue.String(cacheKey),
                    serialized
                });
            }
        }

        return parsedTree;
    }

    private static string BuildUiGeneratePrompt(string description)
    {
        return string.Join(
            "\n",
            "Generate a MALDA server UI tree as JSON.",
            "Output requirements:",
            "- Return ONLY one JSON object.",
            "- Root and every child must be: {\"type\": string, \"props\": object, \"children\": array}.",
            "- Optional \"key\" is allowed.",
            "- Do not include markdown, comments, or explanations.",
            "- Use only these control types:",
            "  row,column,stack,spacer,panel,drawer,tabs,accordion,breadcrumbs,text,heading,image,icon,button,textField,textArea,checkbox,switch,select,radioGroup,slider,form,field,list,table,dataGrid,treeView,paginator,emptyState,badge,alert,progress,modal,toast,skeleton,spinner,errorBoundary,slot,when,choose,each",
            "- Prefer event names supported by MALDA: onClick,onChange,onInput,onSubmit,onClose,onFocus,onBlur,onRowClick,onSelectionChange,onSort,onFilter,onPageChange,onViewportChange,onNodeSelect,onNodeToggle,onNodeExpand,onNodeCollapse,onNodeActivate,onLoadChildren,onDragStart,onDragOver,onDrop,onDragEnd.",
            "",
            "Requested UI:",
            description
        );
    }

    private static string ExtractAgentContentAsString(RuntimeValue response)
    {
        if (response.Type == ValueType.String)
        {
            return response.AsString();
        }

        if (response.Type == ValueType.Object)
        {
            var responseObject = response.AsObject();
            if (responseObject is JsonObject jsonObj)
            {
                var content = jsonObj.Get("content", null);
                if (content.Type == ValueType.String)
                {
                    return content.AsString();
                }
            }
            else
            {
                try
                {
                    var content = responseObject.Get("content", null);
                    if (content.Type == ValueType.String)
                    {
                        return content.AsString();
                    }
                }
                catch
                {
                    // Fall back to string conversion below.
                }
            }
        }

        return response.ToString();
    }

    private static string ExtractJsonObjectPayload(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new Exception("ui.generate received an empty agent response");
        }

        var codeFencePattern = @"```json\s*(.*?)\s*```";
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            codeFencePattern,
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return trimmed;
        }

        throw new Exception("ui.generate expected a JSON object response");
    }

    private static string ResolveSessionId(RuntimeValue source)
    {
        if (source.Type == ValueType.String)
        {
            return source.AsString();
        }

        var sourceObj = CoerceRuntimeObjectToJsonObject(source);
        if (sourceObj == null)
        {
            return string.Empty;
        }

        var directSession = GetStringValueFromObject(sourceObj, "session", "");
        if (!string.IsNullOrWhiteSpace(directSession))
        {
            return directSession;
        }

        var queryValue = sourceObj.Get("query", null);
        if (queryValue.Type == ValueType.Object && queryValue.AsObject() is JsonObject queryObject)
        {
            var querySession = GetStringValueFromObject(queryObject, "session", "");
            if (!string.IsNullOrWhiteSpace(querySession))
            {
                return querySession;
            }
        }

        var bodyValue = sourceObj.Get("body", null);
        if (bodyValue.Type == ValueType.Object && bodyValue.AsObject() is JsonObject bodyObject)
        {
            var bodySession = GetStringValueFromObject(bodyObject, "session", "");
            if (!string.IsNullOrWhiteSpace(bodySession))
            {
                return bodySession;
            }
        }

        return string.Empty;
    }
    
    private static async Task<RuntimeValue> BuiltInGenerateUIAsync(
        List<RuntimeValue> args, 
        Interpreter interpreter)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("generateUI() expects at least 1 string argument");
        
        var description = args[0].AsString();
        HTMLCacheInstance? cache = null;
        AgentInstance? agent = null;
        
        // Parse optional arguments
        if (args.Count >= 2 && args[1].Type == ValueType.Object)
        {
            var cacheObj = args[1].AsObject();
            if (cacheObj is HTMLCacheInstance cacheInst)
                cache = cacheInst;
        }
        
        if (args.Count >= 3 && args[2].Type == ValueType.Object)
        {
            var agentObj = args[2].AsObject();
            if (agentObj is AgentInstance agentInst)
                agent = agentInst;
        }
        
        // Check cache first
        if (cache != null)
        {
            var cached = cache.CallMethod("get", new List<RuntimeValue> { RuntimeValue.String(description) });
            if (cached.Type != ValueType.Null)
                return cached;
        }
        
        // Generate with agent (or create default)
        if (agent == null)
        {
            var defaultLlama = DefaultLocalLlm.GetDefaultLocalClient();
            agent = new AgentInstance();
            agent.Initialize(
                "UIGenerator",
                "UI designer",
                "You create beautiful, functional HTML forms. Always include proper form submission to /submit endpoint with POST method. Use modern CSS styling.",
                null,
                defaultLlama,
                null,
                null
            );
        }
        
        var response = agent.Think(RuntimeValue.String($"Create an HTML form: {description}"));
        
        // Extract content from response
        RuntimeValue contentValue;
        if (response.Type == ValueType.Object)
        {
            var responseObj = response.AsObject();
            if (responseObj is JsonObject jsonObj)
            {
                contentValue = jsonObj.Get("content", null) ?? RuntimeValue.String("");
            }
            else
            {
                // Try to get content property
                try
                {
                    contentValue = responseObj.Get("content", null) ?? RuntimeValue.String("");
                }
                catch
                {
                    contentValue = RuntimeValue.String("");
                }
            }
        }
        else
        {
            contentValue = RuntimeValue.String("");
        }
        
        var html = BuiltInExtractHTML(new List<RuntimeValue> { contentValue });
        
        // Cache if cache provided
        if (cache != null && html.Type == ValueType.String)
            cache.CallMethod("set", new List<RuntimeValue> { RuntimeValue.String(description), html });
        
        return html;
    }
}
