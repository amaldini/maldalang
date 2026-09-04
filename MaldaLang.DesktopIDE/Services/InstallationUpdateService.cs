// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaldaLang.DesktopIDE.Services;

public enum InstallationKind
{
    Unknown,
    SourceTree,
    Distribution
}

public enum UpdateAvailability
{
    Unknown,
    CannotUpdateHere,
    UpToDate,
    UpdateAvailable,
    LocalNewer
}

public sealed record GitHubReleaseAsset(string Name, string BrowserDownloadUrl, long Size);

public sealed record GitHubRelease(string TagName, string HtmlUrl, IReadOnlyList<GitHubReleaseAsset> Assets);

public sealed record InstallationLocation(InstallationKind Kind, string? RootPath);

public sealed record ApplyUpdateRequest(string PayloadRoot, string Destination, string Tag, int WaitPid);

public sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes);

public sealed record UpdateCheckResult(
    UpdateAvailability Availability,
    string CurrentLabel,
    GitHubRelease? Latest,
    GitHubReleaseAsset? WinX64Asset,
    string Message);

/// <summary>
/// Locates a zip-install, reads GitHub Releases, and applies the win-x64
/// distribution the same way <c>scripts/update-local-win-x64-release.ps1</c> does.
/// Running binaries are replaced by a second process started from the extracted payload.
/// </summary>
public sealed class InstallationUpdateService
{
    public const string DefaultRepo = "amaldini/maldalang";
    public const string MarkerFileName = ".malda-release";
    public const string DesktopExeFileName = "MaldaLang.DesktopIDE.exe";

    private static readonly Regex WinX64ZipName = new(
        @"(?i)malda-.*-win-x64\.zip$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HttpClient Http = CreateClient();

    public static string ReleasesPageUrl => $"https://github.com/{DefaultRepo}/releases";

    public static string LatestReleaseApiUrl(string repo = DefaultRepo) =>
        $"https://api.github.com/repos/{repo}/releases/latest";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromHours(2)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("MaldaDesktopIDE", GetRunningProductVersion() ?? "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        TryAddGitHubToken(client.DefaultRequestHeaders);
        return client;
    }

    private static void TryAddGitHubToken(HttpRequestHeaders headers)
    {
        var token = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }
    }

