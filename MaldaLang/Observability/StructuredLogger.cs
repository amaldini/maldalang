namespace MaldaLang.Observability;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public sealed class StructuredLogger
{
    public string Environment { get; }
    public string Profile { get; }
    public bool IncludeCorrelationId { get; }

    public StructuredLogger(string environment, string profile, bool includeCorrelationId)
    {
        Environment = environment;
        Profile = profile;
        IncludeCorrelationId = includeCorrelationId;
    }

    public IReadOnlyDictionary<string, object> CreateEntry(string level, string message, string? correlationId = null)
    {
        var entry = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["level"] = level,
            ["message"] = message,
            ["environment"] = Environment,
            ["profile"] = Profile
        };

        if (IncludeCorrelationId && !string.IsNullOrWhiteSpace(correlationId))
        {
            entry["correlationId"] = correlationId!;
        }

        return entry;
    }

    public void LogInfo(TextWriter output, string message, string? correlationId = null)
    {
        var entry = CreateEntry("info", message, correlationId);
        output.WriteLine(JsonSerializer.Serialize(entry));
    }
}
