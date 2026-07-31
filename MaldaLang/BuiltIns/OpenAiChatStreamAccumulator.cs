// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Text;
using System.Text.Json;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;

/// <summary>
/// Accumulates OpenAI-style chat/completions SSE chunks into a single assistant message object.
/// </summary>
internal sealed class OpenAiChatStreamAccumulator
{
    private readonly StringBuilder _content = new();
    private readonly StringBuilder _reasoning = new();
    private readonly Dictionary<int, ToolCallBuilder> _toolCalls = new();
    private int? _promptTokens;
    private int? _completionTokens;
    private int? _totalTokens;
    private double? _cost;

    public Action<LlmStreamDelta>? OnDelta { get; set; }

    public void ProcessSseDataLine(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[DONE]")
            return;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorEl))
        {
            var errorMessage = errorEl.ValueKind == JsonValueKind.Object &&
                          errorEl.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString()
                : errorEl.GetRawText();
            throw new InvalidOperationException(errorMessage ?? "Streaming API error");
        }

        if (root.TryGetProperty("usage", out var usageElem) && usageElem.ValueKind == JsonValueKind.Object)
        {
            if (usageElem.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var ptVal))
                _promptTokens = ptVal;
            if (usageElem.TryGetProperty("completion_tokens", out var ct) && ct.TryGetInt32(out var ctVal))
                _completionTokens = ctVal;
            if (usageElem.TryGetProperty("total_tokens", out var tt) && tt.TryGetInt32(out var ttVal))
                _totalTokens = ttVal;
            if (usageElem.TryGetProperty("cost", out var costElem) && costElem.TryGetDouble(out var costVal))
                _cost = costVal;
            else if (usageElem.TryGetProperty("total_cost", out var totalCostElem) && totalCostElem.TryGetDouble(out var totalCostVal))
                _cost = totalCostVal;
        }

        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return;
        }

        var choice = choices[0];

        if (choice.TryGetProperty("delta", out var delta))
        {
            AppendDelta(delta);
            return;
        }

        // Some providers emit a final non-delta message chunk.
        if (choice.TryGetProperty("message", out var message))
            AppendMessage(message);
    }

    private void AppendDelta(JsonElement delta)
    {
        if (delta.TryGetProperty("content", out var contentEl) &&
            contentEl.ValueKind == JsonValueKind.String)
        {
            var piece = contentEl.GetString();
            if (!string.IsNullOrEmpty(piece))
            {
                _content.Append(piece);
                OnDelta?.Invoke(new LlmStreamDelta("content", piece));
            }
        }

        foreach (var propName in new[] { "reasoning", "reasoning_content" })
        {
            if (!delta.TryGetProperty(propName, out var reasoningEl) ||
                reasoningEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var piece = reasoningEl.GetString();
            if (string.IsNullOrEmpty(piece))
                continue;

            _reasoning.Append(piece);
            OnDelta?.Invoke(new LlmStreamDelta("reasoning", piece));
        }

        if (delta.TryGetProperty("tool_calls", out var toolCallsEl) &&
            toolCallsEl.ValueKind == JsonValueKind.Array)
        {
            AppendToolCallDeltas(toolCallsEl);
        }
    }

    private void AppendMessage(JsonElement message)
    {
        if (message.TryGetProperty("content", out var contentEl) &&
            contentEl.ValueKind == JsonValueKind.String)
        {
            var piece = contentEl.GetString();
            if (!string.IsNullOrEmpty(piece) && _content.Length == 0)
            {
                _content.Append(piece);
                OnDelta?.Invoke(new LlmStreamDelta("content", piece));
            }
        }

        var reasoning = LLMClientInstance.ExtractReasoningFromMessage(message);
        if (!string.IsNullOrWhiteSpace(reasoning) && _reasoning.Length == 0)
        {
            _reasoning.Append(reasoning);
            OnDelta?.Invoke(new LlmStreamDelta("reasoning", reasoning));
        }

        if (message.TryGetProperty("tool_calls", out var toolCallsEl) &&
            toolCallsEl.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var tc in toolCallsEl.EnumerateArray())
            {
                ApplyCompleteToolCall(index, tc);
                index++;
            }
        }
    }

    private void AppendToolCallDeltas(JsonElement toolCallsEl)
    {
        foreach (var tc in toolCallsEl.EnumerateArray())
        {
            if (!tc.TryGetProperty("index", out var indexEl) ||
                indexEl.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var index = indexEl.GetInt32();
            if (!_toolCalls.TryGetValue(index, out var builder))
            {
                builder = new ToolCallBuilder();
                _toolCalls[index] = builder;
            }

            if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            {
                var id = idEl.GetString();
                if (!string.IsNullOrEmpty(id))
                    builder.Id = id;
            }

            if (tc.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
            {
                var type = typeEl.GetString();
                if (!string.IsNullOrEmpty(type))
                    builder.Type = type;
            }

            if (!tc.TryGetProperty("function", out var funcEl) ||
                funcEl.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (funcEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                var name = nameEl.GetString();
                if (!string.IsNullOrEmpty(name))
                    builder.Name = name;
            }

            if (funcEl.TryGetProperty("arguments", out var argsEl) &&
                argsEl.ValueKind == JsonValueKind.String)
            {
                var argsPiece = argsEl.GetString();
                if (!string.IsNullOrEmpty(argsPiece))
                    builder.Arguments.Append(argsPiece);
            }
        }
    }

    private void ApplyCompleteToolCall(int index, JsonElement tc)
    {
        if (!_toolCalls.TryGetValue(index, out var builder))
        {
            builder = new ToolCallBuilder();
            _toolCalls[index] = builder;
        }

        if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            var id = idEl.GetString();
            if (!string.IsNullOrEmpty(id))
                builder.Id = id;
        }

        if (tc.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            var type = typeEl.GetString();
            if (!string.IsNullOrEmpty(type))
                builder.Type = type;
        }

        if (!tc.TryGetProperty("function", out var funcEl) ||
            funcEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (funcEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
        {
            var name = nameEl.GetString();
            if (!string.IsNullOrEmpty(name))
                builder.Name = name;
        }

        if (funcEl.TryGetProperty("arguments", out var argsEl) &&
            argsEl.ValueKind == JsonValueKind.String)
        {
            var args = argsEl.GetString() ?? "";
            if (builder.Arguments.Length == 0)
                builder.Arguments.Append(args);
        }
    }

    public JsonObject ToResultObject()
    {
        var resultObj = new JsonObject();

        if (_content.Length > 0)
            resultObj.Set("content", RuntimeValue.String(_content.ToString()));
        else
            resultObj.Set("content", RuntimeValue.Null());

        if (_reasoning.Length > 0)
            resultObj.Set("reasoning", RuntimeValue.String(_reasoning.ToString()));

        if (_toolCalls.Count > 0)
        {
            var toolCallsArray = new List<RuntimeValue>();
            foreach (var kvp in _toolCalls.OrderBy(p => p.Key))
            {
                var builder = kvp.Value;
                if (string.IsNullOrEmpty(builder.Id))
                    continue;

                var tcObj = new JsonObject();
                tcObj.Set("id", RuntimeValue.String(builder.Id));
                tcObj.Set("type", RuntimeValue.String(builder.Type ?? "function"));

                var funcObj = new JsonObject();
                funcObj.Set("name", RuntimeValue.String(builder.Name ?? ""));
                funcObj.Set("arguments", RuntimeValue.String(builder.Arguments.ToString()));
                tcObj.Set("function", RuntimeValue.Object(funcObj));
                toolCallsArray.Add(RuntimeValue.Object(tcObj));
            }

            if (toolCallsArray.Count > 0)
                resultObj.Set("tool_calls", RuntimeValue.Array(toolCallsArray));
        }

        if (_promptTokens.HasValue || _completionTokens.HasValue || _totalTokens.HasValue || _cost.HasValue)
        {
            var usageObj = new JsonObject();
            if (_promptTokens.HasValue)
                usageObj.Set("promptTokens", RuntimeValue.Integer(_promptTokens.Value));
            if (_completionTokens.HasValue)
                usageObj.Set("completionTokens", RuntimeValue.Integer(_completionTokens.Value));
            if (_totalTokens.HasValue)
                usageObj.Set("totalTokens", RuntimeValue.Integer(_totalTokens.Value));
            if (_cost.HasValue)
                usageObj.Set("cost", RuntimeValue.Float(_cost.Value));
            resultObj.Set("usage", RuntimeValue.Object(usageObj));
        }

        return resultObj;
    }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}
