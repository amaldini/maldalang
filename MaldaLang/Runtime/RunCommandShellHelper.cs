// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime;

using System.IO;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

/// <summary>
/// Normalizes and validates shell invocations so agents do not hang on interactive cmd/bash mistakes.
/// </summary>
public static class RunCommandShellHelper
{
    public static int? ResolveTimeoutMs(string command, int? explicitTimeoutMs)
    {
        if (explicitTimeoutMs.HasValue)
            return explicitTimeoutMs;

        var raw = System.Environment.GetEnvironmentVariable("MALDA_RUN_COMMAND_DEFAULT_TIMEOUT_MS");
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw.Trim(), out var fromEnv) && fromEnv > 0)
            return fromEnv;

        var name = Path.GetFileNameWithoutExtension(command).ToLowerInvariant();
        if (CommandApprovalPolicy.ShellWrapperCommands.Contains(name))
            return 120_000;

        return null;
    }

    /// <summary>
    /// Returns an error result object, or null if invocation looks OK.
    /// Mutates <paramref name="commandArgs"/> in place (e.g. cmd -c → /c).
    /// </summary>
    public static RuntimeValue? ValidateAndNormalize(string command, List<string>? commandArgs)
    {
        var name = Path.GetFileNameWithoutExtension(command).ToLowerInvariant();

        if (name is "cmd" or "cmd.exe")
        {
            if (commandArgs == null || commandArgs.Count == 0)
            {
                return Error(
                    "cmd.exe started without /c is interactive and was blocked. " +
                    "Use list_directory or pwd for paths; use run_command with dotnet/npm for builds.");
            }

            NormalizeCmdSwitch(commandArgs);

            var first = commandArgs[0];
            if (!first.Equals("/c", StringComparison.OrdinalIgnoreCase) &&
                !first.Equals("/k", StringComparison.OrdinalIgnoreCase))
            {
                return Error(
                    "cmd.exe must use /c <command> on Windows (not -c). " +
                    "Example: command=cmd, args=[\"/c\", \"echo hello\"]. " +
                    "For directory listing use list_directory; for cwd use pwd.");
            }
        }

        if (name is "powershell" or "pwsh")
        {
            if (commandArgs == null || commandArgs.Count == 0)
            {
                return Error("powershell without -Command/-File is interactive and was blocked.");
            }

            if (commandArgs[0] is "-c" or "-C")
                commandArgs[0] = "-Command";
        }

        return null;
    }

    private static void NormalizeCmdSwitch(List<string> commandArgs)
    {
        if (commandArgs.Count == 0)
            return;

        if (commandArgs[0] is "-c" or "-C")
            commandArgs[0] = "/c";
    }

    private static RuntimeValue Error(string message)
    {
        var obj = new JsonObject();
        obj.Set("exitCode", RuntimeValue.Integer(-1));
        obj.Set("stdout", RuntimeValue.String(""));
        obj.Set("stderr", RuntimeValue.String(message));
        return RuntimeValue.Object(obj);
    }
}
