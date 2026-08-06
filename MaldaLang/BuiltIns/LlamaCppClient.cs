// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;
using LLama;
using LLama.Common;
using LLama.Sampling;
using System.Threading;

/// <summary>
/// LLAMA.cpp client for local LLM inference using GGUF models.
/// </summary>
public class LlamaCppClientInstance : ObjectInstance, IDisposable
{
    public string ModelPath { get; set; }
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2000;
    public int GpuLayerCount { get; set; } = 0; // 0 = CPU only, >0 = number of layers to offload to GPU
    
    private LLamaWeights? _model;
    private StatelessExecutor? _executor;
    private ModelParams? _modelParams;
    private bool _disposed = false;
    private Task? _loadingTask;
    private readonly SemaphoreSlim _loadingLock = new SemaphoreSlim(1, 1);
    private CancellationTokenSource? _progressCancellation;
    private Exception? _loadingException = null; // Store exception for immediate access
    private readonly object _loadingExceptionLock = new object();
    
    /// <summary>
    /// Progress information for model loading.
    /// </summary>
    public class ModelLoadingProgress
    {
        public string Message { get; set; } = string.Empty;
        public int Percentage { get; set; }
    }
    
    /// <summary>
    /// Event fired during model loading to report progress.
    /// </summary>
    public event Action<ModelLoadingProgress>? OnLoadingProgress;
    
    public LlamaCppClientInstance() : base(null)
    {
        ModelPath = "";
        // Try to auto-detect GPU support if available
        GpuLayerCount = TryDetectGpuSupport();
    }

    private string ResolveModelPathForLoad()
    {
        if (string.IsNullOrWhiteSpace(ModelPath))
        {
            ModelPath = DefaultLocalLlm.GetOrDownloadDefaultModelPath();
            return ModelPath;
        }

        var configuredPath = ModelPath.Trim();
        try
        {
            var (defaultModelId, defaultFileName) = DefaultLocalLlm.GetDefaultLocalModelFromEnvironment();
            var defaultPath = Path.Combine(DefaultLocalLlm.GetDefaultModelsDirectory(defaultModelId), defaultFileName);
            if (!File.Exists(configuredPath) &&
                string.Equals(Path.GetFullPath(configuredPath), Path.GetFullPath(defaultPath), StringComparison.OrdinalIgnoreCase))
            {
                ModelPath = DefaultLocalLlm.GetOrDownloadDefaultModelPath();
                return ModelPath;
            }
        }
        catch
        {
            // Fall back to the configured path if path normalization fails.
        }

        ModelPath = configuredPath;
        return ModelPath;
    }
    
    /// <summary>
    /// Attempts to detect if GPU support is available. Returns 0 if CPU-only or if detection fails.
    /// This is a best-effort detection that gracefully handles missing CUDA backends.
    /// </summary>
    private int TryDetectGpuSupport()
    {
        try
        {
            // Check if CUDA backend is available by trying to load a minimal test
            // Since we only have CPU backend by default, this will return 0
            // Users can manually set GpuLayerCount if they install CUDA backends
            return 0; // Default to CPU - users must explicitly enable GPU or install CUDA backends
        }
        catch
        {
            return 0; // Fallback to CPU on any error
        }
    }
    
    /// <summary>
    /// Gets the available physical memory in MB. Returns -1 if unable to determine.
    /// Uses cross-platform approach that doesn't require additional dependencies.
    /// </summary>
    private long GetAvailableMemoryMB()
    {
        try
        {
            // Try to use GC memory info as a rough estimate
            // This is not perfect but doesn't require additional dependencies
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            var managedMemoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            
            // For a rough estimate, we can use the working set
            // Note: This is not accurate for system memory, but gives a ballpark
            // In practice, we'll mostly rely on the error message diagnostics
            // Return -1 to indicate we can't accurately determine system memory
            // The memory check is just a helpful warning anyway
            return -1; // Unable to accurately determine system memory without additional dependencies
        }
        catch
        {
            return -1; // Unable to determine
        }
    }
    
