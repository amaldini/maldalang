// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Interpreter;
using MaldaLang.PackageManager;
using System;
using System.Text;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using MaldaLang.Testing;
using MaldaLang.Scaffolding;
using MaldaLang.Deployment;
using MaldaLang.UIHost;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Cli;
using MaldaLang.Eval;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.BuiltIns;
using MaldaLang.DebugAdapter;
using SystemEnvironment = System.Environment;
using PackageManager = MaldaLang.PackageManager.PackageManager;

namespace MaldaLang;

partial class Program
{
    internal readonly struct AgentLaunchOptions
    {
        public bool ShowHelp { get; init; }
        public string? Error { get; init; }
        public string? Message { get; init; }
        public string? Channel { get; init; }
        public string? Backend { get; init; }
        public string? Workdir { get; init; }
        /// <summary>null is the personal assistant. "dev" is DevAgent. "malda" is MALDACodingAgent.</summary>
        public string? DevelopKind { get; init; }
    }

    internal static AgentLaunchOptions ParseAgentLaunch(string[] args)
    {
        string? message = null;
        string? channel = null;
        string? backend = null;
        string? workdir = null;
        string? positionalKind = null;
        string? flagKind = null;
        var develop = false;
        string? error = null;

        for (int i = 1; i < args.Length; i++)
        {
            var token = args[i];
            if (token == "--help" || token == "-h")
                return new AgentLaunchOptions { ShowHelp = true };

            if (token == "-m" || token == "--message")
            {
                if (!TryTakeAgentFlagValue(args, ref i, token, out message, ref error))
                    break;
                continue;
            }
            if (token == "-c" || token == "--channel")
            {
                if (!TryTakeAgentFlagValue(args, ref i, token, out channel, ref error))
                    break;
                continue;
            }
            if (token == "-b" || token == "--backend")
            {
                if (!TryTakeAgentFlagValue(args, ref i, token, out backend, ref error))
                    break;
                continue;
            }
            if (token == "--workdir")
            {
                if (!TryTakeAgentFlagValue(args, ref i, token, out workdir, ref error))
                    break;
                continue;
            }
            if (token == "--kind")
            {
                if (!TryTakeAgentFlagValue(args, ref i, token, out var rawKind, ref error))
                    break;
                if (!TryNormalizeDevelopKind(rawKind, out flagKind))
                {
                    error = "Unknown develop kind '" + rawKind + "'. Use generic or malda.";
                    break;
                }
                continue;
            }
            if (token == "develop")
            {
                develop = true;
                if (i + 1 < args.Length && TryNormalizeDevelopKind(args[i + 1], out var nextKind))
                {
                    positionalKind = nextKind;
                    i++;
                }
                continue;
            }
            if (develop)
            {
                error = "Unknown agent develop argument: " + token;
                break;
            }
        }

        if (error == null && positionalKind != null && flagKind != null &&
            !string.Equals(positionalKind, flagKind, StringComparison.Ordinal))
        {
            error = "Conflicting develop kind. Pass either 'malda' or --kind, not both.";
        }

        var kind = flagKind ?? positionalKind;
        if (error == null && !string.IsNullOrEmpty(kind))
            develop = true;
        if (error == null && !develop && !string.IsNullOrEmpty(workdir))
            error = "--workdir applies to 'malda agent develop'.";

        return new AgentLaunchOptions
        {
            Error = error,
            Message = message,
            Channel = channel,
            Backend = backend,
            Workdir = workdir,
            DevelopKind = develop && error == null ? (kind ?? "dev") : null
        };
    }

    static bool TryTakeAgentFlagValue(string[] args, ref int index, string flag, out string? value, ref string? error)
    {
        if (index + 1 >= args.Length)
        {
            value = null;
            error = flag + " requires a value.";
            return false;
        }
        index++;
        value = args[index];
        return true;
    }

    static bool TryNormalizeDevelopKind(string? token, out string? kind)
    {
        if (string.Equals(token, "malda", StringComparison.OrdinalIgnoreCase))
        {
            kind = "malda";
            return true;
        }
        if (string.Equals(token, "generic", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token, "dev", StringComparison.OrdinalIgnoreCase))
        {
            kind = "dev";
            return true;
        }
        kind = null;
        return false;
    }
}
