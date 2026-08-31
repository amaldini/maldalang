// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class SecondBrainRetrievalEvalTests : TestBase
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string EvalDir =>
        Path.Combine(RepoRoot, "Examples", "Agents", "sb", "eval");

    [Fact]
    public void RetrievalEval_FixtureFilesExist()
    {
        Assert.True(File.Exists(Path.Combine(EvalDir, "questions.json")), "missing eval questions");
        Assert.True(File.Exists(Path.Combine(EvalDir, "catalog.json")), "missing eval catalog");
        Assert.True(File.Exists(Path.Combine(EvalDir, "run_retrieval_eval.malda")), "missing eval runner");
        var common = File.ReadAllText(Path.Combine(RepoRoot, "Examples", "Agents", "sb", "05-ask-common.malda"));
        Assert.Contains("function retrievalHitAtK(", common, StringComparison.Ordinal);
        Assert.Contains("function selectNotes(", common, StringComparison.Ordinal);
        Assert.Contains("function expandAskRetrievalQuery(", common, StringComparison.Ordinal);
        Assert.Contains("function askCompletedHistoryTurns(", common, StringComparison.Ordinal);
        var questions = File.ReadAllText(Path.Combine(EvalDir, "questions.json"));
        Assert.Contains("expectSlugs", questions, StringComparison.Ordinal);
        Assert.Contains("photovoltaic-inverters", questions, StringComparison.Ordinal);
        Assert.Contains("priorId", questions, StringComparison.Ordinal);
        Assert.Contains("q-inverter-followup-night", questions, StringComparison.Ordinal);
        Assert.Contains("q-topic-switch-indemnity", questions, StringComparison.Ordinal);
        var runner = File.ReadAllText(Path.Combine(EvalDir, "run_retrieval_eval.malda"));
        Assert.Contains("expandAskRetrievalQuery(", runner, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrievalEval_LexicalHitAtK_RunsUnderInterpreter()
    {
        var prev = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(RepoRoot);
            var output = await InterpretEvalAsync();
            Assert.Contains("EVAL_PASS", output, StringComparison.Ordinal);
            Assert.Contains("HIT_AT_K=8/8", output, StringComparison.Ordinal);
            Assert.DoesNotContain("EVAL_FAIL", output, StringComparison.Ordinal);
            Assert.DoesNotContain("MISS ", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.SetCurrentDirectory(prev);
        }
    }

    private static async Task<string> InterpretEvalAsync()
    {
        var sourcePath = Path.Combine(EvalDir, "run_retrieval_eval.malda");
        var source = await File.ReadAllTextAsync(sourcePath);
        var lexer = new Lexer(source, sourcePath);
        var parser = new MaldaLang.Parser.Parser(lexer.Tokenize(), sourcePath);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var interpreter = new Interpreter.Interpreter();
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
