// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

/// <summary>
/// Provides a default local LLM client with optional auto-download of the
/// default Hugging Face GGUF model. Used when no explicit LLM client is set
/// (prompts, agents, etc.). The default model is chosen for small size and
/// chat/tool-friendly behavior.
/// Optionally override via environment variable <see cref="DefaultLocalModelEnvVar"/>.
/// </summary>
public static class DefaultLocalLlm
{
    /// <summary>Environment variable to optionally configure a different local model. Format: <c>modelId</c> (e.g. <c>org/repo</c>) or <c>modelId/fileName.gguf</c>. When set, that model is downloaded and used instead of the built-in default.</summary>
    public const string DefaultLocalModelEnvVar = "MALDA_DEFAULT_LOCAL_MODEL";

    /// <summary>Hugging Face model ID for the default local GGUF build of Qwen/Qwen2.5-0.5B-Instruct.</summary>
    public const string DefaultModelId = "Qwen/Qwen2.5-0.5B-Instruct-GGUF";

    /// <summary>GGUF file name (Q4_K_M quant for balance of size and quality).</summary>
    public const string DefaultModelFileName = "qwen2.5-0.5b-instruct-q4_k_m.gguf";

    private const string HuggingFaceResolveBase = "https://huggingface.co";
    private static readonly HttpClient HttpClient = new HttpClient();
    private static LlamaCppClientInstance? _cachedClient;
    private static string? _cachedClientKey;
    private static readonly object CacheLock = new object();

    static DefaultLocalLlm()
    {
        HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MaldaLang/1.0");
        HttpClient.Timeout = TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Parses the default local model from the environment variable. Returns (modelId, fileName).
    /// If the env var is not set or empty, returns the built-in default. Format when set:
    /// <c>modelId</c> (uses built-in default file name for that repo) or <c>modelId/fileName.gguf</c>.
    /// </summary>
    public static (string modelId, string fileName) GetDefaultLocalModelFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(DefaultLocalModelEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
            return (DefaultModelId, DefaultModelFileName);

        raw = raw!.Trim();
        var lastSlash = raw.LastIndexOf('/');
        if (lastSlash >= 0 && raw.IndexOf(".gguf", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var modelId = raw.Substring(0, lastSlash);
            var fileName = raw.Substring(lastSlash + 1);
            if (!string.IsNullOrWhiteSpace(fileName))
                return (modelId, fileName);
        }
        return (raw, DefaultModelFileName);
    }

    /// <summary>
    /// Returns the default local LLM client (LlamaCppClientInstance). On first use,
    /// ensures the default GGUF model is present in the cache, downloading from
    /// Hugging Face if necessary. Respects <see cref="DefaultLocalModelEnvVar"/> when set.
    /// </summary>
    public static LlamaCppClientInstance GetDefaultLocalClient()
    {
        lock (CacheLock)
        {
            var (modelId, fileName) = GetDefaultLocalModelFromEnvironment();
            var key = $"{modelId}|{fileName}";
            if (_cachedClient != null && _cachedClientKey == key)
                return _cachedClient;

            var path = GetOrDownloadDefaultModelPath();
            _cachedClientKey = key;
            _cachedClient = new LlamaCppClientInstance { ModelPath = path };
            return _cachedClient;
        }
    }

    /// <summary>
    /// Returns the cache directory used for the default model
    /// (e.g. %LOCALAPPDATA%\MaldaLang\Models\default on Windows).
    /// For a custom model (when <see cref="DefaultLocalModelEnvVar"/> is set), returns a subdir named by model ID.
    /// </summary>
    public static string GetDefaultModelsDirectory(string? modelId = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var segment = string.IsNullOrEmpty(modelId) || modelId == DefaultModelId
            ? "default"
            : string.Join("_", modelId.Split(Path.GetInvalidFileNameChars()));
        var dir = Path.Combine(appData, "MaldaLang", "Models", segment);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Full path to the default model file. If the file does not exist, downloads it from Hugging Face.
    /// Uses <see cref="DefaultLocalModelEnvVar"/> when set.
    /// </summary>
    public static string GetOrDownloadDefaultModelPath()
    {
        var (modelId, fileName) = GetDefaultLocalModelFromEnvironment();
        var dir = GetDefaultModelsDirectory(modelId);
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path))
            return path;

        DownloadDefaultModelAsync(progress: null, modelId: modelId, fileName: fileName).GetAwaiter().GetResult();
        if (!File.Exists(path))
            throw new InvalidOperationException($"Default model download failed: expected file at {path}");
        return path;
    }

    /// <summary>
    /// Downloads the default GGUF model from Hugging Face to the cache directory.
    /// Uses built-in default model unless <paramref name="modelId"/> and <paramref name="fileName"/> are provided.
    /// </summary>
    public static async Task DownloadDefaultModelAsync(
        IProgress<(long bytesReceived, long? totalBytes)>? progress = null,
        string? modelId = null,
        string? fileName = null)
    {
        if (string.IsNullOrEmpty(modelId)) modelId = DefaultModelId;
        if (string.IsNullOrEmpty(fileName)) fileName = DefaultModelFileName;
        var dir = GetDefaultModelsDirectory(modelId);
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path))
            return;

        var url = $"{HuggingFaceResolveBase}/{modelId}/resolve/main/{Uri.EscapeDataString(fileName)}";
        if (await TryDownloadWithCurlAsync(url, path).ConfigureAwait(false))
            return;

        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var file = File.Create(path);
        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            totalRead += read;
            progress?.Report((totalRead, total));
        }
    }

    private static async Task<bool> TryDownloadWithCurlAsync(string url, string path)
    {
        var tempPath = path + "." + Process.GetCurrentProcess().Id + ".download";
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = $"-L --fail --output \"{tempPath}\" \"{url}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0 || !File.Exists(tempPath))
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                return false;
            }

            if (File.Exists(path))
            {
                File.Delete(tempPath);
                return true;
            }

            File.Move(tempPath, path, overwrite: true);
            return true;
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            return false;
        }
    }

    /// <summary>
    /// Clears the cached default client (e.g. after changing model path or env var). Next call to
    /// GetDefaultLocalClient() will create a new instance.
    /// </summary>
    public static void ClearCache()
    {
        lock (CacheLock)
        {
            _cachedClient = null;
            _cachedClientKey = null;
        }
    }
}
