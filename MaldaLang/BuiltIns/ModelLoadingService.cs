// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Global service for aggregating and reporting model loading progress from all LlamaCppClient instances.
/// </summary>
public static class ModelLoadingService
{
    /// <summary>
    /// Progress information for model loading.
    /// </summary>
    public class ModelLoadingProgress
    {
        public string ModelPath { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int Percentage { get; set; }
        public bool IsLoading { get; set; }
        public bool IsError { get; set; }
    }
    
    private static readonly Dictionary<string, ModelLoadingProgress> _activeLoadings = new();
    private static readonly object _lock = new object();
    
    /// <summary>
    /// Event fired when model loading progress changes.
    /// </summary>
    public static event Action<ModelLoadingProgress>? OnProgressChanged;
    
    /// <summary>
    /// Event fired when model loading starts.
    /// </summary>
    public static event Action<ModelLoadingProgress>? OnLoadingStarted;
    
    /// <summary>
    /// Event fired when model loading completes.
    /// </summary>
    public static event Action<string>? OnLoadingCompleted;
    
    /// <summary>
    /// Reports progress for a model loading operation.
    /// </summary>
    public static void ReportProgress(string modelPath, string message, int percentage, bool isLoading, bool isError = false)
    {
        lock (_lock)
        {
            var progress = new ModelLoadingProgress
            {
                ModelPath = modelPath,
                Message = message,
                Percentage = percentage,
                IsLoading = isLoading,
                IsError = isError
            };
            
            if (isLoading && percentage < 100)
            {
                if (!_activeLoadings.ContainsKey(modelPath))
                {
                    _activeLoadings[modelPath] = progress;
                    OnLoadingStarted?.Invoke(progress);
                }
                else
                {
                    _activeLoadings[modelPath] = progress;
                }
            }
            else
            {
                if (_activeLoadings.ContainsKey(modelPath))
                {
                    _activeLoadings.Remove(modelPath);
                    OnLoadingCompleted?.Invoke(modelPath);
                }
            }
            
            OnProgressChanged?.Invoke(progress);
        }
    }
    
    /// <summary>
    /// Gets all currently active model loadings.
    /// </summary>
    public static List<ModelLoadingProgress> GetActiveLoadings()
    {
        lock (_lock)
        {
            return _activeLoadings.Values.ToList();
        }
    }
    
    /// <summary>
    /// Checks if any model is currently loading.
    /// </summary>
    public static bool IsAnyModelLoading()
    {
        lock (_lock)
        {
            return _activeLoadings.Count > 0;
        }
    }
}