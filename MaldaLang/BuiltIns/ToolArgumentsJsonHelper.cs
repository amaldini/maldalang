// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Text.Json;

/// <summary>
/// Detects truncated or malformed LLM tool-call argument JSON (common with large write_file payloads).
/// </summary>
internal static class ToolArgumentsJsonHelper
{
    public static bool IsLikelyTruncated(JsonException ex, string argumentsJson, string? toolName)
    {
        var msg = ex.Message;
        var hasTruncationSignal =
            msg.Contains("end of data", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("end of string", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("reached end of", StringComparison.OrdinalIgnoreCase);

        if (hasTruncationSignal)
        {
            if (toolName is "write_file" or "replace_in_file" or "edit_file")
                return argumentsJson.Length >= 40;
            return argumentsJson.Length > 500;
        }

        if (argumentsJson.Length > 20000 &&
            (msg.Contains("escape", StringComparison.OrdinalIgnoreCase) ||
             msg.Contains("Invalid character", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    public static bool LooksLikeWriteFilePayload(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return false;
        return argumentsJson.Contains("filePath", StringComparison.OrdinalIgnoreCase) &&
               argumentsJson.Contains("content", StringComparison.OrdinalIgnoreCase);
    }

    public static string WriteFileTruncationToolResult(int jsonLength) =>
        $"Error: Tool call JSON was truncated ({jsonLength} chars) — the model output was cut off before the JSON string closed. " +
        "Do NOT retry write_file with the full file. Use read_file, then edit_file or replace_in_file with small targeted changes (each oldText/newText under ~1500 characters).";
}