    private void EnsureModelLoaded()
    {
        // If already loaded, return immediately
        if (_model != null && _executor != null)
        {
            return;
        }
        
        // If loading is in progress, wait for it to complete
        if (_loadingTask != null && !_loadingTask.IsCompleted)
        {
            try
            {
                _loadingTask.ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Re-throw with more context
                throw new Exception($"Model loading failed: {ex.Message}", ex);
            }
            return;
        }
        
        // Start loading if not already started
        if (_loadingTask == null || _loadingTask.IsCompleted)
        {
            // Clear any previous exception
            lock (_loadingExceptionLock)
            {
                _loadingException = null;
            }
            
            _loadingTask = EnsureModelLoadedAsync();
            
            // Add continuation to detect faults immediately and store exception
            _loadingTask.ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    var baseException = task.Exception.GetBaseException();
                    lock (_loadingExceptionLock)
                    {
                        _loadingException = baseException;
                    }
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
        
        // Wait for loading to complete - poll task status and handle faults immediately
        try
        {
            // Poll task status with timeout - check for faults immediately
            var timeout = TimeSpan.FromSeconds(60);
            var startTime = DateTime.Now;
            int pollCount = 0;
            
            while ((DateTime.Now - startTime) < timeout)
            {
                pollCount++;
                
                // Force memory barrier to ensure we see latest task state
                System.Threading.Thread.MemoryBarrier();
                
                // Check stored exception FIRST (set by continuation, immediately visible)
                Exception? storedException = null;
                lock (_loadingExceptionLock)
                {
                    storedException = _loadingException;
                }
                
                if (storedException != null)
                {
                    throw storedException;
                }
                
                // Check if task became faulted (fallback check)
                try
                {
                    if (_loadingTask.IsFaulted)
                    {
                        var exception = _loadingTask.Exception?.GetBaseException();
                        if (exception != null)
                        {
                            throw exception;
                        }
                    }
                }
                catch (Exception ex) when (ex != _loadingTask.Exception?.GetBaseException())
                {
                    // If accessing Exception property throws, continue
                }
                
                // Check if task completed successfully
                if (_loadingTask.IsCompleted && !_loadingTask.IsFaulted)
                {
                    break; // Exit loop - task completed successfully
                }
                
                // Task is still running - small delay to avoid busy-waiting
                System.Threading.Thread.Sleep(50);
            }
            
            // Check for timeout
            if (!_loadingTask.IsCompleted && (DateTime.Now - startTime) >= timeout)
            {
                ReportProgress("Model loading timed out after 60 seconds", 100);
                throw new TimeoutException($"Model loading timed out after 60 seconds. The model file may be corrupted or incompatible: {ModelPath}");
            }
            
            // Final check - task completed, verify if it faulted
            if (_loadingTask.IsFaulted && _loadingTask.Exception != null)
            {
                var exception = _loadingTask.Exception.GetBaseException();
                throw exception;
            }
            
            // Task completed successfully - verify by getting result (should not throw now)
            _loadingTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Skip ReportProgress in error path to avoid potential deadlock - just re-throw immediately
            // ReportProgress might try to marshal to UI thread which could be blocked
            
            // Re-throw with more context - this will be caught by the Task.Run wrapper in AIChatService
            throw new Exception($"Failed to load model from '{ModelPath}': {ex.Message}", ex);
        }
    }
    
    private async Task EnsureModelLoadedAsync()
    {
        await _loadingLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_model != null && _executor != null)
                return;
            
            var resolvedModelPath = ResolveModelPathForLoad();
            
            if (!File.Exists(resolvedModelPath))
                throw new Exception($"Model file not found: {resolvedModelPath}");
            
            // Get file size for progress estimation and validation
            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(resolvedModelPath);
            }
            catch (Exception ex)
            {
                throw new Exception($"Cannot access model file: {ex.Message}. The file may be locked by another process or you may not have permission to access it.", ex);
            }
            
            // Validate file size
            if (fileInfo.Length == 0)
                throw new Exception($"Model file is empty (0 bytes): {resolvedModelPath}. The file may be corrupted or incomplete.");
            
            if (fileInfo.Length < 1024) // Less than 1KB is suspicious for a model file
                throw new Exception($"Model file is too small ({fileInfo.Length} bytes): {resolvedModelPath}. This is likely not a valid GGUF model file.");
            