    public static string? GetRunningProductVersion()
    {
        var assembly = typeof(InstallationUpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? null : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public static string NormalizeTag(string? tagOrVersion)
    {
        var value = (tagOrVersion ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            return "v" + value[1..];
        }

        return "v" + value;
    }

    public static Version? TryParseVersion(string? tagOrVersion)
    {
        var value = (tagOrVersion ?? string.Empty).Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            value = value[1..];
        }

        return Version.TryParse(value, out var version) ? CanonicalVersion(version) : null;
    }

    public static int CompareTags(string? left, string? right)
    {
        var leftVersion = TryParseVersion(left);
        var rightVersion = TryParseVersion(right);
        if (leftVersion != null && rightVersion != null)
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.Compare(
            NormalizeTag(left),
            NormalizeTag(right),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeSourceTree(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        return File.Exists(Path.Combine(path, "MaldaLang.sln"))
               || Directory.Exists(Path.Combine(path, "MaldaLang.UIHost"));
    }

    public static bool LooksLikeDistributionRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || LooksLikeSourceTree(path))
        {
            return false;
        }

        var manual = Path.Combine(path, "ReferenceManual", "index.html");
        var desktopExe = Path.Combine(path, "bin", "desktop-ide", DesktopExeFileName);
        return File.Exists(manual) && File.Exists(desktopExe);
    }

    public static InstallationLocation Locate(string? startDirectory = null)
    {
        var walked = WalkForInstallRoot(startDirectory ?? AppContext.BaseDirectory);
        if (walked.Kind != InstallationKind.Unknown)
        {
            return walked;
        }

        var home = Environment.GetEnvironmentVariable("MALDA_HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            return walked;
        }

        try
        {
            var full = Path.GetFullPath(home.Trim());
            if (LooksLikeSourceTree(full))
            {
                return new InstallationLocation(InstallationKind.SourceTree, full);
            }

            if (LooksLikeDistributionRoot(full))
            {
                return new InstallationLocation(InstallationKind.Distribution, full);
            }
        }
        catch (Exception)
        {
            // Ignore a malformed MALDA_HOME.
        }

        return walked;
    }

    public static string ReadInstalledTag(string? destination, string? assemblyVersionFallback = null)
    {
        if (!string.IsNullOrWhiteSpace(destination))
        {
            var markerPath = Path.Combine(destination, MarkerFileName);
            if (File.Exists(markerPath))
            {
                var marker = File.ReadAllText(markerPath).Trim();
                if (marker.Length > 0)
                {
                    return NormalizeTag(marker);
                }
            }
        }

        var fallback = assemblyVersionFallback ?? GetRunningProductVersion();
        return string.IsNullOrWhiteSpace(fallback) ? string.Empty : NormalizeTag(fallback);
    }

    public static GitHubRelease ParseReleaseJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? string.Empty : string.Empty;
        var html = root.TryGetProperty("html_url", out var htmlElement) ? htmlElement.GetString() ?? string.Empty : string.Empty;
        var assets = new List<GitHubReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes)
                    ? bytes
                    : 0;
                assets.Add(new GitHubReleaseAsset(name, url, size));
            }
        }

        return new GitHubRelease(tag, html, assets);
    }

    public static GitHubReleaseAsset? FindWinX64Asset(GitHubRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return release.Assets.FirstOrDefault(asset => WinX64ZipName.IsMatch(asset.Name));
    }

    public static string ResolveExtractedRoot(string extractDir)
    {
        if (string.IsNullOrWhiteSpace(extractDir) || !Directory.Exists(extractDir))
        {
            throw new InvalidOperationException("Extracted zip folder is missing.");
        }

        var children = Directory.GetFileSystemEntries(extractDir);
        if (children.Length == 1 && Directory.Exists(children[0]))
        {
            var candidate = children[0];
            if (LooksLikeExtractedPayload(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        if (LooksLikeExtractedPayload(extractDir))
        {
            return Path.GetFullPath(extractDir);
        }

        throw new InvalidOperationException(
            "Extracted zip does not look like a MALDA win-x64 distribution (missing bin\\desktop-ide or bin\\malda).");
    }

    public static UpdateCheckResult Evaluate(InstallationLocation location, string currentTag, GitHubRelease? latest)
    {
        if (location.Kind == InstallationKind.SourceTree)
        {
            return new UpdateCheckResult(
                UpdateAvailability.CannotUpdateHere,
                currentTag,
                latest,
                latest is null ? null : FindWinX64Asset(latest),
                "This Desktop IDE is running from a source checkout. Use git pull, or unzip a GitHub Release and update that copy.");
        }

        if (location.Kind != InstallationKind.Distribution || string.IsNullOrWhiteSpace(location.RootPath))
        {
            return new UpdateCheckResult(
                UpdateAvailability.CannotUpdateHere,
                currentTag,
                latest,
                latest is null ? null : FindWinX64Asset(latest),
                "Could not find a MALDA zip install (a folder with ReferenceManual and bin\\desktop-ide). Download a win-x64 release, unzip it, and run the IDE from that folder.");
        }

        if (latest is null)
        {
            return new UpdateCheckResult(
                UpdateAvailability.Unknown,
                currentTag,
                null,
                null,
                "Could not read the latest GitHub release.");
        }

        var asset = FindWinX64Asset(latest);
        if (asset is null)
        {
            return new UpdateCheckResult(
                UpdateAvailability.Unknown,
                currentTag,
                latest,
                null,
                $"Release {latest.TagName} has no malda-*-win-x64.zip asset.");
        }

        var latestTag = NormalizeTag(latest.TagName);
        if (string.IsNullOrWhiteSpace(currentTag))
        {
            return new UpdateCheckResult(
                UpdateAvailability.UpdateAvailable,
                currentTag,
                latest,
                asset,
                $"Latest release is {latestTag}. The installed version could not be determined.");
        }

        var comparison = CompareTags(currentTag, latestTag);
        if (comparison < 0)
        {
            return new UpdateCheckResult(
                UpdateAvailability.UpdateAvailable,
                currentTag,
                latest,
                asset,
                $"A newer release is available: {latestTag}.");
        }

        if (comparison > 0)
        {
            return new UpdateCheckResult(
                UpdateAvailability.LocalNewer,
                currentTag,
                latest,
                asset,
                $"This install ({currentTag}) is newer than the latest GitHub release ({latestTag}). You can still reinstall the published zip.");
        }

        return new UpdateCheckResult(
            UpdateAvailability.UpToDate,
            currentTag,
            latest,
            asset,
            $"Already at {latestTag}.");
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    public static bool TryParseApplyRequest(IReadOnlyList<string> args, out ApplyUpdateRequest? request, out string? error)
    {
        request = null;
        error = null;
        if (args.All(arg => !string.Equals(arg, "--apply-update", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string? payload = null;
        string? destination = null;
        string? tag = null;
        var waitPid = 0;

        for (var i = 0; i < args.Count; i++)
        {
            if (TryReadOption(args, ref i, "--payload", out var payloadValue))
            {
                payload = payloadValue;
            }
            else if (TryReadOption(args, ref i, "--destination", out var destinationValue))
            {
                destination = destinationValue;
            }
            else if (TryReadOption(args, ref i, "--tag", out var tagValue))
            {
                tag = tagValue;
            }
            else if (TryReadOption(args, ref i, "--wait-pid", out var pidValue)
                     && int.TryParse(pidValue, out var parsedPid))
            {
                waitPid = parsedPid;
            }
        }

        if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(tag))
        {
            error = "Update apply arguments are incomplete (--payload, --destination, and --tag are required).";
            return true;
        }

        request = new ApplyUpdateRequest(payload, destination, tag, waitPid);
        return true;
    }

    public static Process StartApplyProcess(ApplyUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payloadRoot = ResolveExtractedRoot(request.PayloadRoot);
        var newExe = Path.Combine(payloadRoot, "bin", "desktop-ide", DesktopExeFileName);
        if (!File.Exists(newExe))
        {
            throw new InvalidOperationException("The downloaded release does not include the Desktop IDE.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = newExe,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(newExe)
        };
        startInfo.ArgumentList.Add("--apply-update");
        startInfo.ArgumentList.Add("--payload");
        startInfo.ArgumentList.Add(payloadRoot);
        startInfo.ArgumentList.Add("--destination");
        startInfo.ArgumentList.Add(request.Destination);
        startInfo.ArgumentList.Add("--tag");
        startInfo.ArgumentList.Add(request.Tag);
        startInfo.ArgumentList.Add("--wait-pid");
        startInfo.ArgumentList.Add(request.WaitPid.ToString());

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("Could not start the update process.");
    }

    public static void WaitForProcessExit(int pid, TimeSpan timeout)
    {
        if (pid <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.WaitForExit((int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue)))
            {
                throw new TimeoutException($"Timed out waiting for process {pid} to exit.");
            }
        }
        catch (ArgumentException)
        {
            // Already exited.
        }

        Thread.Sleep(400);
    }

    public static void ApplyExtractedRelease(string payloadRoot, string destination, string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        destination = Path.GetFullPath(destination);
        if (LooksLikeSourceTree(destination))
        {
            throw new InvalidOperationException("Refusing to overwrite a source checkout.");
        }

        var resolved = ResolveExtractedRoot(payloadRoot);
        Directory.CreateDirectory(destination);

        var cacheDir = Path.Combine(destination, ".cache");
        Directory.CreateDirectory(cacheDir);

        var sourceBin = Path.Combine(resolved, "bin");
        var destinationBin = Path.Combine(destination, "bin");
        string? backup = null;

        try
        {
            if (Directory.Exists(sourceBin))
            {
                if (Directory.Exists(destinationBin))
                {
                    backup = Path.Combine(cacheDir, "bin-backup-" + Guid.NewGuid().ToString("N"));
                    Directory.Move(destinationBin, backup);
                }

                CopyDirectory(sourceBin, destinationBin);
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(resolved))
            {
                var name = Path.GetFileName(entry);
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || name.Equals(".cache", StringComparison.OrdinalIgnoreCase)
                    || name.Equals(MarkerFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = Path.Combine(destination, name);
                if (Directory.Exists(entry))
                {
                    CopyDirectoryMerge(entry, target);
                }
                else
                {
                    var parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    File.Copy(entry, target, overwrite: true);
                }
            }

            File.WriteAllText(Path.Combine(destination, MarkerFileName), NormalizeTag(tag) + Environment.NewLine);

            if (backup != null && Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }
        }
        catch
        {
            if (backup != null && Directory.Exists(backup))
            {
                if (Directory.Exists(destinationBin))
                {
                    Directory.Delete(destinationBin, recursive: true);
                }

                Directory.Move(backup, destinationBin);
            }

            throw;
        }
    }

    public static void CleanupStaleCache(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        var cacheDir = Path.Combine(destination, ".cache");
        if (!Directory.Exists(cacheDir))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(cacheDir))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith("extract-", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("bin-backup-", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception)
                {
                    // The apply process may still be running from an extract folder.
                }
            }
        }

        foreach (var zip in Directory.EnumerateFiles(cacheDir, "malda-*-win-x64.zip"))
        {
            try
            {
                File.Delete(zip);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }

    public static string DesktopExePath(string installRoot) =>
        Path.Combine(installRoot, "bin", "desktop-ide", DesktopExeFileName);

    public async Task<GitHubRelease> FetchLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(LatestReleaseApiUrl(), cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub releases request failed ({(int)response.StatusCode}). {TrimErrorBody(body)}");
        }

        return ParseReleaseJson(body);
    }

    public async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new UpdateDownloadProgress(received, total));
        }
    }

    public static void ExtractZip(string zipPath, string extractDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractDirectory);
        if (Directory.Exists(extractDirectory))
        {
            Directory.Delete(extractDirectory, recursive: true);
        }

        Directory.CreateDirectory(extractDirectory);
        ZipFile.ExtractToDirectory(zipPath, extractDirectory, overwriteFiles: true);
    }

    private static InstallationLocation WalkForInstallRoot(string startDirectory)
    {
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (current != null)
            {
                if (LooksLikeSourceTree(current.FullName))
                {
                    return new InstallationLocation(InstallationKind.SourceTree, current.FullName);
                }

                if (LooksLikeDistributionRoot(current.FullName))
                {
                    return new InstallationLocation(InstallationKind.Distribution, current.FullName);
                }

                current = current.Parent;
            }
        }
        catch (Exception)
        {
            // Best effort.
        }

        return new InstallationLocation(InstallationKind.Unknown, null);
    }

    private static bool LooksLikeExtractedPayload(string path)
    {
        return File.Exists(Path.Combine(path, "bin", "desktop-ide", DesktopExeFileName))
               || File.Exists(Path.Combine(path, "bin", "malda", "malda.exe"));
    }

    private static Version CanonicalVersion(Version version) =>
        new(
            version.Major,
            version.Minor,
            version.Build < 0 ? 0 : version.Build,
            version.Revision < 0 ? 0 : version.Revision);

    private static bool TryReadOption(
        IReadOnlyList<string> args,
        ref int index,
        string name,
        out string? value)
    {
        value = null;
        if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (index + 1 >= args.Count)
        {
            return true;
        }

        index++;
        value = args[index];
        return true;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void CopyDirectoryMerge(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectoryMerge(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static string TrimErrorBody(string body)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length > 240)
        {
            trimmed = trimmed[..240] + "…";
        }

        return trimmed.ReplaceLineEndings(" ");
    }
}
