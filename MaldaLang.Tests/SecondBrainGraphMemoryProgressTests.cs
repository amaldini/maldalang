// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Mockup of the GraphMemory index progress / ETA helpers from
/// <c>Examples/Agents/secondbrain_semantic.malda</c> — runs the same syntax under
/// interpreter and C# transpile without loading LlamaEmbedder or GraphMemory.
/// </summary>
[Collection("Sequential")]
public class SecondBrainGraphMemoryProgressTests : TestBase
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    /// <summary>
    /// Mirrors formatDurationMs + indexBrainMemoryProgressLine + the progress loop
    /// control flow (now/or/modulo/formatDate/t with concat) used in secondbrain_semantic.
    /// </summary>
    private const string ProgressMockupSource = """
        var UI_LANG = "en";

        function t(en, it) {
            if (UI_LANG == "en") {
                return en;
            }
            return it;
        }

        function formatDurationMs(ms) {
            if (ms == null or ms < 0) {
                ms = 0;
            }
            var totalSec = int(ms / 1000);
            if (totalSec < 1) {
                return string(int(ms)) + "ms";
            }
            if (totalSec < 60) {
                return string(totalSec) + "s";
            }
            var hours = int(totalSec / 3600);
            var mins = int((totalSec % 3600) / 60);
            var secs = totalSec % 60;
            if (hours > 0) {
                return string(hours) + "h " + string(mins) + "m";
            }
            if (secs == 0) {
                return string(mins) + "m";
            }
            return string(mins) + "m " + string(secs) + "s";
        }

        function indexBrainMemoryProgressLine(indexed, total, startedAt) {
            var pct = 0;
            if (total > 0) {
                pct = int((indexed * 100) / total);
            }
            var line = "[dim][[" + string(indexed) + "/" + string(total) + "]][/] " +
                string(pct) + "%";
            if (indexed <= 0) {
                return line;
            }
            var elapsed = now() - startedAt;
            var avgMs = elapsed / indexed;
            var remaining = total - indexed;
            line = line + "  ~" + formatDurationMs(avgMs) + t("/note", "/nota");
            if (remaining <= 0) {
                return line + "  " + t("elapsed ", "trascorsi ") + formatDurationMs(elapsed);
            }
            var etaMs = avgMs * remaining;
            var finishAt = formatDate(now() + etaMs, "HH:mm");
            return line + "  " + t(
                "ETA " + formatDurationMs(etaMs) + " left (~" + finishAt + ")",
                "fine tra " + formatDurationMs(etaMs) + " (~" + finishAt + ")");
        }

        function mockIndexNotes(notes) {
            var total = 0;
            if (notes != null) {
                total = notes.length;
            }
            print("START " + string(total));
            var indexed = 0;
            var startedAt = now();
            var lastProgressAt = 0;
            foreach (var note in notes) {
                sleep(25);
                indexed = indexed + 1;
                var due = (now() - lastProgressAt) >= 0;
                if (indexed == 1 or indexed == total or due) {
                    print(indexBrainMemoryProgressLine(indexed, total, startedAt));
                    lastProgressAt = now();
                }
            }
            var elapsed = now() - startedAt;
            print("DONE " + string(indexed) + " " + formatDurationMs(elapsed));
            print("SAVE " + t("Saving GraphMemory artifacts...",
                "Salvataggio artefatti GraphMemory..."));
            print("SUMMARY " + formatDurationMs(elapsed) +
                t("; save ", "; salvataggio ") + formatDurationMs(5));
        }

        print("D_MS=" + formatDurationMs(500));
        print("D_S=" + formatDurationMs(4500));
        print("D_M=" + formatDurationMs(125000));
        print("D_H=" + formatDurationMs(3723000));
        print("D_NULL=" + formatDurationMs(null));
        print("D_NEG=" + formatDurationMs(-10));

        var notes = [
            { "slug": "a" },
            { "slug": "b" },
            { "slug": "c" }
        ];
        mockIndexNotes(notes);
        """;

    private static void AssertProgressMockupOutput(string output)
    {
        Assert.Contains("D_MS=500ms", output, StringComparison.Ordinal);
        Assert.Contains("D_S=4s", output, StringComparison.Ordinal);
        Assert.Contains("D_M=2m 5s", output, StringComparison.Ordinal);
        Assert.Contains("D_H=1h 2m", output, StringComparison.Ordinal);
        Assert.Contains("D_NULL=0ms", output, StringComparison.Ordinal);
        Assert.Contains("D_NEG=0ms", output, StringComparison.Ordinal);

        Assert.Contains("START 3", output, StringComparison.Ordinal);
        Assert.Contains("[[1/3]]", output, StringComparison.Ordinal);
        Assert.Contains("[[2/3]]", output, StringComparison.Ordinal);
        Assert.Contains("[[3/3]]", output, StringComparison.Ordinal);
        Assert.Contains("/note", output, StringComparison.Ordinal);
        Assert.Contains("ETA ", output, StringComparison.Ordinal);
        Assert.Contains(" left (~", output, StringComparison.Ordinal);
        Assert.Contains("elapsed ", output, StringComparison.Ordinal);
        Assert.Contains("DONE 3 ", output, StringComparison.Ordinal);
        Assert.Contains("Saving GraphMemory artifacts...", output, StringComparison.Ordinal);
        Assert.Contains("; save ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticHost_DefinesGraphMemoryProgressHelpers()
    {
        var path = Path.Combine(RepoRoot, "Examples", "Agents", "secondbrain_semantic.malda");
        Assert.True(File.Exists(path), "missing " + path);
        var source = File.ReadAllText(path);
        Assert.Contains("function formatDurationMs(ms)", source, StringComparison.Ordinal);
        Assert.Contains("function indexBrainMemoryProgressLine(", source, StringComparison.Ordinal);
        Assert.Contains("formatDate(now() + etaMs, \"HH:mm\")", source, StringComparison.Ordinal);
        Assert.Contains("(now() - lastProgressAt) >= 1500", source, StringComparison.Ordinal);
        Assert.Contains("indexed == 1 or indexed == total or due", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphMemoryProgressMockup_RunsUnderInterpreter()
    {
        var output = RunProgram(ProgressMockupSource);
        AssertProgressMockupOutput(output);
    }

    [Fact]
    public void GraphMemoryProgressMockup_RunsUnderTranspile()
    {
        var result = TranspiledTestRunner.CompileAndRunFromSource(ProgressMockupSource);
        Assert.Equal(0, result.ExitCode);
        Assert.True(
            string.IsNullOrWhiteSpace(result.StdErr) ||
            !result.StdErr.Contains("error", StringComparison.OrdinalIgnoreCase),
            "unexpected stderr: " + result.StdErr);
        AssertProgressMockupOutput(result.StdOut);
    }

    [Fact]
    public void GraphMemoryProgressMockup_InterpreterAndTranspileAgreeOnDurations()
    {
        var interpreted = RunProgram(ProgressMockupSource);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(ProgressMockupSource).StdOut;

        // Duration unit tests are deterministic; progress ETA clock may differ by a second.
        foreach (var marker in new[]
                 {
                     "D_MS=500ms", "D_S=4s", "D_M=2m 5s", "D_H=1h 2m",
                     "D_NULL=0ms", "D_NEG=0ms", "START 3", "DONE 3 "
                 })
        {
            Assert.Contains(marker, interpreted, StringComparison.Ordinal);
            Assert.Contains(marker, transpiled, StringComparison.Ordinal);
        }
    }
}
