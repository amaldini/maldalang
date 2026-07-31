// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime;

using System.IO;
using MaldaLang.Interpreter;

public enum CommandApprovalMode
{
    Ask,
    Whitelist,
    Allow,
    Deny
}

public enum CommandRisk
{
    Safe,
    NeedsApproval,
    DeniedAlways
}

/// <summary>
/// Policy for run_command approval. Configure via MALDA_RUN_COMMAND_POLICY and MALDA_RUN_COMMAND_WHITELIST.
/// </summary>
public sealed class CommandApprovalPolicy
{
    public CommandApprovalMode Mode { get; init; } = CommandApprovalMode.Ask;

    public HashSet<string> Whitelist { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static CommandApprovalPolicy FromEnvironment()
    {
        var rawMode = System.Environment.GetEnvironmentVariable("MALDA_RUN_COMMAND_POLICY");
        var mode = ParseMode(rawMode);

        var whitelist = new HashSet<string>(DefaultWhitelist, StringComparer.OrdinalIgnoreCase);
        var rawList = System.Environment.GetEnvironmentVariable("MALDA_RUN_COMMAND_WHITELIST");
        if (!string.IsNullOrWhiteSpace(rawList))
        {
            foreach (var part in rawList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Length > 0)
                    whitelist.Add(part);
            }
        }

        return new CommandApprovalPolicy { Mode = mode, Whitelist = whitelist };
    }

    private static CommandApprovalMode ParseMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return CommandApprovalMode.Ask;

        return raw.Trim().ToLowerInvariant() switch
        {
            "whitelist" => CommandApprovalMode.Whitelist,
            "allow" => CommandApprovalMode.Allow,
            "deny" => CommandApprovalMode.Deny,
            _ => CommandApprovalMode.Ask
        };
    }

    public static readonly HashSet<string> DefaultWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet", "npm", "npx", "node", "python", "python3", "pip", "pip3",
        "git", "cargo", "rustc", "go", "make", "cmake", "msbuild", "echo"
    };

    public static readonly HashSet<string> DeniedAlwaysCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm", "rmdir", "del", "delete", "erase", "format", "fdisk", "diskpart",
        "shutdown", "restart", "reboot", "logoff", "taskkill", "kill", "killall",
        "chmod", "chown", "sudo", "su", "runas", "net",
        "reg", "regedit", "regsvr32", "sc", "wmic"
    };

    public static readonly HashSet<string> ShellWrapperCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell", "cmd", "bash", "sh", "zsh", "csh", "tcsh", "ksh", "fish", "pwsh"
    };
}

/// <summary>
/// Internal scope flag set by Conversation after user approval — not exposed to LLM tool args.
/// </summary>
public static class CommandExecutionContext
{
    private static readonly AsyncLocal<bool> UserApproved = new();

    public static bool IsUserApproved => UserApproved.Value;

    public static IDisposable EnterUserApprovedScope()
    {
        UserApproved.Value = true;
        return new UserApprovedScope();
    }

    private sealed class UserApprovedScope : IDisposable
    {
        public void Dispose() => UserApproved.Value = false;
    }
}

public static class CommandApprovalService
{
    public static CommandRisk Classify(string command)
    {
        var commandName = Path.GetFileNameWithoutExtension(command).ToLowerInvariant();
        if (CommandApprovalPolicy.DeniedAlwaysCommands.Contains(commandName))
            return CommandRisk.DeniedAlways;
        if (CommandApprovalPolicy.ShellWrapperCommands.Contains(commandName))
            return CommandRisk.NeedsApproval;
        return CommandRisk.Safe;
    }

    public static bool IsWhitelisted(string command, CommandApprovalPolicy policy)
    {
        var commandName = Path.GetFileNameWithoutExtension(command).ToLowerInvariant();
        return policy.Whitelist.Contains(commandName);
    }

    public static string FormatCommandDisplay(string command, IEnumerable<string>? args, string? workingDirectory)
    {
        var parts = new List<string> { command };
        if (args != null)
            parts.AddRange(args);
        var line = string.Join(" ", parts);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            line += $"  (cwd: {workingDirectory})";
        return line;
    }

