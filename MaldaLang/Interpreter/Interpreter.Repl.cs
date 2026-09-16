// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System.Collections.Generic;

public partial class Interpreter
{
    /// <summary>
    /// Drops a user-defined REPL binding. Host/stdlib names from
    /// <paramref name="hostNames"/> are refused. Classes and actors are removed
    /// from the session tables so a later declaration is a first definition
    /// (not an augmentation).
    /// </summary>
    internal bool TryDropUserDefinition(string name, ISet<string>? hostNames, out string kind, out string? error)
    {
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
            error = "No user definition named '" + name + "'.";
            return false;
        }

        var value = _globals.Get(name);
        kind = value.Type switch
        {
            ValueType.Function => "function",
            ValueType.Prompt => "prompt",
            ValueType.Class => "class",
            ValueType.Actor => "actor",
            _ => "variable"
        };
        _globals.TryRemoveOwn(name);
        return true;
    }
}
