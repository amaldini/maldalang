// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime;

using System.IO;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

/// <summary>
/// Unix-style commands that are not Windows executables — handled in-process when possible.
/// </summary>
public static class RunCommandPseudo
{
    public static bool IsAutoApproved(string command, IEnumerable<string>? args)
    {
        var name = Path.GetFileNameWithoutExtension(command).ToLowerInvariant();
        var argList = args?.ToList() ?? new List<string>();
        return (name is "pwd" or "cd") && argList.Count == 0;
    }

    public static RuntimeValue? TryExecute(string command, IEnumerable<string>? args, string workingDirectory)
    {
        var name = Path.GetFileNameWithoutExtension(command).ToLowerInvariant();
        var argList = args?.ToList() ?? new List<string>();

        if (name == "pwd" || name == "cd")
        {
            if (argList.Count > 0)
            {
                return Error("'" + name + "' does not take arguments on this platform. Working directory is already set on the agent.");
            }

            return Success(workingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + System.Environment.NewLine);
        }

        if (name is "ls")
        {
            return Error(
                "'ls' is not a Windows executable. Use list_directory with dirPath \".\" instead.");
        }

        return null;
    }

    private static RuntimeValue Success(string stdout)
    {
        var obj = new JsonObject();
        obj.Set("exitCode", RuntimeValue.Integer(0));
        obj.Set("stdout", RuntimeValue.String(stdout));
        obj.Set("stderr", RuntimeValue.String(""));
        return RuntimeValue.Object(obj);
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
