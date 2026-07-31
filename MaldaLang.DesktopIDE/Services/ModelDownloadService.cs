// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

using System.IO;
using System.Net.Http;
using MaldaLang.DesktopIDE.Models;

/// <summary>
/// Service for downloading model files with progress tracking.
/// </summary>
public class ModelDownloadService
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private readonly Dictionary<string, CancellationTokenSource> _activeDownloads = new();

    /// <summary>
    /// Event fired during download to report progress.
    /// </summary>
    public event Action<ModelDownloadProgress>? OnDownloadProgress;

    static ModelDownloadService()
    {
        _httpClient.Timeout = TimeSpan.FromHours(2); // Allow long downloads
    }

    /// <summary>
    /// Downloads a model file from a URL.
    /// </summary>
    /// <param name="modelId">The model ID for tracking</param>
    /// <param name="fileName">The file name</param>
    /// <param name="downloadUrl">The URL to download from</param>
    /// <param name="destinationPath">The local path to save the file</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>True if download completed successfully</returns>
    public async Task<bool> DownloadFileAsync(
        string modelId,
        string fileName,
        string downloadUrl,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeDownloads[modelId] = cts;

        try
        {
            // Ensure destination directory exists
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Check if file already exists
            if (File.Exists(destinationPath))
            {
                var existingSize = new FileInfo(destinationPath).Length;
                var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl);
                var headResponse = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                
                if (headResponse.IsSuccessStatusCode && headResponse.Content.Headers.ContentLength.HasValue)
                {
                    var totalSize = headResponse.Content.Headers.ContentLength.Value;
                    if (existingSize == totalSize)
                    {
                        // File already exists and is complete
                        ReportProgress(modelId, fileName, totalSize, totalSize, 0, true);
                        return true;
                    }
                }
            }

            // Download the file
            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var startTime = DateTime.Now;
                long lastBytes = 0;
                var lastTime = startTime;

                using (var contentStream = await response.Content.ReadAsStreamAsync(cts.Token))
                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long totalBytesRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                        totalBytesRead += bytesRead;

                        // Calculate speed every second
                        var currentTime = DateTime.Now;
                        var elapsed = (currentTime - lastTime).TotalSeconds;
                        if (elapsed >= 1.0)
                        {
                            var bytesSinceLastUpdate = totalBytesRead - lastBytes;
                            var bytesPerSecond = (long)(bytesSinceLastUpdate / elapsed);
                            ReportProgress(modelId, fileName, totalBytesRead, totalBytes, bytesPerSecond, false);
                            lastBytes = totalBytesRead;
                            lastTime = currentTime;
                        }
                    }

                    // Final progress report
                    ReportProgress(modelId, fileName, totalBytesRead, totalBytes, 0, true);
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            // Clean up partial file
            if (File.Exists(destinationPath))
            {
                try
                {
                    File.Delete(destinationPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            var progress = new ModelDownloadProgress
            {
                ModelId = modelId,
                FileName = fileName,
                IsComplete = false,
                ErrorMessage = "Download cancelled"
            };
            OnDownloadProgress?.Invoke(progress);
            return false;
        }
        catch (Exception ex)
        {
            // Clean up partial file
            if (File.Exists(destinationPath))
            {
                try
                {
                    File.Delete(destinationPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            var errorProgress = new ModelDownloadProgress
            {
                ModelId = modelId,
                FileName = fileName,
                IsComplete = false,
                ErrorMessage = ex.Message
            };
            OnDownloadProgress?.Invoke(errorProgress);
            return false;
        }
        finally
        {
            _activeDownloads.Remove(modelId);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Cancels an active download.
    /// </summary>
    /// <param name="modelId">The model ID to cancel</param>
    public void CancelDownload(string modelId)
    {
        if (_activeDownloads.TryGetValue(modelId, out var cts))
        {
            cts.Cancel();
        }
    }

    /// <summary>
    /// Checks if a download is in progress for the given model.
    /// </summary>
    /// <param name="modelId">The model ID to check</param>
    /// <returns>True if download is in progress</returns>
    public bool IsDownloading(string modelId)
    {
        return _activeDownloads.ContainsKey(modelId);
    }

    private void ReportProgress(string modelId, string fileName, long bytesDownloaded, long totalBytes, long bytesPerSecond, bool isComplete)
    {
        var progress = new ModelDownloadProgress
        {
            ModelId = modelId,
            FileName = fileName,
            BytesDownloaded = bytesDownloaded,
            TotalBytes = totalBytes,
            BytesPerSecond = bytesPerSecond,
            IsComplete = isComplete
        };
        OnDownloadProgress?.Invoke(progress);
    }
}