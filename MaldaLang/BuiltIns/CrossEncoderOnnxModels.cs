// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

/// <summary>
/// Downloads and caches the default Hugging Face cross-encoder ONNX model used by
/// GraphMemory rerank (<c>rerankMode: onnx</c>).
/// </summary>
public static class CrossEncoderOnnxModels
{
    /// <summary>Environment variable to override the Hugging Face model id (e.g. <c>cross-encoder/ms-marco-MiniLM-L6-v2</c>).</summary>
    public const string ModelIdEnvVar = "MALDA_CROSS_ENCODER_MODEL";

    /// <summary>Default cross-encoder for memory rerank.</summary>
    public const string DefaultModelId = "cross-encoder/ms-marco-MiniLM-L6-v2";

    /// <summary>ONNX file path inside the Hugging Face repository.</summary>
    public const string DefaultOnnxRemotePath = "onnx/model.onnx";

    /// <summary>Local ONNX file name inside the cache directory.</summary>
    public const string LocalOnnxFileName = "model.onnx";

    /// <summary>Local vocab file name inside the cache directory.</summary>
    public const string LocalVocabFileName = "vocab.txt";

    /// <summary>Default config path string for <c>agents.memory.rerankModelPath</c>.</summary>
    public const string DefaultConfigPath = "~/.malda/models/cross-encoder";

    private const string HuggingFaceResolveBase = "https://huggingface.co";
    private static readonly HttpClient HttpClient = new HttpClient();

    static CrossEncoderOnnxModels()
    {
        HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MaldaLang/1.0");
        HttpClient.Timeout = TimeSpan.FromMinutes(30);
    }

    public static string GetModelIdFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(ModelIdEnvVar);
        return string.IsNullOrWhiteSpace(raw) ? DefaultModelId : raw.Trim();
    }

    public static string GetMaldaHomeDirectory(string? maldaHome = null)
    {
        if (!string.IsNullOrWhiteSpace(maldaHome))
            return Path.GetFullPath(maldaHome);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            return Path.Combine(Directory.GetCurrentDirectory(), ".malda");
        return Path.Combine(userProfile, ".malda");
    }

    public static string GetDefaultModelDirectory(string? maldaHome = null)
    {
        var dir = Path.Combine(GetMaldaHomeDirectory(maldaHome), "models", "cross-encoder");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    public static bool IsInstalled(string? directory = null)
    {
        directory ??= GetDefaultModelDirectory();
        return File.Exists(Path.Combine(directory, LocalOnnxFileName))
            && File.Exists(Path.Combine(directory, LocalVocabFileName));
    }

    public static string ExpandMaldaPath(string? value, string? maldaHome = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (expanded.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            expanded.StartsWith("~" + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                expanded = Path.Combine(home, expanded.Substring(2));
        }
        else if (string.Equals(expanded, "~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                expanded = home;
        }

        _ = maldaHome;
        return Path.GetFullPath(expanded);
    }

    public static string? ResolveRerankModelPath(string? configuredPath, string? maldaHome = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expanded = ExpandMaldaPath(configuredPath, maldaHome);
            if (!string.IsNullOrWhiteSpace(expanded))
                return expanded;
        }

        var defaultDir = GetDefaultModelDirectory(maldaHome);
        return IsInstalled(defaultDir) ? defaultDir : null;
    }

    public static string GetOrDownloadDefaultModelDirectory(string? maldaHome = null)
    {
        var dir = GetDefaultModelDirectory(maldaHome);
        if (IsInstalled(dir))
            return dir;

        EnsureDownloadedAsync(maldaHome: maldaHome).GetAwaiter().GetResult();
        if (!IsInstalled(dir))
            throw new InvalidOperationException($"Cross-encoder download failed: expected model.onnx and vocab.txt in {dir}");
        return dir;
    }

    public static async Task EnsureDownloadedAsync(
        IProgress<(string fileName, long bytesReceived, long? totalBytes)>? progress = null,
        string? maldaHome = null,
        string? modelId = null)
    {
        modelId ??= GetModelIdFromEnvironment();
        var dir = GetDefaultModelDirectory(maldaHome);
        var onnxPath = Path.Combine(dir, LocalOnnxFileName);
        var vocabPath = Path.Combine(dir, LocalVocabFileName);

        if (File.Exists(onnxPath) && File.Exists(vocabPath))
            return;

        await DownloadFileAsync(modelId, DefaultOnnxRemotePath, onnxPath, progress).ConfigureAwait(false);
        await DownloadFileAsync(modelId, LocalVocabFileName, vocabPath, progress).ConfigureAwait(false);
    }

    private static async Task DownloadFileAsync(
        string modelId,
        string remotePath,
        string destinationPath,
        IProgress<(string fileName, long bytesReceived, long? totalBytes)>? progress)
    {
        if (File.Exists(destinationPath))
            return;

        var url = $"{HuggingFaceResolveBase}/{modelId}/resolve/main/{remotePath}";
        if (await TryDownloadWithCurlAsync(url, destinationPath).ConfigureAwait(false))
        {
            progress?.Report((Path.GetFileName(destinationPath), File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0, null));
            return;
        }

        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var file = File.Create(destinationPath);
        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            totalRead += read;
            progress?.Report((Path.GetFileName(destinationPath), totalRead, total));
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
}
