// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a model from HuggingFace Hub.
/// </summary>
public class HuggingFaceModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("likes")]
    public int Likes { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("pipeline_tag")]
    public string? PipelineTag { get; set; }

    [JsonPropertyName("siblings")]
    public List<HuggingFaceFile>? Siblings { get; set; }

    /// <summary>
    /// Gets the GGUF files from the model's siblings.
    /// </summary>
    public List<HuggingFaceFile> GetGgufFiles()
    {
        if (Siblings == null)
            return new List<HuggingFaceFile>();

        return Siblings
            .Where(f => f.RFileName != null && f.RFileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Gets the total size of all GGUF files in bytes.
    /// </summary>
    public long GetTotalGgufSize()
    {
        return GetGgufFiles().Sum(f => f.Size);
    }

    /// <summary>
    /// Formats the total size as a human-readable string.
    /// </summary>
    public string GetFormattedSize()
    {
        var bytes = GetTotalGgufSize();
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Represents a file in a HuggingFace model repository.
/// </summary>
public class HuggingFaceFile
{
    [JsonPropertyName("rfilename")]
    public string? RFileName { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Formats the file size as a human-readable string.
    /// </summary>
    public string GetFormattedSize()
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = Size;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Represents model metadata stored locally.
/// </summary>
public class InstalledModel
{
    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime DownloadedAt { get; set; }
    public string? FileName { get; set; }
}