// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Text;
using System.Text.Json;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using ValueType = MaldaLang.Interpreter.ValueType;

internal static class MemoryReflectService
{
    internal sealed class ReflectedFact
    {
        public required string Fact { get; init; }
        public double Confidence { get; init; }
        public string? Category { get; init; }
    }
    
    public static List<ReflectedFact> Reflect(
        List<(string NodeId, DateTime Timestamp, JsonObject NodeObj)> episodics,
        JsonObject? options,
        Interpreter? interpreter)
    {
        var injected = TryReadInjectedFacts(options);
        if (injected != null)
            return injected;
        
        var model = GetStringOption(options, "model");
        var prompt = BuildPrompt(episodics);
        RuntimeValue response;
        var clientValue = options?.Get("client", null);
        if (clientValue != null && clientValue.Type == ValueType.Object)
        {
            response = CallClientComplete(clientValue.AsObject()!, prompt, interpreter);
        }
        else
        {
            var client = new OpenRouterClientInstance(model);
            response = client.CallMethod("complete", new List<RuntimeValue> { RuntimeValue.String(prompt) }, interpreter);
        }
        var text = ExtractResponseText(response);
        return ParseFactsJson(text);
    }

    internal static RuntimeValue CallClientComplete(ObjectInstance clientObject, string prompt, Interpreter? interpreter)
    {
        var effectiveInterpreter = interpreter ?? TranspiledBuiltinRuntime.GetOrCreateInterpreter();
        var callMethod = clientObject.GetType().GetMethod(
            "CallMethod",
            new[] { typeof(string), typeof(List<RuntimeValue>), typeof(Interpreter) });
        if (callMethod == null)
            throw new InvalidOperationException("Injected client does not expose CallMethod(name, args, interpreter).");
        var result = callMethod.Invoke(clientObject, new object[] { "complete", new List<RuntimeValue> { RuntimeValue.String(prompt) }, effectiveInterpreter });
        if (result is not RuntimeValue runtimeValue)
            throw new InvalidOperationException("Injected client complete() did not return RuntimeValue.");
        return runtimeValue;
    }
    
    private static string BuildPrompt(List<(string NodeId, DateTime Timestamp, JsonObject NodeObj)> episodics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are reflecting on episodic chat turns.");
        sb.AppendLine("Extract durable semantic facts. Reply with JSON only.");
        sb.AppendLine("Format: {\"facts\":[{\"fact\":\"...\",\"confidence\":0.0,\"category\":\"...\"}]}");
        sb.AppendLine("Keep confidence in [0,1].");
        sb.AppendLine();
        sb.AppendLine("Turns:");
        for (var i = 0; i < episodics.Count; i++)
        {
            var node = episodics[i].NodeObj;
            var fact = node.Get("fact", null);
            var context = node.Get("context", null);
            sb.Append("- Q: ");
            sb.AppendLine(fact != null && fact.Type == ValueType.String ? fact.AsString() : "");
            if (context != null && context.Type == ValueType.String && !string.IsNullOrWhiteSpace(context.AsString()))
            {
                sb.Append("  A: ");
                sb.AppendLine(context.AsString());
            }
        }
        
        return sb.ToString();
    }
    
    private static string ExtractResponseText(RuntimeValue response)
    {
        if (response.Type == ValueType.String)
            return response.AsString();
        if (response.Type == ValueType.Object && response.AsObject() is JsonObject obj)
        {
            var content = obj.Get("content", null);
            if (content != null && content.Type == ValueType.String)
                return content.AsString();
        }
        
        throw new InvalidOperationException("LLM reflect response did not contain text content.");
    }
    
    private static List<ReflectedFact> ParseFactsJson(string text)
    {
        var json = ExtractJsonObject(text);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("facts", out var facts) || facts.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Reflect JSON did not include facts array.");
        
        var result = new List<ReflectedFact>();
        foreach (var item in facts.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            if (!item.TryGetProperty("fact", out var factProp) || factProp.ValueKind != JsonValueKind.String)
                continue;
            var fact = factProp.GetString();
            if (string.IsNullOrWhiteSpace(fact))
                continue;
            var confidence = 0.0;
            if (item.TryGetProperty("confidence", out var confProp) && confProp.ValueKind == JsonValueKind.Number)
                confidence = Math.Clamp(confProp.GetDouble(), 0.0, 1.0);
            string? category = null;
            if (item.TryGetProperty("category", out var catProp) && catProp.ValueKind == JsonValueKind.String)
                category = catProp.GetString();
            result.Add(new ReflectedFact
            {
                Fact = fact.Trim(),
                Confidence = confidence,
                Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim()
            });
        }
        
        if (result.Count == 0)
            throw new InvalidOperationException("Reflect JSON contained no valid facts.");
        
        return result;
    }
    
    private static string ExtractJsonObject(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = trimmed.IndexOf('{');
            var lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
                return trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
        }
        
        return trimmed;
    }
    
    private static List<ReflectedFact>? TryReadInjectedFacts(JsonObject? options)
    {
        if (options == null)
            return null;
        var factsVal = options.Get("facts", null);
        if (factsVal == null || factsVal.Type != ValueType.Array)
            return null;
        
        var reflected = new List<ReflectedFact>();
        foreach (var entry in factsVal.AsArray())
        {
            if (entry.Type != ValueType.Object || entry.AsObject() is not JsonObject entryObj)
                continue;
            var factVal = entryObj.Get("fact", null);
            if (factVal == null || factVal.Type != ValueType.String || string.IsNullOrWhiteSpace(factVal.AsString()))
                continue;
            
            var confidence = 0.0;
            var confidenceVal = entryObj.Get("confidence", null);
            if (confidenceVal != null)
            {
                if (confidenceVal.Type == ValueType.Float)
                    confidence = Math.Clamp(confidenceVal.AsFloat(), 0.0, 1.0);
                else if (confidenceVal.Type == ValueType.Integer)
                    confidence = Math.Clamp(confidenceVal.AsInteger(), 0.0, 1.0);
            }
            
            string? category = null;
            var categoryVal = entryObj.Get("category", null);
            if (categoryVal != null && categoryVal.Type == ValueType.String && !string.IsNullOrWhiteSpace(categoryVal.AsString()))
                category = categoryVal.AsString();
            
            reflected.Add(new ReflectedFact
            {
                Fact = factVal.AsString().Trim(),
                Confidence = confidence,
                Category = category
            });
        }
        
        if (reflected.Count == 0)
            throw new InvalidOperationException("Injected reflect facts were provided but invalid.");
        return reflected;
    }
    
    private static string? GetStringOption(JsonObject? options, string key)
    {
        if (options == null)
            return null;
        var value = options.Get(key, null);
        if (value != null && value.Type == ValueType.String)
        {
            var text = value.AsString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        return null;
    }
}
