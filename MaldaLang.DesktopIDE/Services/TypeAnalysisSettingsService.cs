// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

using System;
using System.IO;
using System.Text.Json;
using MaldaLang.IDE;

/// <summary>
/// Persists Desktop IDE type-diagnostic severity (default: type mismatches as errors).
/// </summary>
public sealed class TypeAnalysisSettingsService
{
    private readonly string _settingsFilePath;

    public TypeAnalysisSettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "MaldaLang");
        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "type-analysis-settings.json");
    }

    public bool TypeErrors { get; private set; } = true;

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
                return;

            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<StoredSettings>(json);
            if (settings != null)
                TypeErrors = settings.TypeErrors;
        }
        catch
        {
            // Keep default
        }
    }

    public void SetTypeErrors(bool typeErrors)
    {
        TypeErrors = typeErrors;
        try
        {
            var json = JsonSerializer.Serialize(
                new StoredSettings { TypeErrors = typeErrors },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Ignore persistence failures
        }
    }

    public StrictTypesOptions ToOptions() =>
        TypeErrors ? StrictTypesOptions.Default : StrictTypesOptions.Lenient;

    private sealed class StoredSettings
    {
        public bool TypeErrors { get; set; } = true;
    }
}