            var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);
            
            // Check available memory before attempting to load
            // Models typically need 2-3x their file size in RAM
            try
            {
                var requiredMemoryMB = fileSizeMB * 2.5; // Conservative estimate: 2.5x file size
                var availableMemoryMB = GetAvailableMemoryMB();
                
                if (availableMemoryMB > 0 && availableMemoryMB < requiredMemoryMB)
                {
                    var warning = $"⚠️ Low memory warning: Model requires approximately {requiredMemoryMB:F0} MB of RAM, but only {availableMemoryMB:F0} MB appears to be available. " +
                                 $"Loading may fail. Try closing other applications or using a smaller model.";
                    ReportProgress(warning, 3, isError: false);
                    // Don't throw - just warn, as memory detection isn't always accurate
                }
            }
            catch
            {
                // Ignore memory check errors - it's just a warning
            }
            
            // Validate GGUF format by checking magic bytes and version
            try
            {
                using (var fs = new FileStream(resolvedModelPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var buffer = new byte[8]; // Read magic + version
                    var bytesRead = fs.Read(buffer, 0, 8);
                    if (bytesRead < 4)
                    {
                        throw new Exception($"Model file is too small or corrupted: {resolvedModelPath}. Cannot read file header.");
                    }
                    
                    // GGUF files start with "GGUF" magic bytes
                    var magicString = Encoding.ASCII.GetString(buffer, 0, 4);
                    if (magicString != "GGUF")
                    {
                        throw new Exception($"Invalid GGUF format: {resolvedModelPath}. File does not start with 'GGUF' magic bytes. Found: '{magicString}'. The file may be corrupted or not a valid GGUF model.");
                    }
                    
                    // Check GGUF version (bytes 4-7 are version number, little-endian)
                    if (bytesRead >= 8)
                    {
                        var version = BitConverter.ToUInt32(buffer, 4);
                        // GGUF version 1 and 2 are common, version 3+ may have compatibility issues
                        if (version > 3)
                        {
                            ReportProgress($"⚠️ Warning: GGUF version {version} detected. This version may not be fully compatible with this LLamaSharp version.", 4, isError: false);
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new Exception($"Access denied to model file: {resolvedModelPath}. Please check file permissions.", ex);
            }
            catch (IOException ex)
            {
                throw new Exception($"Cannot read model file: {resolvedModelPath}. The file may be locked by another process or inaccessible. Error: {ex.Message}", ex);
            }
            catch (Exception ex) when (!(ex is Exception && ex.Message.Contains("GGUF")))
            {
                // Re-throw our custom GGUF validation errors, but wrap other exceptions
                throw new Exception($"Error validating model file: {resolvedModelPath}. {ex.Message}", ex);
            }
            
            ReportProgress("Initializing model parameters...", 5);
            
            try
            {
                LlamaCppLog.EnsureQuietByDefault();

                _modelParams = new ModelParams(resolvedModelPath)
                {
                    ContextSize = 4096, // Default context size, can be adjusted
                    GpuLayerCount = GpuLayerCount // Use the property value (0 = CPU, >0 = GPU layers)
                };
                
                ReportProgress($"Loading model from {Path.GetFileName(resolvedModelPath)} ({fileSizeMB:F1} MB)...", 10);
                
                // Load model on background thread to keep UI responsive
                // Since LLamaWeights.LoadFromFile doesn't provide progress callbacks,
                // we simulate progress based on elapsed time
                _progressCancellation = new CancellationTokenSource();
                var progressUpdateTask = Task.Run(async () =>
                {
                    var startTime = DateTime.Now;
                    var estimatedDuration = TimeSpan.FromSeconds(Math.Max(5, fileSizeMB / 10)); // Rough estimate: 10 MB/sec
                    
                    try
                    {
                        while (!_progressCancellation.Token.IsCancellationRequested && !_disposed)
                        {
                            await Task.Delay(100, _progressCancellation.Token); // Update every 100ms
                            var elapsed = DateTime.Now - startTime;
                            var progress = Math.Min(90, 10 + (int)((elapsed.TotalSeconds / estimatedDuration.TotalSeconds) * 80));
                            ReportProgress($"Loading model... ({progress}%)", progress);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation is requested
                    }
                }, _progressCancellation.Token);
                
                // Actually load the model on a background thread with timeout
                // Use a timeout to prevent indefinite hanging (30 seconds should be enough for most models)
                var loadTask = Task.Run(() =>
                {
                    try
                    {
                        var result = LLamaWeights.LoadFromFile(_modelParams);
                        return result;
                    }
                    catch (Exception loadEx)
                    {
                        // Preserve the original exception with full details
                        // Include exception type and full details to help diagnose the issue
                        var exceptionDetails = new StringBuilder();
                        exceptionDetails.AppendLine($"Exception Type: {loadEx.GetType().FullName}");
                        exceptionDetails.AppendLine($"Message: {loadEx.Message}");
                        
                        // Get the innermost exception for more details
                        var inner = loadEx;
                        while (inner.InnerException != null)
                        {
                            inner = inner.InnerException;
                            exceptionDetails.AppendLine($"Inner Exception Type: {inner.GetType().FullName}");
                            exceptionDetails.AppendLine($"Inner Message: {inner.Message}");
                        }
                        
                        // Include stack trace for debugging (first few lines)
                        if (!string.IsNullOrEmpty(loadEx.StackTrace))
                        {
                            var stackLines = loadEx.StackTrace.Split('\n').Take(5);
                            exceptionDetails.AppendLine($"Stack Trace (first 5 lines):");
                            foreach (var line in stackLines)
                            {
                                exceptionDetails.AppendLine($"  {line.Trim()}");
                            }
                        }
                        
                        throw new Exception($"LLamaSharp error: {loadEx.Message}\n\nDetails:\n{exceptionDetails}", loadEx);
                    }
                });
                
                // Wait with timeout - larger models need more time
                // Calculate timeout based on file size: minimum 60 seconds, add 1 second per 10MB
                var timeoutSeconds = Math.Max(60, 60 + (int)(fileSizeMB / 10));
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
                var completedTask = await Task.WhenAny(loadTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    // Timeout occurred
                    ReportProgress($"Model loading timed out after {timeoutSeconds} seconds", 100, isError: true);
                    throw new TimeoutException($"Model loading timed out after {timeoutSeconds} seconds. The model file ({fileSizeMB:F1} MB) may be too large, corrupted, or incompatible: {resolvedModelPath}");
                }
                
                // loadTask completed (either successfully or with exception)
                // Await loadTask - if it's faulted, this will throw the exception which will be caught by outer try-catch
                _model = await loadTask;
                
                // Cancel progress updates and wait for the task to finish
                _progressCancellation.Cancel();
                try
                {
                    await progressUpdateTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                
                ReportProgress("Initializing executor...", 95);
                
                _executor = new StatelessExecutor(_model, _modelParams);
                
                ReportProgress("Model loaded successfully!", 100);
                
                // Small delay to ensure progress shows 100%
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                // Store exception immediately for polling loop to detect
                var baseException = ex.GetBaseException();
                lock (_loadingExceptionLock)
                {
                    _loadingException = baseException;
                }
                
                // Build detailed error message with diagnostics
                // Walk the exception chain to find the root cause
                var rootException = baseException;
                var currentEx = ex;
                var exceptionChain = new List<(string Type, string Message)>();
                
                while (currentEx != null)
                {
                    if (!string.IsNullOrWhiteSpace(currentEx.Message))
                    {
                        exceptionChain.Add((currentEx.GetType().Name, currentEx.Message));
                    }
                    currentEx = currentEx.InnerException;
                }
                
                // Get the most specific error message (usually the innermost exception)
                var errorMessage = rootException.Message;
                if (string.IsNullOrWhiteSpace(errorMessage) && exceptionChain.Count > 0)
                {
                    errorMessage = exceptionChain[0].Message;
                }
                
                // Extract more specific error information
                var diagnosticInfo = new StringBuilder();
                diagnosticInfo.AppendLine($"Error: {errorMessage}");
                
                // Show exception chain with types if there are multiple levels
                if (exceptionChain.Count > 1)
                {
                    diagnosticInfo.AppendLine();
                    diagnosticInfo.AppendLine("Exception chain:");
                    for (int i = 0; i < exceptionChain.Count; i++)
                    {
                        diagnosticInfo.AppendLine($"  [{i + 1}] {exceptionChain[i].Type}: {exceptionChain[i].Message}");
                    }
                }
                else if (exceptionChain.Count == 1)
                {
                    diagnosticInfo.AppendLine($"Exception Type: {exceptionChain[0].Type}");
                }
                
                // Check if the exception message contains "Details:" which means we have more info
                if (ex.Message.Contains("Details:"))
                {
                    var detailsIndex = ex.Message.IndexOf("Details:");
                    if (detailsIndex >= 0)
                    {
                        var details = ex.Message.Substring(detailsIndex);
                        diagnosticInfo.AppendLine();
                        diagnosticInfo.AppendLine(details);
                    }
                }
                
                // Try to get additional information from the root exception's full string representation
                // This often contains more details than just the Message property
                try
                {
                    var fullExceptionString = rootException.ToString();
                    // Look for specific patterns that might indicate the real issue
                    if (fullExceptionString.Contains("DllNotFoundException") || fullExceptionString.Contains("dll") || fullExceptionString.Contains("native"))
                    {
                        diagnosticInfo.AppendLine();
                        diagnosticInfo.AppendLine("⚠️ Possible native library issue detected!");
                        diagnosticInfo.AppendLine("This may indicate missing or incompatible native LLamaSharp libraries.");
                        diagnosticInfo.AppendLine("Try reinstalling the LLamaSharp NuGet package or checking for platform-specific dependencies.");
                    }
                    else if (fullExceptionString.Contains("AccessViolation") || fullExceptionString.Contains("SEHException"))
                    {
                        diagnosticInfo.AppendLine();
                        diagnosticInfo.AppendLine("⚠️ Memory access violation detected!");
                        diagnosticInfo.AppendLine("This often indicates a memory issue or incompatible model format.");
                    }
                    else if (fullExceptionString.Contains("OutOfMemoryException") || fullExceptionString.Contains("bad_alloc"))
                    {
                        diagnosticInfo.AppendLine();
                        diagnosticInfo.AppendLine("⚠️ Out of memory detected!");
                        diagnosticInfo.AppendLine("The system ran out of memory while loading the model.");
                    }
                }
                catch
                {
                    // Ignore errors when extracting exception details
                }
                
                // Add file information
                try
                {
                    if (File.Exists(resolvedModelPath))
                    {
                        var info = new FileInfo(resolvedModelPath);
                        diagnosticInfo.AppendLine($"File size: {info.Length:N0} bytes ({info.Length / (1024.0 * 1024.0):F2} MB)");
                        diagnosticInfo.AppendLine($"File path: {resolvedModelPath}");
                    }
                }
                catch
                {
                    // Ignore errors when getting file info
                }
                
                // Check for common error patterns and provide suggestions
                var lowerMessage = errorMessage.ToLowerInvariant();
                var allMessages = string.Join(" ", exceptionChain).ToLowerInvariant();
                
                // Check available memory and include in diagnostics
                try
                {
                    var availableMemoryMB = GetAvailableMemoryMB();
                    if (availableMemoryMB > 0)
                    {
                        diagnosticInfo.AppendLine($"Available memory: {availableMemoryMB:F0} MB");
                        diagnosticInfo.AppendLine($"Model size: {fileSizeMB:F1} MB");
                        diagnosticInfo.AppendLine($"Estimated required memory: {fileSizeMB * 2.5:F0} MB (2.5x model size)");
                    }
                }
                catch
                {
                    // Ignore memory check errors
                }
                
                if (lowerMessage.Contains("out of memory") || lowerMessage.Contains("insufficient memory") || 
                    allMessages.Contains("out of memory") || allMessages.Contains("insufficient memory") ||
                    lowerMessage.Contains("bad_alloc") || allMessages.Contains("bad_alloc"))
                {
                    diagnosticInfo.AppendLine();
                    diagnosticInfo.AppendLine("⚠️ Out of memory error detected!");
                    diagnosticInfo.AppendLine("Suggestion: The model may be too large for available memory. Try:");
                    diagnosticInfo.AppendLine("  - Using a smaller quantized model (e.g., Q4_K_M, Q3_K_L, Q2_K)");
                    diagnosticInfo.AppendLine("  - Closing other applications to free memory");
                    diagnosticInfo.AppendLine("  - Using GPU acceleration if available (set GpuLayerCount > 0)");
                    diagnosticInfo.AppendLine("  - Reducing context size (currently 4096)");
                }
                else if (lowerMessage.Contains("corrupted") || lowerMessage.Contains("invalid") || lowerMessage.Contains("format") ||
                         allMessages.Contains("corrupted") || allMessages.Contains("invalid") || allMessages.Contains("format") ||
                         lowerMessage.Contains("gguf") && (lowerMessage.Contains("invalid") || lowerMessage.Contains("error")))
                {
                    diagnosticInfo.AppendLine();
                    diagnosticInfo.AppendLine("Suggestion: The model file may be corrupted or incompatible. Try:");
                    diagnosticInfo.AppendLine("  - Re-downloading the model file");
                    diagnosticInfo.AppendLine("  - Verifying the file is a valid GGUF format");
                    diagnosticInfo.AppendLine("  - Checking if the model is compatible with this version of LLamaSharp");
                    diagnosticInfo.AppendLine("  - Trying a different quantization level");
                }
                else if (lowerMessage.Contains("access") || lowerMessage.Contains("permission") || lowerMessage.Contains("locked") ||
                         allMessages.Contains("access") || allMessages.Contains("permission") || allMessages.Contains("locked"))
                {
                    diagnosticInfo.AppendLine();
                    diagnosticInfo.AppendLine("Suggestion: File access issue. Try:");
                    diagnosticInfo.AppendLine("  - Closing any other applications using the file");
                    diagnosticInfo.AppendLine("  - Checking file permissions");
                    diagnosticInfo.AppendLine("  - Running the application with appropriate permissions");
                }
                else if (lowerMessage.Contains("not found") || lowerMessage.Contains("file") && lowerMessage.Contains("exist") ||
                         allMessages.Contains("not found") || allMessages.Contains("file") && allMessages.Contains("exist"))
                {
                    diagnosticInfo.AppendLine();
                    diagnosticInfo.AppendLine("Suggestion: File not found. Try:");
                    diagnosticInfo.AppendLine("  - Verifying the file path is correct");
                    diagnosticInfo.AppendLine("  - Re-downloading the model if it was moved or deleted");
                }
                else
                {
                    // Check for specific model compatibility issues
                    var modelName = Path.GetFileNameWithoutExtension(resolvedModelPath).ToLowerInvariant();
                    var isGemmaModel = modelName.Contains("gemma");
                    var isGemma3 = modelName.Contains("gemma-3") || modelName.Contains("gemma3");
                    
                    if (isGemmaModel)
                    {
                        diagnosticInfo.AppendLine();
                        diagnosticInfo.AppendLine("⚠️ Gemma Model Detected:");
                        if (isGemma3)
                        {
                            diagnosticInfo.AppendLine("   - Gemma 3 is a very new model (2024) and may have compatibility issues");
                            diagnosticInfo.AppendLine("   - LLamaSharp 0.12.0 may not fully support Gemma 3 yet");
                            diagnosticInfo.AppendLine("   - Try: Use Gemma 2 models instead, or wait for LLamaSharp updates");
                        }
                        else
                        {
                            diagnosticInfo.AppendLine("   - Gemma models may require specific GGUF versions or quantization formats");
                            diagnosticInfo.AppendLine("   - Try: Ensure you're using a compatible GGUF quantization (Q4_K_M, Q5_K_M recommended)");
                        }
                        diagnosticInfo.AppendLine();
                    }
                    
                    // Generic suggestion for unknown errors
                    // Since "Failed to load model" is very generic, provide comprehensive suggestions
                    diagnosticInfo.AppendLine("⚠️ Generic error message detected. Common causes:");
                    diagnosticInfo.AppendLine();
                    diagnosticInfo.AppendLine("1. Memory Issues (most common):");
                    diagnosticInfo.AppendLine("   - Model requires ~2-3x its file size in RAM");
                    diagnosticInfo.AppendLine($"   - Your model ({fileSizeMB:F1} MB) may need ~{fileSizeMB * 2.5:F0} MB free RAM");
                    
                    // Add memory-specific advice if we detected low memory
                    try
                    {
                        var availableMemoryMB = GetAvailableMemoryMB();
                        if (availableMemoryMB > 0 && availableMemoryMB < fileSizeMB * 2.5)
                        {
                            diagnosticInfo.AppendLine($"   - ⚠️ WARNING: Only {availableMemoryMB:F0} MB available, which is likely insufficient!");
                        }
                    }
                    catch
                    {
                        // Ignore
                    }
                    
                    diagnosticInfo.AppendLine("   - Try: Close other applications, use a smaller model, or enable GPU");
                    diagnosticInfo.AppendLine();
                    diagnosticInfo.AppendLine("2. Model Compatibility:");
                    diagnosticInfo.AppendLine("   - Model may be incompatible with this LLamaSharp version (0.12.0)");
                    diagnosticInfo.AppendLine("   - Try: Re-download the model or use a different quantization");
                    diagnosticInfo.AppendLine("   - Check if the GGUF version is supported (v1-v3 are typically safe)");
                    diagnosticInfo.AppendLine("   - Recommended models for LLamaSharp 0.12.0: Llama 2, Mistral, Phi, CodeLlama");
                    diagnosticInfo.AppendLine();
                    diagnosticInfo.AppendLine("3. Native Library Issues:");
                    diagnosticInfo.AppendLine("   - Missing or incompatible native LLamaSharp libraries");
                    diagnosticInfo.AppendLine("   - Try: Reinstall LLamaSharp NuGet package");
                    diagnosticInfo.AppendLine("   - Ensure the correct backend (CPU/CUDA) is installed");
                    diagnosticInfo.AppendLine();
                    diagnosticInfo.AppendLine("4. File Corruption:");
                    diagnosticInfo.AppendLine("   - File may be incomplete or corrupted");
                    diagnosticInfo.AppendLine("   - Try: Re-download the model file");
                    diagnosticInfo.AppendLine("   - Verify the file size matches the expected size");
                }
                
                ReportProgress($"Failed to load model: {errorMessage}", 100, isError: true);
                
                // Re-throw with more context - this will be caught by EnsureModelLoaded's GetResult() call
                throw new Exception($"Failed to load model from '{resolvedModelPath}':\n\n{diagnosticInfo}", ex);
            }
        }
        finally
        {
            _loadingLock.Release();
        }
    }
    
    private void ReportProgress(string message, int percentage, bool isError = false)
    {
        var progress = new ModelLoadingProgress
        {
            Message = message,
            Percentage = percentage
        };
        
        // Report to instance-level event
        OnLoadingProgress?.Invoke(progress);
        
        // Report to global service for UI integration
        ModelLoadingService.ReportProgress(
            ModelPath,
            message,
            percentage,
            percentage < 100,
            isError
        );
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "modelPath")
            return RuntimeValue.String(ModelPath ?? "");
        if (name == "temperature")
            return RuntimeValue.Float(Temperature);
        if (name == "maxTokens")
            return RuntimeValue.Integer(MaxTokens);
        if (name == "gpuLayerCount")
            return RuntimeValue.Integer(GpuLayerCount);
        
        // Handle method access - create a FunctionValue wrapper
        if (name == "complete" || name == "chat" || name == "setTemperature" || name == "setMaxTokens" || name == "setGpuLayerCount")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on LlamaCppClient.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        switch (methodName)
        {
            case "setTemperature":
                if (args.Count != 1 || args[0].Type != ValueType.Float)
                    throw new Exception("setTemperature() expects 1 float argument");
                Temperature = args[0].AsFloat();
                return RuntimeValue.Null();
            
            case "setMaxTokens":
                if (args.Count != 1 || args[0].Type != ValueType.Integer)
                    throw new Exception("setMaxTokens() expects 1 integer argument");
                MaxTokens = args[0].AsInteger();
                return RuntimeValue.Null();
            
            case "setGpuLayerCount":
                if (args.Count != 1 || args[0].Type != ValueType.Integer)
                    throw new Exception("setGpuLayerCount() expects 1 integer argument");
                var layerCount = args[0].AsInteger();
                if (layerCount < 0)
                    throw new Exception("setGpuLayerCount() expects a non-negative integer (0 = CPU only, >0 = GPU layers)");
                // If model is already loaded, we need to reload it with new GPU settings
                if (_model != null)
                {
                    _model.Dispose();
                    _model = null;
                    _executor = null;
                    _modelParams = null;
                }
                GpuLayerCount = layerCount;
                return RuntimeValue.Null();
            
            case "chat":
                if (args.Count < 1)
                    throw new Exception("chat() expects at least 1 argument");
                var messages = args[0];
                var tools = args.Count > 1 ? args[1] : null;
                var responseFormat = args.Count > 2 ? args[2] : null;
                return Chat(messages, tools, responseFormat);
            
            case "complete":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("complete() expects 1 string argument");
                return Complete(args[0].AsString());
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    /// <param name="responseFormat">OpenAI response_format for structured output. Ignored by llama.cpp/LLamaSharp (not supported); no error.</param>
    public RuntimeValue Chat(RuntimeValue messages, RuntimeValue? tools, RuntimeValue? responseFormat = null, LlmRequestOverrides? overrides = null)
    {
        try
        {
            try
            {
                EnsureModelLoaded();
            }
            catch (Exception ensureEx)
            {
                throw; // Re-throw to be caught by outer catch
            }
            
            if (messages.Type != ValueType.Array)
            {
                var errorObj = new JsonObject();
                errorObj.Set("content", RuntimeValue.String("Error: messages must be an array"));
                return RuntimeValue.Object(errorObj);
            }
            
            var messagesList = messages.AsArray();
            
            // Convert messages to a prompt format
            var prompt = BuildPromptFromMessages(messagesList);
            
            var maxTokens = overrides?.MaxTokens ?? MaxTokens;
            var temperature = overrides?.Temperature ?? Temperature;

            // Create inference parameters
            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = new List<string> { "User:", "\nUser:", "Q:", "\n\n" },
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = (float)temperature,
                    TopK = 40,
                    TopP = 0.95f
                }
            };
            
            // Generate response
            var responseBuilder = new StringBuilder();
            var responseEnumerable = _executor!.InferAsync(prompt, inferenceParams, System.Threading.CancellationToken.None);
            
            // Use synchronous enumeration (blocking) since Chat method is synchronous
            var enumerator = responseEnumerable.GetAsyncEnumerator();
            try
            {
                while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                {
                    responseBuilder.Append(enumerator.Current);
                }
            }
            finally
            {
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            
            var responseText = responseBuilder.ToString().Trim();
            
            // Return in OpenAI-compatible format
            var resultObj = new JsonObject();
            resultObj.Set("content", RuntimeValue.String(responseText));
            
            // Note: Tool calls are not fully supported in this MVP implementation
            // LLamaSharp may support function calling in future versions
            // For now, we return an empty tool_calls array if tools were provided
            if (tools != null && tools.Type == ValueType.Array && tools.AsArray().Count > 0)
            {
                // Tools were provided but we can't execute them yet
                // Return empty tool_calls array
                resultObj.Set("tool_calls", RuntimeValue.Array(new List<RuntimeValue>()));
            }
            
            return RuntimeValue.Object(resultObj);
        }
        catch (Exception ex)
        {
            // Re-throw exception so AIChatService can catch it and return a user-friendly error message
            // AIChatService has proper error handling with IsError = true and ErrorMessage
            throw;
        }
    }
    
    public RuntimeValue Complete(string prompt)
    {
        var messages = new List<RuntimeValue>
        {
            RuntimeValue.Object(CreateMessage("user", prompt))
        };
        
        var response = Chat(RuntimeValue.Array(messages), null);
        if (response.Type == ValueType.Object)
        {
            var obj = response.AsObject();
            if (obj is JsonObject jsonObj)
            {
                var content = jsonObj.Get("content", null);
                return content ?? RuntimeValue.Null();
            }
        }
        return RuntimeValue.Null();
    }
    
    private string BuildPromptFromMessages(List<RuntimeValue> messages)
    {
        var promptBuilder = new StringBuilder();
        
        foreach (var msg in messages)
        {
            if (msg.Type != ValueType.Object)
                continue;
            
            var msgObj = msg.AsObject();
            var role = GetStringProperty(msgObj, "role") ?? "user";
            var content = GetStringProperty(msgObj, "content");
            
            if (string.IsNullOrEmpty(content))
                continue;
            
            // Convert role to prompt format
            switch (role.ToLower())
            {
                case "system":
                    // System messages are typically prepended to the prompt
                    promptBuilder.Insert(0, $"{content}\n\n");
                    break;
                
                case "user":
                    promptBuilder.Append($"User: {content}\n");
                    break;
                
                case "assistant":
                    promptBuilder.Append($"Assistant: {content}\n");
                    break;
                
                case "tool":
                    // Tool messages are typically not included in the prompt for local models
                    // Skip them for now
                    break;
                
                default:
                    promptBuilder.Append($"{role}: {content}\n");
                    break;
            }
        }
        
        // Add assistant prefix for response
        promptBuilder.Append("Assistant: ");
        
        return promptBuilder.ToString();
    }
    
    private JsonObject CreateMessage(string role, string content)
    {
        var msg = new JsonObject();
        msg.Set("role", RuntimeValue.String(role));
        msg.Set("content", RuntimeValue.String(content));
        return msg;
    }
    
    private string? GetStringProperty(ObjectInstance obj, string name)
    {
        try
        {
            var prop = obj.Get(name, null);
            return prop?.AsString();
        }
        catch
        {
            return null;
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _progressCancellation?.Cancel();
            _progressCancellation?.Dispose();
            // StatelessExecutor doesn't implement IDisposable, so we just dispose the model
            _model?.Dispose();
        }
    }
}