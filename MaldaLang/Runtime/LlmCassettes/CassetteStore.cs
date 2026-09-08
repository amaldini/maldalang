// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.LlmCassettes;

using System.Text.Json;
using MaldaLang.Interpreter;

public sealed class CassetteEntry
{
    public string Key { get; set; } = "";
    public string? Model { get; set; }
    public string Mode { get; set; } = "A";
    public JsonElement Response { get; set; }
}

/// <summary>
/// JSONL cassette file: one recorded Chat response per line, keyed for replay.
/// </summary>
public sealed class CassetteStore
{
    private readonly string _path;
    private readonly Dictionary<string, CassetteEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public CassetteStore(string path)
    {
        _path = path;
        Load();
    }

    public string Path => _path;

    public bool TryGet(string key, out RuntimeValue response)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                response = CassetteJson.FromElement(entry.Response);
                return true;
            }
        }

        response = RuntimeValue.Null();
        return false;
    }

    public void Append(string key, CassetteRequest request, RuntimeValue response)
    {
        var entry = new CassetteEntry
        {
            Key = key,
            Model = request.Model,
            Mode = request.Mode,
            Response = CassetteJson.ToElement(response)
        };

        lock (_sync)
        {
            _entries[key] = entry;
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(_path, JsonSerializer.Serialize(entry, CassetteJson.Options) + System.Environment.NewLine);
        }
    }

    private void Load()
    {
        if (!File.Exists(_path))
            return;

        foreach (var line in File.ReadAllLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var entry = JsonSerializer.Deserialize<CassetteEntry>(line, CassetteJson.Options);
                if (entry != null && !string.IsNullOrEmpty(entry.Key))
                    _entries[entry.Key] = entry;
            }
            catch (JsonException)
            {
                // Skip corrupt lines; replay-strict will miss them.
            }
        }
    }
}
