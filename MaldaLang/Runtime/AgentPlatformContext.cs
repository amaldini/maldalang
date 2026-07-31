// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime;

using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// Host OS facts injected into DevAgent prompts so LLMs pick correct shell/path conventions.
/// </summary>
public static class AgentPlatformContext
{
    public static string OsFamily =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
        "unknown";

    public static string DescribeForAgentPrompt(string? agentWorkingDirectory = null)
    {
        var os = OsFamily;
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var desc = RuntimeInformation.OSDescription;
        var cwd = System.Environment.CurrentDirectory;
        var agentDir = NormalizeDirectoryPath(agentWorkingDirectory);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n\n## Host environment");
        sb.AppendLine($"OS: {os} ({desc}, {arch}).");
        if (agentDir != null && !PathsEqual(agentDir, cwd))
        {
            sb.AppendLine($"Agent project directory (file + git tools): {agentDir}.");
            sb.AppendLine($"Process launch directory: {cwd} — not the agent workdir; do not use for file paths or git repoPath.");
        }
        else if (agentDir != null)
        {
            sb.AppendLine($"Agent project directory: {agentDir}.");
        }
        else
        {
            sb.AppendLine($"Process working directory: {cwd}.");
        }

        if (os == "windows")
        {
            sb.AppendLine("Shell: cmd.exe uses /c (not bash -c). Example: run_command cmd with args [\"/c\", \"dotnet\", \"build\"].");
            sb.AppendLine("Paths use backslashes; prefer list_directory / read_file tools over dir/ls/findstr/powershell.");
            sb.AppendLine("pwd and cd are handled in-process — do not spawn cmd to print %CD%.");
        }
        else if (os == "linux")
        {
            sb.AppendLine("Shell: bash/sh use -c. Example: run_command bash with args [\"-c\", \"dotnet build\"].");
            sb.AppendLine("Paths use forward slashes; ls/find may exist but prefer list_directory and grep tools.");
        }
        else if (os == "macos")
        {
            sb.AppendLine("Shell: bash/zsh use -c. Prefer list_directory and grep tools over raw ls/find.");
        }

        sb.AppendLine("Use run_command only for real executables (dotnet, npm, python, make) — not shell builtins as the command name.");
        return sb.ToString();
    }

    private static string? NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string left, string right)
    {
        var leftNorm = NormalizeDirectoryPath(left) ?? left;
        var rightNorm = NormalizeDirectoryPath(right) ?? right;
        return string.Equals(leftNorm, rightNorm, StringComparison.OrdinalIgnoreCase);
    }
}
