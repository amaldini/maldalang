// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

using System.IO;
using System.Text.Json;
using MaldaLang.DesktopIDE.Models;

/// <summary>
/// Service for managing local model storage and metadata.
/// </summary>
public class ModelStorageService
{
    private readonly string _modelsDirectory;
    private readonly string _metadataFile;
    private List<InstalledModel> _installedModels = new();

    public ModelStorageService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _modelsDirectory = Path.Combine(appDataPath, "MaldaLang", "Models");
        _metadataFile = Path.Combine(_modelsDirectory, "models.json");

        // Ensure directory exists
        if (!Directory.Exists(_modelsDirectory))
        {
            Directory.CreateDirectory(_modelsDirectory);
        }

        // Load existing metadata
        LoadMetadata();
    }

    /// <summary>
    /// Gets the default models directory path.
    /// </summary>
    public string ModelsDirectory => _modelsDirectory;

    /// <summary>
    /// Gets all installed models.
    /// </summary>
    public List<InstalledModel> GetInstalledModels()
    {
        // Validate that files still exist
        _installedModels = _installedModels
            .Where(m => File.Exists(m.Path))
            .ToList();
        SaveMetadata();
        return _installedModels.ToList();
    }

    /// <summary>
    /// Registers a downloaded model.
    /// </summary>
    /// <param name="modelId">The HuggingFace model ID</param>
    /// <param name="filePath">The local file path</param>
    /// <param name="fileName">The file name</param>
    public void RegisterModel(string modelId, string filePath, string fileName)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"Model file not found: {filePath}");
        }

        // Remove existing entry if present
        _installedModels.RemoveAll(m => m.Id == modelId && m.FileName == fileName);

        // Add new entry
        var model = new InstalledModel
        {
            Id = modelId,
            Path = filePath,
            Size = fileInfo.Length,
            DownloadedAt = DateTime.UtcNow,
            FileName = fileName
        };
        _installedModels.Add(model);

        SaveMetadata();
    }

    /// <summary>
    /// Gets the local path for a model, if installed.
    /// </summary>
    /// <param name="modelId">The HuggingFace model ID</param>
    /// <param name="fileName">Optional file name to get specific file</param>
    /// <returns>The local path or null if not found</returns>
    public string? GetModelPath(string modelId, string? fileName = null)
    {
        var models = _installedModels
            .Where(m => m.Id == modelId)
            .ToList();

        if (string.IsNullOrEmpty(fileName))
        {
            // Return the first matching model
            return models.FirstOrDefault()?.Path;
        }

        // Return the specific file
        return models.FirstOrDefault(m => m.FileName == fileName)?.Path;
    }

    /// <summary>
    /// Checks if a model is installed.
    /// </summary>
    /// <param name="modelId">The HuggingFace model ID</param>
    /// <param name="fileName">Optional file name to check</param>
    /// <returns>True if the model is installed</returns>
    public bool IsModelInstalled(string modelId, string? fileName = null)
    {
        var path = GetModelPath(modelId, fileName);
        return path != null && File.Exists(path);
    }

    /// <summary>
    /// Unregisters a model (does not delete the file).
    /// </summary>
    /// <param name="modelId">The HuggingFace model ID</param>
    /// <param name="fileName">Optional file name</param>
    public void UnregisterModel(string modelId, string? fileName = null)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            _installedModels.RemoveAll(m => m.Id == modelId);
        }
        else
        {
            _installedModels.RemoveAll(m => m.Id == modelId && m.FileName == fileName);
        }
        SaveMetadata();
    }

    /// <summary>
    /// Deletes a model file and unregisters it.
    /// </summary>
    /// <param name="modelId">The HuggingFace model ID</param>
    /// <param name="fileName">Optional file name</param>
    /// <returns>True if deletion was successful</returns>
    public bool DeleteModel(string modelId, string? fileName = null)
    {
        var path = GetModelPath(modelId, fileName);
        if (path == null || !File.Exists(path))
        {
            return false;
        }

        try
        {
            File.Delete(path);
            UnregisterModel(modelId, fileName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the suggested file path for a new download.
    /// </summary>
    /// <param name="modelId">The HuggingFace model ID</param>
    /// <param name="fileName">The file name</param>
    /// <returns>The suggested file path</returns>
    public string GetSuggestedFilePath(string modelId, string fileName)
    {
        // Sanitize model ID for use in file path
        var sanitizedId = string.Join("_", modelId.Split(Path.GetInvalidFileNameChars()));
        var directory = Path.Combine(_modelsDirectory, sanitizedId);
        
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return Path.Combine(directory, fileName);
    }

    /// <summary>
    /// Opens the models directory in the file explorer.
    /// </summary>
    public void OpenModelsDirectory()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _modelsDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors
        }
    }

    private void LoadMetadata()
    {
        if (!File.Exists(_metadataFile))
        {
            _installedModels = new List<InstalledModel>();
            return;
        }

        try
        {
            var json = File.ReadAllText(_metadataFile);
            var data = JsonSerializer.Deserialize<ModelsMetadata>(json);
            _installedModels = data?.Models ?? new List<InstalledModel>();
        }
        catch
        {
            _installedModels = new List<InstalledModel>();
        }
    }

    private void SaveMetadata()
    {
        try
        {
            var data = new ModelsMetadata
            {
                Models = _installedModels
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_metadataFile, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    private class ModelsMetadata
    {
        public List<InstalledModel> Models { get; set; } = new();
    }
}