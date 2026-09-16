// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaldaLang.Interpreter;

/// <summary>
/// REPL <c>drop</c> / <c>replace</c>: remove a previous definition from the
/// session (and, for <c>replace</c>, reprint its source so it can be re-entered).
/// </summary>
public static class ReplSessionEdit
{
    public const string DropAction = "drop";
    public const string ReplaceAction = "replace";

    /// <summary>
    /// True when <paramref name="line"/> is <c>drop</c>/<c>undef</c> or
    /// <c>replace</c>/<c>edit</c>, not an assignment such as <c>drop = 1</c>.
    /// </summary>
    public static bool TryParseCommand(string? line, out string action, out string name)
    {
        action = "";
        name = "";
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !TryVerb(parts[0], out action))
            return false;

        if (parts.Length >= 2 && !LooksLikeNameToken(parts[1]))
            return false;

        if (parts.Length > 1)
            name = string.Join(" ", parts.Skip(1));
        return true;
    }

    public static void Write(
        TextWriter writer,
        string action,
        string name,
        Interpreter interpreter,
        ISet<string>? hostNames,
        ReplSessionSource sessionSource)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            writer.WriteLine(action == ReplaceAction
                ? "Usage: replace <name>   (aliases: edit)"
                : "Usage: drop <name>   (aliases: undef)");
            return;
        }

        if (hostNames != null && hostNames.Contains(name))
        {
            writer.WriteLine("Cannot drop host name '" + name + "'.");
            return;
        }

        var previous = sessionSource.Peek(name);
        var dropped = interpreter.TryDropUserDefinition(name, hostNames, out var kind, out var error);
        if (!dropped && previous == null)
        {
            writer.WriteLine(error ?? "No user definition named '" + name + "'.");
            return;
        }

        sessionSource.TryRemove(name);
        var label = string.IsNullOrEmpty(kind) ? "definition" : kind;
        if (action == ReplaceAction)
        {
            writer.WriteLine("Dropped " + label + " '" + name + "'. Previous source:");
            writer.WriteLine();
            writer.WriteLine(previous ?? "(no recorded source for '" + name + "')");
            writer.WriteLine();
            writer.WriteLine("Enter a new definition to replace it.");
            return;
        }

        writer.WriteLine("Dropped " + label + " '" + name + "'.");
    }

    private static bool TryVerb(string word, out string action)
    {
        if (word.Equals("drop", StringComparison.OrdinalIgnoreCase)
            || word.Equals("undef", StringComparison.OrdinalIgnoreCase))
        {
            action = DropAction;
            return true;
        }

        if (word.Equals("replace", StringComparison.OrdinalIgnoreCase)
            || word.Equals("edit", StringComparison.OrdinalIgnoreCase))
        {
            action = ReplaceAction;
            return true;
        }

        action = "";
        return false;
    }

    private static bool LooksLikeNameToken(string token) =>
        token.Length > 0 && (char.IsLetter(token[0]) || token[0] == '_');
}
