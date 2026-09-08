// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Eval;

using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Runtime.Journal;
using MaldaLang.Runtime.LlmCassettes;
using ValueType = MaldaLang.Interpreter.ValueType;

public sealed class EvalCommandRunner
{
    public const int ExitOk = 0;
    public const int ExitFail = 1;
    public const int ExitUsage = 2;

    public int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        {
            output.WriteLine("malda eval <path.malda> [--baseline .malda/evals.json] [--update-baseline]");
            return args.Length == 0 ? ExitUsage : ExitOk;
        }

        string? path = null;
        var baseline = Path.Combine(".malda", "evals.json");
        var update = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--baseline" && i + 1 < args.Length)
                baseline = args[++i];
            else if (args[i] == "--update-baseline")
                update = true;
            else if (!args[i].StartsWith('-'))
                path = args[i];
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            error.WriteLine("eval: missing .malda file");
            return ExitUsage;
        }

        var source = File.ReadAllText(path);
        var parser = new Parser(new Lexer(source, path).Tokenize(), path);
        var statements = parser.Parse();
        var suites = statements.OfType<SuiteDeclaration>().ToList();
        if (suites.Count == 0)
        {
            error.WriteLine("eval: no suite declarations found");
            return ExitFail;
        }

        var report = new EvalReport();
        var failed = false;
        foreach (var suite in suites)
        {
            foreach (var evalCase in suite.Cases)
            {
                var samples = CassetteTransport.IsReplaying ? evalCase.Samples : evalCase.Samples;
                if (evalCase.Samples <= 1)
                    samples = 1;
                var passes = 0;
                long totalMs = 0;
                for (var i = 0; i < samples; i++)
                {
                    RunJournal.ResetForTesting();
                    var interpreter = new Interpreter(currentFile: path);
                    var started = DateTime.UtcNow;
                    try
                    {
                        interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
                        interpreter.ExecuteAsync(evalCase.Body).GetAwaiter().GetResult();
                        RunJudge(interpreter, evalCase);
                        passes++;
                    }
                    catch (Exception ex)
                    {
                        error.WriteLine($"{suite.Title}/{evalCase.Title}: {ex.Message}");
                    }

                    totalMs += (long)(DateTime.UtcNow - started).TotalMilliseconds;
                }

                var rate = samples == 0 ? 0 : (double)passes / samples;
                var row = new EvalCaseResult
                {
                    Suite = suite.Title,
                    Case = evalCase.Title,
                    PassRate = rate,
                    Samples = samples,
                    MeanMs = samples == 0 ? 0 : totalMs / (double)samples,
                    Threshold = evalCase.Threshold
                };
                report.Cases.Add(row);
                output.WriteLine($"{suite.Title} / {evalCase.Title}: {rate:P0} ({passes}/{samples}) mean {row.MeanMs:0}ms");
                if (rate + 1e-9 < evalCase.Threshold)
                    failed = true;
            }
        }

        if (update)
        {
            var dir = Path.GetDirectoryName(baseline);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(baseline, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"Wrote baseline {baseline}");
        }
        else if (File.Exists(baseline))
        {
            var previous = JsonSerializer.Deserialize<EvalReport>(File.ReadAllText(baseline));
            if (previous != null)
            {
                foreach (var row in report.Cases)
                {
                    var old = previous.Cases.FirstOrDefault(c => c.Suite == row.Suite && c.Case == row.Case);
                    if (old != null)
                    {
                        output.WriteLine($"  delta {row.Suite}/{row.Case}: pass {row.PassRate - old.PassRate:+0.00;-0.00} ms {row.MeanMs - old.MeanMs:+0;-0}");
                        if (row.PassRate + 1e-9 < old.PassRate)
                            failed = true;
                    }
                }
            }
        }

        return failed ? ExitFail : ExitOk;
    }

    private static void RunJudge(Interpreter interpreter, EvalCaseDeclaration evalCase)
    {
        if (evalCase.JudgePrompt == null)
            return;

        var judged = interpreter.EvaluateAsync(evalCase.JudgePrompt).GetAwaiter().GetResult();
        if (judged.Type == ValueType.Prompt)
        {
            judged = judged.AsPrompt().CallAsync(
                new List<RuntimeValue> { RuntimeValue.String(evalCase.Title) },
                interpreter).GetAwaiter().GetResult();
        }

        if (judged.Type == ValueType.Object && judged.AsObject() is JsonObject obj)
        {
            var ok = obj.Get("ok");
            if (ok.Type == ValueType.Boolean && !ok.AsBoolean())
                throw new RuntimeException($"judge rejected case '{evalCase.Title}'");
        }
    }
}

public sealed class EvalReport
{
    public List<EvalCaseResult> Cases { get; set; } = new();
}

public sealed class EvalCaseResult
{
    public string Suite { get; set; } = "";
    public string Case { get; set; } = "";
    public double PassRate { get; set; }
    public int Samples { get; set; }
    public double MeanMs { get; set; }
    public double Threshold { get; set; }
    public string? PromptHash { get; set; }
}
