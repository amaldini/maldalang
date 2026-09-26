// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

using System;
using System.IO;
using System.Text.Json;

/// <summary>
/// Persists Desktop IDE layout choices. Output stays in the right pane unless the user docks it under the editor.
/// </summary>
public sealed class LayoutSettingsService
{
    private readonly string _settingsFilePath;

    public LayoutSettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "MaldaLang");
        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "layout-settings.json");
    }

    public bool OutputOnRight { get; private set; } = true;

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return;
            }

            var settings = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(_settingsFilePath));
            if (settings != null)
            {
                OutputOnRight = settings.OutputOnRight;
            }
        }
        catch
        {
            // Keep the default: output on the right.
        }
    }

    public void SetOutputOnRight(bool outputOnRight)
    {
        OutputOnRight = outputOnRight;
        try
        {
            var json = JsonSerializer.Serialize(
                new StoredSettings { OutputOnRight = outputOnRight },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Ignore persistence failures.
        }
    }

    private sealed class StoredSettings
    {
        public bool OutputOnRight { get; set; } = true;
    }
}
