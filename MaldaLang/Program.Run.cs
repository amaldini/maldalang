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
using MaldaLang.Runtime.UI;
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

sealed class CliProfilingSettings
{
    public bool Enabled { get; set; }
    public string? OutputPath { get; set; }
    public ProfilingFormat Format { get; set; } = ProfilingFormat.Text;
    /// <summary>When &gt; 0, write profile file(s) on this interval (seconds) while the program runs.</summary>
    public double PeriodicSnapshotSeconds { get; set; }
}

partial class Program
{
    static bool TryConsumeProfilingArgument(string[] args, ref int index, CliProfilingSettings settings, bool writeToError = false)
    {
        void WriteMessage(string message)
        {
            if (writeToError)
            {
                Console.Error.WriteLine(message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }

        var arg = args[index].ToLowerInvariant();
        if (arg == "--profile")
        {
            settings.Enabled = true;
            return true;
        }

        if (arg == "--profile-output")
        {
            if (index + 1 >= args.Length)
            {
                WriteMessage("Error: --profile-output requires a path");
                SystemEnvironment.Exit(1);
                return true;
            }

            settings.Enabled = true;
            settings.OutputPath = args[index + 1];
            index++;
            return true;
        }

        if (arg == "--profile-format")
        {
            if (index + 1 >= args.Length)
            {
                WriteMessage("Error: --profile-format requires text, json, or both");
                SystemEnvironment.Exit(1);
                return true;
            }

            var formatValue = args[index + 1].ToLowerInvariant();
            settings.Enabled = true;
            if (formatValue == "text")
            {
                settings.Format = ProfilingFormat.Text;
            }
            else if (formatValue == "json")
            {
                settings.Format = ProfilingFormat.Json;
            }
            else if (formatValue == "both")
            {
                settings.Format = ProfilingFormat.Both;
            }
            else
            {
                WriteMessage($"Error: Invalid profile format '{args[index + 1]}'. Use text, json, or both.");
                SystemEnvironment.Exit(1);
                return true;
            }
            index++;
            return true;
        }

        if (arg == "--profile-periodic-seconds")
        {
            if (index + 1 >= args.Length)
            {
                WriteMessage("Error: --profile-periodic-seconds requires a non-negative number");
                SystemEnvironment.Exit(1);
                return true;
            }

            if (!double.TryParse(args[index + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || seconds < 0.0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                WriteMessage("Error: --profile-periodic-seconds must be a non-negative number");
                SystemEnvironment.Exit(1);
                return true;
            }

            settings.Enabled = true;
            settings.PeriodicSnapshotSeconds = seconds;
            index++;
            return true;
        }

        return false;
    }

    static ProfilingOptions? BuildProfilingOptions(CliProfilingSettings settings, bool writeToConsole = true)
    {
        if (!settings.Enabled)
        {
            return null;
        }

        return new ProfilingOptions
        {
            Enabled = true,
            OutputPath = settings.OutputPath,
            Format = settings.Format,
            WriteToConsole = writeToConsole,
            PeriodicSnapshotSeconds = settings.PeriodicSnapshotSeconds
        };
    }

    static CliRunOptions ParseCliRunOptions(string[] args, int startIndex, bool writeToError = false)
    {
        var settings = new CliProfilingSettings();
        var strictTypes = false;
        for (int i = startIndex; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--strict-types", StringComparison.OrdinalIgnoreCase))
            {
                strictTypes = true;
                continue;
            }

            if (TryConsumeProfilingArgument(args, ref i, settings, writeToError))
            {
                continue;
            }
        }

        return new CliRunOptions
        {
            Profiling = BuildProfilingOptions(settings),
            StrictTypes = strictTypes
        };
    }

    static void RunFile(string path, CliRunOptions? runOptions = null)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Error: File not found: {path}");
            SystemEnvironment.Exit(1);
            return;
        }

        var source = File.ReadAllText(path);
        try
        {
            Run(source, null, path, runOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(RuntimeDiagnostics.FormatForConsole(ex));
            SystemEnvironment.Exit(1);
        }
    }

    static void Run(string source, Interpreter.Interpreter? interpreter = null, string? sourceFileName = null, CliRunOptions? runOptions = null)
    {
        if (UsesUiFramework(source))
        {
            UiHostLink.StartAsync().GetAwaiter().GetResult();
        }

        var lexer = new Lexer(source, sourceFileName);
        var tokens = lexer.Tokenize();
        
        var parser = new Parser.Parser(tokens, sourceFileName);
        var statements = parser.Parse();

        // Declaration() catches ParseException, records it, and returns null — so Parse() can
        // finish with an empty statement list and zero thrown exceptions. Without this check a
        // program like `print("hi")` (missing semicolon) exits 0 with no output.
        if (parser.Errors.Count > 0)
        {
            throw parser.Errors.Count == 1
                ? parser.Errors[0]
                : new Parser.ParseException(string.Join(System.Environment.NewLine, parser.Errors.Select(e => e.Message)));
        }

        if (runOptions?.StrictTypes == true)
        {
            var strictDiagnostics = new List<Diagnostic>();
            StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Enabled, strictDiagnostics, sourceFileName);
            if (StrictTypesAnalysis.HasErrors(strictDiagnostics))
            {
                Console.Error.WriteLine(StrictTypesAnalysis.FormatErrorsForConsole(strictDiagnostics, sourceFileName));
                SystemEnvironment.Exit(1);
                return;
            }
        }
        
        var interp = interpreter ?? new Interpreter.Interpreter(currentFile: sourceFileName);
        interp.SetSourceCode(source);
        // For console environment, we can use GetAwaiter().GetResult() since there's no async context
        MaldaProfiler.StartSession(runOptions?.Profiling, sourceFileName ?? "inline");
        try
        {
            interp.InterpretAsync(statements).GetAwaiter().GetResult();
        }
        finally
        {
            MaldaProfiler.CompleteSession();
        }
    }
}
