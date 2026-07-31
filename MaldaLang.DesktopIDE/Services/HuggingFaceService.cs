// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using MaldaLang.DesktopIDE.Models;

/// <summary>
/// Service for interacting with HuggingFace Hub API.
/// </summary>
public class HuggingFaceService
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private const string ApiBaseUrl = "https://huggingface.co/api";
    private const string HubBaseUrl = "https://huggingface.co";

    /// <summary>
    /// Event fired to report progress during model search.
    /// </summary>
    public event Action<string>? OnProgress;

    static HuggingFaceService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MaldaLang-IDE/1.0");
        _httpClient.Timeout = TimeSpan.FromMinutes(2); // Increased timeout for slow connections
    }

    /// <summary>
    /// Searches for GGUF models on HuggingFace Hub.
    /// </summary>
    /// <param name="query">Search query (model name, author, etc.)</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching models</returns>
    public async Task<List<HuggingFaceModel>> SearchGgufModelsAsync(
        string? query = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var searchQuery = "gguf";
            if (!string.IsNullOrWhiteSpace(query))
            {
                searchQuery = $"{query} {searchQuery}";
            }

            var url = $"{ApiBaseUrl}/models?search={Uri.EscapeDataString(searchQuery)}&limit={limit}&sort=downloads";
            
            OnProgress?.Invoke("Connecting to HuggingFace...");
            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to search models: {response.StatusCode}");
            }

            OnProgress?.Invoke("Receiving data from HuggingFace...");
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            OnProgress?.Invoke("Processing model list...");
            var models = JsonSerializer.Deserialize<List<HuggingFaceModel>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (models == null || models.Count == 0)
                return new List<HuggingFaceModel>();

            // First, check if any models already have siblings (file information) from the search response
            var modelsWithGguf = new List<HuggingFaceModel>();
            var modelsNeedingDetails = new List<HuggingFaceModel>();

            foreach (var model in models)
            {
                // Check if siblings are already included in the search response
                if (model.Siblings != null && model.Siblings.Count > 0)
                {
                    if (model.GetGgufFiles().Count > 0)
                    {
                        modelsWithGguf.Add(model);
                    }
                }
                else
                {
                    // Need to fetch full details
                    modelsNeedingDetails.Add(model);
                }
            }

            // If we already have some models with GGUF files, return them immediately
            // and fetch details for others in the background
            if (modelsWithGguf.Count > 0 && modelsNeedingDetails.Count > 10)
            {
                // Return what we have and fetch the rest asynchronously
                // This provides faster initial results
            }

            // Fetch details for models that don't have file information yet
            // Limit concurrent requests to avoid rate limiting
            const int maxConcurrent = 5;
            var semaphore = new SemaphoreSlim(maxConcurrent);
            var tasks = new List<Task>();
            var fetchedCount = 0;
            var maxToFetch = Math.Min(modelsNeedingDetails.Count, limit - modelsWithGguf.Count);

            if (maxToFetch > 0)
            {
                OnProgress?.Invoke($"Fetching details for {maxToFetch} models...");
            }

            for (int i = 0; i < maxToFetch; i++)
            {
                var model = modelsNeedingDetails[i];
                var index = i; // Capture for progress reporting
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var fullModel = await GetModelDetailsAsync(model.Id, cancellationToken);
                        if (fullModel != null && fullModel.GetGgufFiles().Count > 0)
                        {
                            int currentCount;
                            lock (modelsWithGguf)
                            {
                                modelsWithGguf.Add(fullModel);
                                fetchedCount++;
                                currentCount = fetchedCount;
                            }
                            OnProgress?.Invoke($"Fetched {currentCount}/{maxToFetch} models...");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail completely - some models might be unavailable
                        System.Diagnostics.Debug.WriteLine($"Failed to load model {model.Id}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                        // Small delay to avoid rate limiting
                        await Task.Delay(100, cancellationToken);
                    }
                }, cancellationToken));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
                OnProgress?.Invoke("Finalizing model list...");
            }

            // If we still have no models, try a different approach:
            // Return models that match "gguf" in their name/ID even if we couldn't verify files
            // The user can still select them and we'll fetch details on demand
            if (modelsWithGguf.Count == 0 && models.Count > 0)
            {
                // Fallback: return models that have "gguf" in their ID or tags
                foreach (var model in models.Take(limit))
                {
                    if (model.Id.Contains("gguf", StringComparison.OrdinalIgnoreCase) ||
                        model.Tags.Any(t => t.Contains("gguf", StringComparison.OrdinalIgnoreCase)))
                    {
                        modelsWithGguf.Add(model);
                        if (modelsWithGguf.Count >= limit)
                            break;
                    }
                }
            }

            return modelsWithGguf;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error searching for models: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets detailed information about a specific model.
    /// </summary>
    /// <param name="modelId">The model ID (e.g., "TheBloke/Llama-2-7B-Chat-GGUF")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Model details or null if not found</returns>
    public async Task<HuggingFaceModel?> GetModelDetailsAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // HuggingFace API expects the model ID with unencoded slash in the path
            // We need to encode each path segment separately, not the entire model ID
            var urlParts = modelId.Split('/');
            var encodedParts = urlParts.Select(Uri.EscapeDataString).ToArray();
            var url = $"{ApiBaseUrl}/models/{string.Join("/", encodedParts)}";
            
            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Try to get more details from the response for debugging
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                    
                // Log the full error for debugging
                System.Diagnostics.Debug.WriteLine($"API Error - Status: {response.StatusCode}, URL: {url}, ModelId: {modelId}, Response: {errorContent}");
                throw new Exception($"Failed to get model details: {response.StatusCode}. {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var model = JsonSerializer.Deserialize<HuggingFaceModel>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // If we got the model but siblings are empty OR if siblings exist but have no size info, try with files_metadata
            // The files_metadata parameter is needed to get file sizes
            if (model != null && (model.Siblings == null || model.Siblings.Count == 0 || model.Siblings.All(s => s.Size == 0)))
            {
                System.Diagnostics.Debug.WriteLine($"Siblings missing or have no size info for {modelId}, trying with files_metadata");
                try
                {
                    var urlWithMetadata = $"{ApiBaseUrl}/models/{string.Join("/", encodedParts)}?files_metadata=true";
                    var responseWithMetadata = await _httpClient.GetAsync(urlWithMetadata, cancellationToken);
                    
                    if (responseWithMetadata.IsSuccessStatusCode)
                    {
                        var contentWithMetadata = await responseWithMetadata.Content.ReadAsStringAsync(cancellationToken);
                        var modelWithMetadata = JsonSerializer.Deserialize<HuggingFaceModel>(contentWithMetadata, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (modelWithMetadata != null && modelWithMetadata.Siblings != null && modelWithMetadata.Siblings.Count > 0)
                        {
                            model.Siblings = modelWithMetadata.Siblings;
                            
                            // If sizes are still missing, fetch them via HEAD requests
                            if (model.Siblings.Any(s => s.Size == 0 && s.RFileName != null && s.RFileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)))
                            {
                                // Fetch sizes for GGUF files only (to avoid too many requests)
                                var ggufFiles = model.Siblings.Where(s => s.Size == 0 && s.RFileName != null && s.RFileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).ToList();
                                
                                // Limit to first 10 files to avoid rate limiting
                                foreach (var file in ggufFiles.Take(10))
                                {
                                    try
                                    {
                                        var fileUrl = GetDownloadUrl(modelId, file.RFileName!);
                                        var headRequest = new HttpRequestMessage(HttpMethod.Head, fileUrl);
                                        var headResponse = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                                        
                                        if (headResponse.IsSuccessStatusCode && headResponse.Content.Headers.ContentLength.HasValue)
                                        {
                                            file.Size = headResponse.Content.Headers.ContentLength.Value;
                                        }
                                        
                                        // Small delay to avoid rate limiting
                                        await Task.Delay(100, cancellationToken);
                                    }
                                    catch
                                    {
                                        // Ignore errors for individual file size fetches
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore errors from the metadata request - we'll use what we have
                }
            }

            return model;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error getting model details: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets the download URL for a specific file in a model repository.
    /// </summary>
    /// <param name="modelId">The model ID</param>
    /// <param name="fileName">The file name (e.g., "llama-2-7b-chat.Q4_K_M.gguf")</param>
    /// <returns>The download URL</returns>
    public string GetDownloadUrl(string modelId, string fileName)
    {
        // Encode each path segment separately, not the entire model ID
        // HuggingFace expects: https://huggingface.co/org/model/resolve/main/file.gguf
        // NOT: https://huggingface.co/org%2Fmodel/resolve/main/file.gguf
        var urlParts = modelId.Split('/');
        var encodedParts = urlParts.Select(Uri.EscapeDataString).ToArray();
        var encodedModelId = string.Join("/", encodedParts);
        var url = $"{HubBaseUrl}/{encodedModelId}/resolve/main/{Uri.EscapeDataString(fileName)}";
        return url;
    }

    /// <summary>
    /// Sets an optional HuggingFace token for authenticated requests (for private models or higher rate limits).
    /// </summary>
    /// <param name="token">HuggingFace API token</param>
    public void SetAuthToken(string? token)
    {
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        }
    }
}