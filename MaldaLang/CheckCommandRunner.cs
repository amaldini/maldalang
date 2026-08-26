// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;

/// <summary>
/// <c>malda check</c> — parse + IDE diagnostics without executing.
/// Machine-readable <c>--json</c> is the generate → diagnose → patch loop for agents.
/// </summary>
internal sealed class CheckCommandRunner
{
    public const int ExitOk = 0;
    public const int ExitHasErrors = 1;
    public const int ExitUsage = 2;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILanguageService _languageService;

    public CheckCommandRunner(ILanguageService? languageService = null)
    {
        _languageService = languageService ?? new LanguageService();
    }

    public int Run(string[] args, TextWriter output, TextWriter error, TextReader? stdin = null)
    {
        if (args.Any(IsHelpFlag))
        {
            PrintUsage(output);
            return ExitOk;
        }

        if (!TryParseOptions(args, error, out var options))
            return ExitUsage;

        if (options.Json && options.HasUsageError)
        {
            WriteJson(output, CheckCommandReport.Usage(options.UsageError!));
            return ExitUsage;
        }

        if (options.HasUsageError)
            return ExitUsage;

        string source;
        string? fileLabel;
        try
        {
            source = ReadSource(options, stdin ?? Console.In, out fileLabel);
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (options.Json)
            {
                WriteJson(output, CheckCommandReport.Usage(message));
            }
            else
            {
                error.WriteLine(message);
            }

            return ExitUsage;
        }

        var report = Analyze(source, fileLabel, options.TypeOptions);
        if (options.Json)
        {
            WriteJson(output, report);
        }
        else
        {
            WriteHuman(output, report);
        }

        return report.Ok ? ExitOk : ExitHasErrors;
    }

    internal CheckCommandReport Analyze(string source, string? fileLabel, StrictTypesOptions typeOptions)
    {
        List<Diagnostic> diagnostics;
        try
        {
            diagnostics = _languageService.GetDiagnostics(source, fileLabel, strictTypesOptions: typeOptions);
        }
        catch (Exception ex)
        {
            diagnostics = new List<Diagnostic>
            {
                new()
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = ex.Message,
                    Line = 0,
                    Column = 0,
                    Length = 1,
                    Source = "check"
                }
            };
        }

        var items = diagnostics
            .Select(d => CheckDiagnosticDto.From(d, fileLabel))
            .ToList();
        var errors = items.Count(d => d.Severity == "error");
        var warnings = items.Count(d => d.Severity == "warning");
        var infos = items.Count(d => d.Severity == "info");
        return new CheckCommandReport
        {
            Ok = errors == 0,
            Executed = false,
            File = fileLabel,
            ErrorCount = errors,
            WarningCount = warnings,
            InfoCount = infos,
            Diagnostics = items
        };
    }

    public static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Usage: malda check <file.malda> [--json] [--strict-types] [--lenient-types]");
        output.WriteLine("       malda check -e \"<code>\" [--json] [--strict-types] [--lenient-types]");
        output.WriteLine("       malda check --stdin [--json] [--strict-types] [--lenient-types]");
        output.WriteLine();
        output.WriteLine("  Parse and diagnose a MALDA program without executing it.");
        output.WriteLine("  Uses the same LanguageService diagnostics as the IDE/LSP (parser, types,");
        output.WriteLine("  schema/sum-type names, interpolation, UI loop, workflow determinism).");
        output.WriteLine();
        output.WriteLine("  --json              Machine-readable report on stdout (ok, counts, diagnostics).");
        output.WriteLine("  --strict-types      Full CLI suite (match / @pure / @within / @budget / const).");
        output.WriteLine("  --lenient-types     Type mismatches as warnings (IDE default is errors).");
        output.WriteLine("  -e, --eval <code>   Check a snippet instead of a file.");
        output.WriteLine("  --stdin             Read source from stdin (does not execute).");
        output.WriteLine();
        output.WriteLine("  Exit 0 if there are no errors (warnings/info still print). Exit 1 if any error.");
        output.WriteLine("  Exit 2 on usage or I/O errors.");
        output.WriteLine();
        output.WriteLine("  Line and column in --json are 1-based. executed is always false.");
        output.WriteLine("  malda --check \"<code>\" remains a syntax-only compiler Validate.");
    }

    private static bool TryParseOptions(string[] args, TextWriter error, out CheckCommandOptions options)
    {
        options = new CheckCommandOptions();
        string? evalCode = null;
        var stdin = false;
        var json = false;
        var strict = false;
        var lenient = false;
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--json")
            {
                json = true;
            }
            else if (arg == "--strict-types")
            {
                strict = true;
            }
            else if (arg == "--lenient-types")
            {
                lenient = true;
            }
            else if (arg == "--stdin" || arg == "-")
            {
                stdin = true;
            }
            else if (arg == "-e" || arg == "--eval")
            {
                if (i + 1 >= args.Length)
                {
                    options = FailedOptions(json, "Missing code after -e / --eval.");
                    if (!json)
                        error.WriteLine(options.UsageError);
                    return true;
                }

                evalCode = args[++i];
            }
            else if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                options = FailedOptions(json, $"Unknown option: {arg}");
                if (!json)
                    error.WriteLine(options.UsageError);
                return true;
            }
            else
            {
                positionals.Add(arg);
            }
        }

        if (strict && lenient)
        {
            options = FailedOptions(json, "Cannot combine --strict-types and --lenient-types.");
            if (!json)
                error.WriteLine(options.UsageError);
            return true;
        }

        var sources = 0;
        if (evalCode != null) sources++;
        if (stdin) sources++;
        if (positionals.Count > 0) sources++;

        if (sources == 0)
        {
            options = FailedOptions(json, "Specify a .malda file, -e \"<code>\", or --stdin.");
            if (!json)
            {
                error.WriteLine(options.UsageError);
                PrintUsage(error);
            }

            return true;
        }

        if (sources > 1)
        {
            options = FailedOptions(json, "Specify only one of: file, -e, or --stdin.");
            if (!json)
                error.WriteLine(options.UsageError);
            return true;
        }

        if (positionals.Count > 1)
        {
            options = FailedOptions(json, "malda check accepts one file.");
            if (!json)
                error.WriteLine(options.UsageError);
            return true;
        }

        options = new CheckCommandOptions
        {
            Json = json,
            TypeOptions = strict
                ? StrictTypesOptions.Enabled
                : lenient
                    ? StrictTypesOptions.Lenient
                    : StrictTypesOptions.Default,
            EvalCode = evalCode,
            ReadStdin = stdin,
            FilePath = positionals.Count == 1 ? positionals[0] : null
        };
        return true;
    }

    private static CheckCommandOptions FailedOptions(bool json, string message)
    {
        return new CheckCommandOptions
        {
            Json = json,
            UsageError = message
        };
    }

    private static string ReadSource(CheckCommandOptions options, TextReader stdin, out string? fileLabel)
    {
        if (options.EvalCode != null)
        {
            fileLabel = "<eval>";
            return options.EvalCode;
        }

        if (options.ReadStdin)
        {
            fileLabel = "<stdin>";
            return stdin.ReadToEnd();
        }

        var path = options.FilePath!;
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}", path);

        fileLabel = path;
        return File.ReadAllText(path);
    }

    private static void WriteJson(TextWriter output, CheckCommandReport report)
    {
        output.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
    }

    private static void WriteHuman(TextWriter output, CheckCommandReport report)
    {
        if (report.Diagnostics.Count == 0)
        {
            var label = report.File ?? "<source>";
            output.WriteLine($"{label}: ok (0 diagnostics)");
            return;
        }

        foreach (var d in report.Diagnostics)
        {
            var file = d.File ?? report.File ?? "<source>";
            var code = string.IsNullOrEmpty(d.Code) ? "" : $"[{d.Code}]";
            output.WriteLine($"{file}:{d.Line}:{d.Column}: {d.Severity}{code}: {d.Message}");
            if (!string.IsNullOrEmpty(d.Hint))
                output.WriteLine($"  hint: {d.Hint}");
            if (!string.IsNullOrEmpty(d.SuggestedFix))
                output.WriteLine($"  fix: {d.SuggestedFix}");
        }

        output.WriteLine(
            $"{report.ErrorCount} error(s), {report.WarningCount} warning(s), {report.InfoCount} info");
    }

    private static bool IsHelpFlag(string value)
    {
        return string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CheckCommandOptions
    {
        public bool Json { get; init; }
        public StrictTypesOptions TypeOptions { get; init; } = StrictTypesOptions.Default;
        public string? EvalCode { get; init; }
        public bool ReadStdin { get; init; }
        public string? FilePath { get; init; }
        public string? UsageError { get; init; }
        public bool HasUsageError => !string.IsNullOrEmpty(UsageError);
    }
}

