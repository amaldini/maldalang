// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;
using LLama;
using LLama.Common;
using System.Threading;

/// <summary>
/// LLamaSharp embedder for generating neural network embeddings from text.
/// </summary>
public class LlamaEmbedderInstance : ObjectInstance, IDisposable
{
    public string ModelPath { get; set; }
    public int GpuLayerCount { get; set; } = 0; // 0 = CPU only, >0 = number of layers to offload to GPU
    
    private LLamaWeights? _model;
    private LLamaEmbedder? _embedder;
    private ModelParams? _modelParams;
    private bool _disposed = false;
    private Task? _loadingTask;
    private readonly SemaphoreSlim _loadingLock = new SemaphoreSlim(1, 1);
    private Exception? _loadingException = null; // Store exception for immediate access
    private readonly object _loadingExceptionLock = new object();
    
    public LlamaEmbedderInstance() : base(null)
    {
        ModelPath = "";
        // Try to auto-detect GPU support if available
        GpuLayerCount = TryDetectGpuSupport();
    }
    
    /// <summary>
    /// Attempts to detect if GPU support is available. Returns 0 if CPU-only or if detection fails.
    /// </summary>
    private int TryDetectGpuSupport()
    {
        try
        {
            // Default to CPU - users must explicitly enable GPU or install CUDA backends
            return 0;
        }
        catch
        {
            return 0; // Fallback to CPU on any error
        }
    }
    
    private void EnsureModelLoaded()
    {
        // If already loaded, return immediately
        if (_model != null && _embedder != null)
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
            var timeout = TimeSpan.FromSeconds(60);
            var startTime = DateTime.Now;
            
            while ((DateTime.Now - startTime) < timeout)
            {
                // Force memory barrier to ensure we see latest task state
                System.Threading.Thread.MemoryBarrier();
                
                // Check stored exception FIRST
                Exception? storedException = null;
                lock (_loadingExceptionLock)
                {
                    storedException = _loadingException;
                }
                
                if (storedException != null)
                {
                    throw storedException;
                }
                
                // Check if task became faulted
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
                    break;
                }
                
                // Task is still running - small delay to avoid busy-waiting
                System.Threading.Thread.Sleep(50);
            }
            
            // Check for timeout
            if (!_loadingTask.IsCompleted && (DateTime.Now - startTime) >= timeout)
            {
                throw new TimeoutException($"Model loading timed out after 60 seconds. The model file may be corrupted or incompatible: {ModelPath}");
            }
            
            // Final check - task completed, verify if it faulted
            if (_loadingTask.IsFaulted && _loadingTask.Exception != null)
            {
                var exception = _loadingTask.Exception.GetBaseException();
                throw exception;
            }
            
            // Task completed successfully
            _loadingTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to load embedding model from '{ModelPath}': {ex.Message}", ex);
        }
    }
    
    private async Task EnsureModelLoadedAsync()
    {
        await _loadingLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_model != null && _embedder != null)
                return;
            
            if (string.IsNullOrEmpty(ModelPath))
                throw new Exception("ModelPath is not set");
            
            if (!File.Exists(ModelPath))
                throw new Exception($"Model file not found: {ModelPath}");
            
            // Get file size for validation
            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(ModelPath);
            }
            catch (Exception ex)
            {
                throw new Exception($"Cannot access model file: {ex.Message}. The file may be locked by another process or you may not have permission to access it.", ex);
            }
            
            // Validate file size
            if (fileInfo.Length == 0)
                throw new Exception($"Model file is empty (0 bytes): {ModelPath}. The file may be corrupted or incomplete.");
            
            if (fileInfo.Length < 1024)
                throw new Exception($"Model file is too small ({fileInfo.Length} bytes): {ModelPath}. This is likely not a valid GGUF model file.");
            
            // Validate GGUF format
            try
            {
                using (var fs = new FileStream(ModelPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var buffer = new byte[8];
                    var bytesRead = fs.Read(buffer, 0, 8);
                    if (bytesRead < 4)
                    {
                        throw new Exception($"Model file is too small or corrupted: {ModelPath}. Cannot read file header.");
                    }
                    
                    var magicString = Encoding.ASCII.GetString(buffer, 0, 4);
                    if (magicString != "GGUF")
                    {
                        throw new Exception($"Invalid GGUF format: {ModelPath}. File does not start with 'GGUF' magic bytes.");
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new Exception($"Access denied to model file: {ModelPath}. Please check file permissions.", ex);
            }
            catch (IOException ex)
            {
                throw new Exception($"Cannot read model file: {ModelPath}. The file may be locked by another process. Error: {ex.Message}", ex);
            }
            
            try
            {
                LlamaCppLog.EnsureQuietByDefault();

                // Create model parameters
                // Note: EmbeddingMode is not needed in LLamaSharp 0.25.0 - LLamaEmbedder handles it automatically
                _modelParams = new ModelParams(ModelPath)
                {
                    GpuLayerCount = GpuLayerCount
                };
                
                // Load model
                _model = LLamaWeights.LoadFromFile(_modelParams);
                
                // Create embedder
                _embedder = new LLamaEmbedder(_model, _modelParams);
            }
            catch (Exception ex)
            {
                var exceptionDetails = new StringBuilder();
                exceptionDetails.AppendLine($"Exception Type: {ex.GetType().FullName}");
                exceptionDetails.AppendLine($"Message: {ex.Message}");
                
                var inner = ex;
                while (inner.InnerException != null)
                {
                    inner = inner.InnerException;
                    exceptionDetails.AppendLine($"Inner Exception Type: {inner.GetType().FullName}");
                    exceptionDetails.AppendLine($"Inner Message: {inner.Message}");
                }
                
                throw new Exception($"Failed to load embedding model:\n\n{exceptionDetails}", ex);
            }
        }
        finally
        {
            _loadingLock.Release();
        }
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "modelPath")
            return RuntimeValue.String(ModelPath ?? "");
        if (name == "gpuLayerCount")
            return RuntimeValue.Integer(GpuLayerCount);
        
        // Handle method access - create a FunctionValue wrapper
        if (name == "getEmbeddings" || name == "setGpuLayerCount")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on LlamaEmbedder.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        switch (methodName)
        {
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
                    _embedder = null;
                    _modelParams = null;
                }
                GpuLayerCount = layerCount;
                return RuntimeValue.Null();
            
            case "getEmbeddings":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("getEmbeddings() expects 1 string argument");
                return GetEmbeddings(args[0].AsString());
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    public RuntimeValue GetEmbeddings(string text)
    {
        try
        {
            EnsureModelLoaded();
            
            if (_embedder == null)
                throw new Exception("Embedder is not initialized");
            
            // Get embeddings from LLamaSharp
            // GetEmbeddings is async and returns Task<IReadOnlyList<float[]>>
            // In LLamaSharp 0.25.0, it only accepts the text parameter
            var embeddingsTask = _embedder.GetEmbeddings(text);
            var embeddings = embeddingsTask.Result;
            
            // Convert IReadOnlyList<float[]> to RuntimeValue.Array
            // The result is a list of embedding vectors, we take the first one
            var result = new List<RuntimeValue>();
            if (embeddings != null && embeddings.Count > 0)
            {
                // Get the first embedding vector (float[])
                var embeddingVector = embeddings[0];
                foreach (var val in embeddingVector)
                {
                    result.Add(RuntimeValue.Float(val));
                }
            }
            
            return RuntimeValue.Array(result);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get embeddings: {ex.Message}", ex);
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _embedder = null;
            _model?.Dispose();
            _model = null;
            _modelParams = null;
        }
    }
}
