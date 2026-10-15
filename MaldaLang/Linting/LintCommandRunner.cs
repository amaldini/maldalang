// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Linting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MaldaLang.IDE;

/// <summary>
/// <c>malda lint</c>: report <see cref="LintRules"/> findings for files, directories or stdin.
/// Human output and <c>--json</c> follow <c>malda check</c> (same diagnostic fields, 1-based
/// positions, <c>code</c> carries the <c>ML0xxx</c> rule).
/// </summary>
internal sealed class LintCommandRunner
{
    public const int ExitOk = 0;
    /// <summary>At least one finding is a warning or an error (info never fails), or a file did not parse.</summary>
    public const int ExitFindings = 1;
    public const int ExitUsage = 2;

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", "artifacts"
    };

    public static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  malda lint <file.malda|dir>... [--json]");
        output.WriteLine("  malda lint --stdin [--json]");
        output.WriteLine("  malda lint --list-rules [--json]");
        output.WriteLine();
        output.WriteLine("Reports gotchas from docs/llm/malda-gotchas.md that run without error but do the wrong");
        output.WriteLine("thing, as stable ML0xxx findings. It does not execute the program and does not repeat");
        output.WriteLine("what malda check reports (parser, types, malda-interp, WF1001/WF1002, UI100x): run both.");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --json        Machine-readable report on stdout (ok, counts, diagnostics; like check --json)");
        output.WriteLine("  --stdin       Lint source read from stdin");
        output.WriteLine("  --list-rules  Print every rule: code, severity, title, and the gotcha it comes from");
        output.WriteLine();
        output.WriteLine("Suppress a finding with a comment:");
        output.WriteLine("  // malda-lint-disable-next-line ML0001        (the next line)");
        output.WriteLine("  code();  // malda-lint-disable-line ML0001   (this line)");
        output.WriteLine("  // malda-lint-disable-file ML0003            (the whole file)");
        output.WriteLine("  Several codes are separated by commas or spaces; no code silences every rule.");
        output.WriteLine();
        output.WriteLine("Directories are searched recursively for *.malda (bin/, obj/, node_modules/ and .git/ are skipped).");
        output.WriteLine("Line and column are 1-based.");
        output.WriteLine("Exit codes: 0 no warning or error (info findings do not fail), 1 at least one warning or error");
        output.WriteLine("(ML0000 = a file did not parse), 2 usage or I/O error.");
    }

    public int Run(string[] args, TextWriter output, TextWriter error, TextReader? stdin = null)
    {
        // Known before the loop so a usage error in front of --json is still reported as JSON.
        var json = args.Contains("--json");
        var useStdin = false;
        var listRules = false;
        var paths = new List<string>();

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-h":
                case "--help":
                case "help":
                    PrintUsage(output);
                    return ExitOk;
                case "--json":
                    json = true;
                    break;
                case "--stdin":
                case "-":
                    useStdin = true;
                    break;
                case "--list-rules":
                    listRules = true;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                        return UsageError(json, output, error, $"Unknown option: {arg}");
                    paths.Add(arg);
                    break;
            }
        }

        if (listRules)
        {
            WriteRules(output, json);
            return ExitOk;
        }

        var sources = new List<(string Label, string Source)>();
        if (useStdin)
        {
            if (paths.Count > 0)
                return UsageError(json, output, error, "--stdin does not take file arguments.");
            sources.Add(("<stdin>", (stdin ?? Console.In).ReadToEnd()));
        }
        else
        {
            if (paths.Count == 0)
                return UsageError(json, output, error, "Specify a .malda file, a directory, or --stdin.", printUsage: true);

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    sources.Add((path, File.ReadAllText(path)));
                }
                else if (Directory.Exists(path))
                {
                    foreach (var file in EnumerateMaldaFiles(path))
                        sources.Add((file, File.ReadAllText(file)));
                }
                else
                {
                    return UsageError(json, output, error, $"Path not found: {path}");
                }
            }
        }

        var report = Analyze(sources);
        if (json)
            output.WriteLine(JsonSerializer.Serialize(report, CheckCommandRunner.JsonOptions));
        else
            WriteHuman(output, report);

        return report.Ok ? ExitOk : ExitFindings;
    }

    internal static LintCommandReport Analyze(IReadOnlyList<(string Label, string Source)> sources)
    {
        var diagnostics = new List<CheckDiagnosticDto>();
        var suppressed = 0;
        // A file that is both linted and included by another linted file reports its findings once.
        var seen = new HashSet<(string File, string Code, int Line, int Column)>();
        foreach (var (label, source) in sources)
        {
            var result = MaldaLinter.Lint(source, label.StartsWith("<", StringComparison.Ordinal) ? null : label);
            suppressed += result.SuppressedCount;
            foreach (var finding in result.Findings)
            {
                var file = finding.File ?? label;
                if (!seen.Add((FileKey(file), finding.Code, finding.Line, finding.Column)))
                    continue;
                diagnostics.Add(ToDto(finding, label));
            }
        }

        var errors = diagnostics.Count(d => d.Severity == "error");
        var warnings = diagnostics.Count(d => d.Severity == "warning");
        return new LintCommandReport
        {
            Ok = errors == 0 && warnings == 0,
            File = sources.Count == 1 ? sources[0].Label : null,
            FileCount = sources.Count,
            ErrorCount = errors,
            WarningCount = warnings,
            InfoCount = diagnostics.Count(d => d.Severity == "info"),
            SuppressedCount = suppressed,
            Diagnostics = diagnostics
        };
    }

    private static string FileKey(string file)
    {
        if (file.StartsWith("<", StringComparison.Ordinal))
            return file;
        try
        {
            return Path.GetFullPath(file).ToUpperInvariant();
        }
        catch
        {
            return file;
        }
    }

    /// <summary><paramref name="file"/> is the linted source's label; a finding in an included file names that file.</summary>
    internal static CheckDiagnosticDto ToDto(LintFinding finding, string? file) => new()
    {
        Severity = finding.Severity switch
        {
            LintSeverity.Warning => "warning",
            LintSeverity.Info => "info",
            _ => "error"
        },
        Code = finding.Code,
        Message = finding.Message,
        File = finding.File != null ? DiagnosticSourceFiles.ToDisplayPath(finding.File) : file,
        Line = finding.Line,
        Column = finding.Column,
        Length = Math.Max(1, finding.Length),
        Hint = $"Gotcha: {LintRules.Get(finding.Code).Gotcha} (docs/llm/malda-gotchas.md)",
        SuggestedFix = finding.Hint
    };

    private static void WriteHuman(TextWriter output, LintCommandReport report)
    {
        foreach (var d in report.Diagnostics)
        {
            output.WriteLine($"{d.File ?? "<source>"}:{d.Line}:{d.Column}: {d.Severity}[{d.Code}]: {d.Message}");
            if (!string.IsNullOrEmpty(d.SuggestedFix))
                output.WriteLine($"  fix: {d.SuggestedFix}");
        }

        if (report.Diagnostics.Count == 0 && report.FileCount == 1)
        {
            output.WriteLine($"{report.File}: ok (0 findings)");
            return;
        }

        var suppressed = report.SuppressedCount > 0 ? $", {report.SuppressedCount} suppressed" : "";
        output.WriteLine(
            $"{report.FileCount} file(s): {report.ErrorCount} error(s), {report.WarningCount} warning(s), {report.InfoCount} info{suppressed}");
    }

    private static void WriteRules(TextWriter output, bool json)
    {
        if (json)
        {
            var rules = LintRules.All.Select(r => new
            {
                code = r.Code,
                severity = r.Severity.ToString().ToLowerInvariant(),
                title = r.Title,
                gotcha = r.Gotcha
            });
            output.WriteLine(JsonSerializer.Serialize(rules, CheckCommandRunner.JsonOptions));
            return;
        }

        foreach (var rule in LintRules.All)
        {
            output.WriteLine($"{rule.Code}  {rule.Severity.ToString().ToLowerInvariant(),-7}  {rule.Title}");
            output.WriteLine($"        gotcha: {rule.Gotcha}");
        }
    }

    private static int UsageError(bool json, TextWriter output, TextWriter error, string message, bool printUsage = false)
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new LintCommandReport { Ok = false, Error = message }, CheckCommandRunner.JsonOptions));
            return ExitUsage;
        }

        error.WriteLine($"malda lint: {message}");
        if (printUsage)
            PrintUsage(error);
        return ExitUsage;
    }

    private static IEnumerable<string> EnumerateMaldaFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var found = new List<string>();
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            found.AddRange(Directory.EnumerateFiles(dir, "*.malda"));
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(sub)))
                    pending.Push(sub);
            }
        }

        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }
}

/// <summary>
/// <c>malda lint --json</c> report: the <c>malda check --json</c> shape (ok, executed, file, counts,
/// diagnostics) plus <c>fileCount</c> and <c>suppressedCount</c>. <c>file</c> is set when one source was linted.
/// </summary>
internal sealed class LintCommandReport
{
    public bool Ok { get; init; }
    public bool Executed { get; init; }
    public string? File { get; init; }
    public int FileCount { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int InfoCount { get; init; }
    public int SuppressedCount { get; init; }
    public string? Error { get; init; }
    public List<CheckDiagnosticDto> Diagnostics { get; init; } = new();
}