    public static async Task<(bool Approved, string? ErrorMessage)> EnsureApprovedAsync(
        IInputProvider? inputProvider,
        string command,
        IEnumerable<string>? args,
        string? workingDirectory,
        CommandApprovalPolicy? policy = null)
    {
        policy ??= CommandApprovalPolicy.FromEnvironment();
        var risk = Classify(command);

        if (risk == CommandRisk.DeniedAlways)
        {
            var name = Path.GetFileNameWithoutExtension(command);
            return (false, $"Error: Command '{name}' is not allowed for security reasons.");
        }

        if (policy.Mode == CommandApprovalMode.Allow)
            return (true, null);

        if (RunCommandPseudo.IsAutoApproved(command, args))
            return (true, null);

        if (IsWhitelisted(command, policy))
            return (true, null);

        if (policy.Mode == CommandApprovalMode.Deny)
        {
            return (false, $"Error: Command '{command}' is not in the run_command whitelist (policy=deny).");
        }

        var needsPrompt = policy.Mode == CommandApprovalMode.Ask ||
                          risk == CommandRisk.NeedsApproval ||
                          policy.Mode == CommandApprovalMode.Whitelist;

        if (!needsPrompt)
            return (true, null);

        if (inputProvider == null && !IsInteractiveConsole())
        {
            if (risk == CommandRisk.NeedsApproval)
                return (false, BuildShellAutonomousDenialMessage(command, args));

            if (policy.Mode == CommandApprovalMode.Whitelist)
            {
                return (false,
                    $"Error: Command '{command}' is not in the run_command whitelist (policy=whitelist). " +
                    "Use list_directory, grep, or read_file for file operations; run_command only for dotnet/npm/node/python.");
            }
        }

        var display = FormatCommandDisplay(command, args, workingDirectory);
        var prompt = $"Allow running this command?\n{display}";

        var approved = inputProvider != null
            ? await inputProvider.ConfirmAsync(prompt, defaultValue: false).ConfigureAwait(false)
            : ConsoleConfirm(prompt, defaultValue: false);

        if (!approved)
            return (false, BuildDeniedMessage(command, args, display));

        Console.WriteLine("Approved — running command...");
        Console.Out.Flush();
        return (true, null);
    }

    private static bool IsInteractiveConsole()
    {
        var nonInteractive = System.Environment.GetEnvironmentVariable("MALDA_NON_INTERACTIVE");
        if (nonInteractive is "1" or "true" or "yes")
            return false;
        try
        {
            return !Console.IsInputRedirected;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildShellAutonomousDenialMessage(string command, IEnumerable<string>? args)
    {
        var argText = args != null ? string.Join(" ", args).ToLowerInvariant() : "";
        var hints = new List<string> { "list_directory with dirPath \".\" to list files" };
        if (argText.Contains("select-string") || argText.Contains("findstr") || argText.Contains("grep") ||
            argText.Contains("match") || argText.Contains("search"))
            hints.Add("grep to search inside files");
        hints.Add("read_file to read a file");
        hints.Add("pwd or list_directory for paths — not cmd -c echo %CD%");
        hints.Add("run_command only with whitelisted programs (dotnet, npm, node, python) — shell uses /c on Windows, not -c");
        return $"Error: Do not use run_command with '{command}'. Use: {string.Join("; ", hints)}.";
    }

    private static string BuildDeniedMessage(string command, IEnumerable<string>? args, string display)
    {
        if (CommandApprovalPolicy.ShellWrapperCommands.Contains(Path.GetFileNameWithoutExtension(command)))
            return BuildShellAutonomousDenialMessage(command, args);
        return $"Error: User denied command: {display}";
    }

    private static bool ConsoleConfirm(string message, bool defaultValue)
    {
        Console.WriteLine(message);
        Console.Write(defaultValue ? "[Y/n] " : "[y/N] ");
        Console.Out.Flush();
        var line = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(line))
            return defaultValue;
        return line is "y" or "yes" or "true" or "1";
    }
}
