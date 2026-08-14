// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DebugAdapter;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// DAP JSON-RPC DTOs. Property names are camelCase except <c>request_seq</c>.
/// </summary>
public static class DapProtocol
{
    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static DapIncoming Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.Clone();
        return new DapIncoming
        {
            Seq = ReadInt32(root, "seq"),
            Type = ReadString(root, "type") ?? "",
            Command = ReadString(root, "command") ?? "",
            Event = ReadString(root, "event") ?? "",
            RequestSeq = TryGetProperty(root, "request_seq", out _)
                ? ReadInt32(root, "request_seq")
                : ReadInt32(root, "requestSeq"),
            Success = ReadBoolean(root, "success"),
            Message = ReadString(root, "message"),
            Arguments = ReadElement(root, "arguments"),
            Body = ReadElement(root, "body")
        };
    }

    public static string FormatResponse(
        int seq,
        int requestSeq,
        string command,
        bool success,
        object? body = null,
        string? message = null)
    {
        var dto = new DapResponseDto
        {
            Seq = seq,
            Type = "response",
            RequestSeq = requestSeq,
            Success = success,
            Command = command,
            Message = message,
            Body = body == null ? null : JsonSerializer.SerializeToElement(body, JsonOptions)
        };
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static string FormatEvent(int seq, string eventName, object? body = null)
    {
        var dto = new DapEventDto
        {
            Seq = seq,
            Type = "event",
            Event = eventName,
            Body = body == null ? null : JsonSerializer.SerializeToElement(body, JsonOptions)
        };
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static string? ReadString(JsonElement obj, string name)
    {
        if (!TryGetProperty(obj, name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    public static int ReadInt32(JsonElement obj, string name, int defaultValue = 0)
    {
        if (!TryGetProperty(obj, name, out var value))
            return defaultValue;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
            return n;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out n))
            return n;
        return defaultValue;
    }

    public static bool ReadBoolean(JsonElement obj, string name, bool defaultValue = false)
    {
        if (!TryGetProperty(obj, name, out var value))
            return defaultValue;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    public static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out value))
            return true;

        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static JsonElement ReadElement(JsonElement obj, string name)
    {
        return TryGetProperty(obj, name, out var value) ? value : default;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}

public sealed class DapIncoming
{
    public int Seq { get; init; }
    public string Type { get; init; } = "";
    public string Command { get; init; } = "";
    public string Event { get; init; } = "";
    public int RequestSeq { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
    public JsonElement Arguments { get; init; }
    public JsonElement Body { get; init; }
}

public sealed class DapCapabilities
{
    public bool SupportsConfigurationDoneRequest { get; set; }
    public bool SupportsConditionalBreakpoints { get; set; }
    public bool SupportsEvaluateForHovers { get; set; }
    public bool SupportsSetVariable { get; set; }
}

public sealed class DapThread
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class DapSource
{
    public string? Path { get; set; }
    public string? Name { get; set; }
}

public sealed class DapStackFrame
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public DapSource? Source { get; set; }
}

public sealed class DapScope
{
    public string Name { get; set; } = "";
    public int VariablesReference { get; set; }
    public bool Expensive { get; set; }
}

public sealed class DapVariable
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Type { get; set; }
    public int VariablesReference { get; set; }
}

public sealed class DapBreakpoint
{
    public int? Id { get; set; }
    public bool Verified { get; set; }
    public int Line { get; set; }
}

public sealed class DapStoppedBody
{
    public string Reason { get; set; } = "";
    public int ThreadId { get; set; }
    public bool AllThreadsStopped { get; set; }
    public string? Text { get; set; }
    public string? Description { get; set; }
}

public sealed class DapOutputBody
{
    public string Category { get; set; } = "";
    public string Output { get; set; } = "";
}

public sealed class DapExitedBody
{
    public int ExitCode { get; set; }
}

public sealed class DapEvaluateBody
{
    public string Result { get; set; } = "";
    public string? Type { get; set; }
    public int VariablesReference { get; set; }
}

public sealed class DapContinueBody
{
    public bool AllThreadsContinued { get; set; } = true;
}

internal sealed class DapResponseDto
{
    public int Seq { get; set; }
    public string Type { get; set; } = "response";
    [JsonPropertyName("request_seq")]
    public int RequestSeq { get; set; }
    public bool Success { get; set; }
    public string Command { get; set; } = "";
    public string? Message { get; set; }
    public JsonElement? Body { get; set; }
}

internal sealed class DapEventDto
{
    public int Seq { get; set; }
    public string Type { get; set; } = "event";
    public string Event { get; set; } = "";
    public JsonElement? Body { get; set; }
}
