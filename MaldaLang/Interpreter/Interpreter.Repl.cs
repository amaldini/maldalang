// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System;
using System.Collections.Generic;

public partial class Interpreter
{
    internal const string AnonymousLambdaName = "<lambda>";

    /// <summary>
    /// Drops a user-defined REPL binding. Host/stdlib names from
    /// <paramref name="hostNames"/> are refused. Classes and actors are removed
    /// from the session tables so a later declaration is a first definition
    /// (not an augmentation). <paramref name="droppedName"/> is the binding
    /// that was removed (lambdas listed as <c>&lt;lambda&gt;</c> resolve to
    /// their variable name).
    /// </summary>
    internal bool TryDropUserDefinition(
        string name,
        ISet<string>? hostNames,
        out string droppedName,
        out string kind,
        out string? error)
    {
        droppedName = name;
        kind = "";
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Usage: drop <name>";
            return false;
        }

        if (hostNames != null && hostNames.Contains(name))
        {
            error = "Cannot drop host name '" + name + "'.";
            return false;
        }

        if (_classes.Remove(name))
        {
            _globals.TryRemoveOwn(name);
            kind = "class";
            return true;
        }

        if (_actors.Remove(name))
        {
            kind = "actor";
            return true;
        }

        if (_workflows.Remove(name))
        {
            kind = "workflow";
            return true;
        }

        if (_properties.Remove(name))
        {
            kind = "property";
            return true;
        }

        if (!_globals.GetOwnVariables().ContainsKey(name))
        {
            if (!TryResolveDropAlias(name, hostNames, out var resolved, out error))
                return false;
            name = resolved;
            droppedName = resolved;
        }

        var value = _globals.Get(name);
        kind = DescribeDropKind(value);
        _globals.TryRemoveOwn(name);
        droppedName = name;
        return true;
    }

    private bool TryResolveDropAlias(
        string name,
        ISet<string>? hostNames,
        out string resolved,
        out string? error)
    {
        resolved = "";
        var matches = new List<string>();
        foreach (var kvp in _globals.GetOwnVariables())
        {
            if (hostNames != null && hostNames.Contains(kvp.Key))
                continue;
            if (kvp.Value.Type != ValueType.Function)
                continue;
            var declName = kvp.Value.AsFunction().Declaration?.Name;
            if (string.Equals(declName, name, StringComparison.Ordinal))
                matches.Add(kvp.Key);
        }

        matches.Sort(StringComparer.Ordinal);
        if (matches.Count == 1)
        {
            resolved = matches[0];
            error = null;
            return true;
        }

        if (matches.Count > 1)
        {
            error = "Ambiguous name '" + name + "'. Use one of: " + string.Join(", ", matches) + ".";
            return false;
        }

        error = "No user definition named '" + name + "'.";
        return false;
    }

    private static string DescribeDropKind(RuntimeValue value) => value.Type switch
    {
        ValueType.Function => IsAnonymousLambda(value) ? "lambda" : "function",
        ValueType.Prompt => "prompt",
        ValueType.Class => "class",
        ValueType.Actor => "actor",
        _ => "variable"
    };

    private static bool IsAnonymousLambda(RuntimeValue value) =>
        value.Type == ValueType.Function
        && value.AsFunction().Declaration?.Name == AnonymousLambdaName;
}
