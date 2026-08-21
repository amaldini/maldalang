// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Builds Desktop Web Preview URLs. The host page lives at the repo root (or
/// <c>.malda-preview/</c>), while <c>assets/...</c> in the program is relative
/// to the open <c>.malda</c> file. A virtual HTTPS host makes <c>fetch</c>
/// work for glTF; <c>assets</c> tells the runtime how to prefix those URLs.
/// </summary>
public static class WebPreviewHostBuilder
{
    public const string VirtualHostName = "malda.preview";

    public static Uri BuildHostUri(
        string hostPath,
        string repoRoot,
        string scriptPath,
        string title,
        string? sourceFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        var fullHostPath = Path.GetFullPath(hostPath);
        var fullRepoRoot = Path.GetFullPath(repoRoot);
        var hostDirectory = Path.GetDirectoryName(fullHostPath)
            ?? throw new InvalidOperationException("Could not resolve the web preview host directory.");

        var relativeScriptPath = ToUrlPath(Path.GetRelativePath(hostDirectory, Path.GetFullPath(scriptPath)));
        var query = $"?script={Uri.EscapeDataString(relativeScriptPath)}&title={Uri.EscapeDataString(title ?? "")}";

        var assetBase = BuildAssetBase(hostDirectory, sourceFilePath);
        if (!string.IsNullOrEmpty(assetBase))
        {
            query += "&assets=" + Uri.EscapeDataString(assetBase);
        }

        // Generated host lives under .malda-preview/; runtime assets stay at repo root.
        if (!string.Equals(hostDirectory, fullRepoRoot, StringComparison.OrdinalIgnoreCase))
        {
            query +=
                "&runtime=" + Uri.EscapeDataString("../Examples/Web/wwwroot/malda-js-runtime.js") +
                "&three=" + Uri.EscapeDataString("../Examples/Web/wwwroot/vendor/three.min.js");
        }

        var hostRelativeToRepo = ToUrlPath(Path.GetRelativePath(fullRepoRoot, fullHostPath));
        if (string.IsNullOrWhiteSpace(hostRelativeToRepo) || hostRelativeToRepo == ".")
        {
            hostRelativeToRepo = Path.GetFileName(fullHostPath);
        }

        return new Uri(new Uri($"https://{VirtualHostName}/"), hostRelativeToRepo + query);
    }

    public static string BuildAssetBase(string hostDirectory, string? sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return "";
        }

        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceFilePath));
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return "";
        }

        var relative = ToUrlPath(Path.GetRelativePath(Path.GetFullPath(hostDirectory), sourceDirectory));
        if (string.IsNullOrWhiteSpace(relative) || relative == ".")
        {
            return "";
        }

        return relative.EndsWith('/') ? relative : relative + "/";
    }

    private static string ToUrlPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "" : path.Replace('\\', '/');
    }
}
