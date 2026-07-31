// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Models;

/// <summary>
/// Represents progress information for a model download.
/// </summary>
public class ModelDownloadProgress
{
    public string ModelId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public long BytesPerSecond { get; set; }
    public bool IsComplete { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets the download progress as a percentage (0-100).
    /// </summary>
    public int Percentage
    {
        get
        {
            if (TotalBytes == 0)
                return 0;
            return (int)((BytesDownloaded * 100) / TotalBytes);
        }
    }

    /// <summary>
    /// Gets a formatted string for bytes downloaded.
    /// </summary>
    public string GetFormattedDownloaded()
    {
        return FormatBytes(BytesDownloaded);
    }

    /// <summary>
    /// Gets a formatted string for total bytes.
    /// </summary>
    public string GetFormattedTotal()
    {
        return FormatBytes(TotalBytes);
    }

    /// <summary>
    /// Gets a formatted string for download speed.
    /// </summary>
    public string GetFormattedSpeed()
    {
        return $"{FormatBytes(BytesPerSecond)}/s";
    }

    private static string FormatBytes(long bytes)
    {
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