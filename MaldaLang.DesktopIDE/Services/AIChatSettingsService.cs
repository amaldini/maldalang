// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

using System.IO;
using System.Text.Json;
using MaldaLang.DesktopIDE.Models;

/// <summary>
/// Service for persisting AI chat settings.
/// </summary>
public class AIChatSettingsService
{
    private readonly string _settingsFilePath;

    public AIChatSettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "MaldaLang");
        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "ai-chat-settings.json");
    }

    /// <summary>
    /// Loads AI chat settings from disk.
    /// </summary>
    public AIChatSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AIChatSettings>(json);
                return settings ?? new AIChatSettings { UseOpenRouterClient = true };
            }
        }
        catch
        {
            // If loading fails, return default settings
        }
        return new AIChatSettings { UseOpenRouterClient = true };
    }

    /// <summary>
    /// Saves AI chat settings to disk.
    /// </summary>
    public void SaveSettings(AIChatSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // If saving fails, silently ignore
        }
    }
}