internal sealed class CheckCommandReport
{
    public bool Ok { get; init; }
    public bool Executed { get; init; }
    public string? File { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int InfoCount { get; init; }
    public string? Error { get; init; }
    public List<CheckDiagnosticDto> Diagnostics { get; init; } = new();

    public static CheckCommandReport Usage(string message)
    {
        return new CheckCommandReport
        {
            Ok = false,
            Executed = false,
            Error = message,
            ErrorCount = 0,
            WarningCount = 0,
            InfoCount = 0,
            Diagnostics = new List<CheckDiagnosticDto>()
        };
    }
}

internal sealed class CheckDiagnosticDto
{
    public string Severity { get; init; } = "error";
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public string? File { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public int Length { get; init; }
    public string? Hint { get; init; }
    public string? SuggestedFix { get; init; }
    public CheckFixDto? Fix { get; init; }

    public static CheckDiagnosticDto From(Diagnostic diagnostic, string? fileLabel)
    {
        CheckFixDto? fix = null;
        if (diagnostic.AutoFix != null)
        {
            fix = new CheckFixDto
            {
                Description = diagnostic.AutoFix.Description,
                Line = diagnostic.AutoFix.Line + 1,
                Column = diagnostic.AutoFix.Column + 1,
                Insert = diagnostic.AutoFix.TextToInsert,
                ReplaceLength = diagnostic.AutoFix.LengthToReplace
            };
        }

        return new CheckDiagnosticDto
        {
            Severity = diagnostic.Severity switch
            {
                DiagnosticSeverity.Warning => "warning",
                DiagnosticSeverity.Info => "info",
                _ => "error"
            },
            Code = diagnostic.Source ?? "",
            Message = diagnostic.Message,
            File = fileLabel,
            Line = diagnostic.Line + 1,
            Column = diagnostic.Column + 1,
            Length = Math.Max(diagnostic.Length, 1),
            Hint = string.IsNullOrEmpty(diagnostic.LearningHint) ? null : diagnostic.LearningHint,
            SuggestedFix = string.IsNullOrEmpty(diagnostic.SuggestedFix) ? null : diagnostic.SuggestedFix,
            Fix = fix
        };
    }
}

internal sealed class CheckFixDto
{
    public string Description { get; init; } = "";
    public int Line { get; init; }
    public int Column { get; init; }
    public string Insert { get; init; } = "";
    public int ReplaceLength { get; init; }
}
