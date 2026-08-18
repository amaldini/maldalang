// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Mockup of the GraphMemory index progress / ETA helpers from
/// <c>Examples/Agents/sb/06-memory.malda</c> — runs the same syntax under
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
        var path = Path.Combine(RepoRoot, "Examples", "Agents", "sb", "06-memory.malda");
        Assert.True(File.Exists(path), "missing " + path);
        var source = File.ReadAllText(path);
        Assert.Contains("function formatDurationMs(ms)", source, StringComparison.Ordinal);
        Assert.Contains("function indexBrainMemoryProgressLine(", source, StringComparison.Ordinal);
        Assert.Contains("function indexShouldUseFullRebuild(", source, StringComparison.Ordinal);
        Assert.Contains("function upsertNoteMemory(", source, StringComparison.Ordinal);
        Assert.Contains("function catalogHasMemoryNodeIds(", source, StringComparison.Ordinal);
        Assert.Contains("function embedFingerprintMatches(", source, StringComparison.Ordinal);
        Assert.Contains("function rememberNoteMemory(", source, StringComparison.Ordinal);
        Assert.Contains("ctx.removedNodeIds", source, StringComparison.Ordinal);
        Assert.Contains("note.memoryNodeId = nodeId", source, StringComparison.Ordinal);
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

    /// <summary>
    /// Mirrors incremental GraphMemory UPDATE decisions from
    /// <c>Examples/Agents/sb/06-memory.malda</c> without LlamaEmbedder or GraphMemory.
    /// </summary>
    private const string IncrementalIndexMockupSource = """
        var embedMode = "hash";
        var EMBED_DIM = 1024;

        function noteMemoryId(note) {
            if (note == null or note.memoryNodeId == null) {
                return "";
            }
            return str.trim(string(note.memoryNodeId));
        }

        function catalogHasMemoryNodeIds(notes) {
            if (notes == null) {
                return false;
            }
            foreach (var note in notes) {
                if (noteMemoryId(note) != "") {
                    return true;
                }
            }
            return false;
        }

        function embedFingerprintMatches(catalog) {
            if (catalog == null) {
                return false;
            }
            var mode = "";
            if (catalog.embedMode != null) {
                mode = str.trim(string(catalog.embedMode));
            }
            if (mode == "") {
                return false;
            }
            var dim = 0;
            if (catalog.embedDim != null) {
                dim = int(catalog.embedDim);
            }
            return mode == string(embedMode) and dim == EMBED_DIM;
        }

        function indexShouldUseFullRebuild(ctx, artifactsExist, fingerprintOk, catalogHasIds) {
            if (ctx == null) {
                return true;
            }
            if (ctx.forceFull == true) {
                return true;
            }
            var mode = "";
            if (ctx.mode != null) {
                mode = str.trim(string(ctx.mode));
            }
            if (mode != "incremental") {
                return true;
            }
            if (artifactsExist != true) {
                return true;
            }
            if (fingerprintOk != true) {
                return true;
            }
            if (catalogHasIds != true) {
                return true;
            }
            return false;
        }

        function listContains(items, value) {
            foreach (var item in items) {
                if (item == value) {
                    return true;
                }
            }
            return false;
        }

        function planIncremental(notes, removedNodeIds) {
            var forget = [];
            foreach (var id in removedNodeIds) {
                var nid = str.trim(string(id));
                if (nid != "" and not listContains(forget, nid)) {
                    forget.append(nid);
                }
            }
            var skip = [];
            var upsert = [];
            foreach (var note in notes) {
                var existing = noteMemoryId(note);
                if (existing != "") {
                    skip.append(note.slug);
                } else {
                    upsert.append(note.slug);
                }
            }
            print("FORGET=" + str.join(forget, ","));
            print("SKIP=" + str.join(skip, ","));
            print("UPSERT=" + str.join(upsert, ","));
        }

        print("ID_EMPTY=" + noteMemoryId(null));
        print("ID_TRIM=" + noteMemoryId({ "memoryNodeId": " node_3 " }));
        print("HAS_IDS=" + string(catalogHasMemoryNodeIds([
            { "slug": "a" },
            { "slug": "b", "memoryNodeId": "node_1" }
        ])));
        print("NO_IDS=" + string(catalogHasMemoryNodeIds([
            { "slug": "a" },
            { "slug": "b" }
        ])));
        print("FP_OK=" + string(embedFingerprintMatches({
            "embedMode": "hash",
            "embedDim": 1024
        })));
        print("FP_MODE=" + string(embedFingerprintMatches({
            "embedMode": "llama",
            "embedDim": 1024
        })));
        print("FP_DIM=" + string(embedFingerprintMatches({
            "embedMode": "hash",
            "embedDim": 384
        })));
        print("FP_LEGACY=" + string(embedFingerprintMatches({ "retrieval": "graphmemory" })));
        print("FP_NULL=" + string(embedFingerprintMatches(null)));

        print("FULL_NULL=" + string(indexShouldUseFullRebuild(null, true, true, true)));
        print("FULL_FORCE=" + string(indexShouldUseFullRebuild({
            "mode": "incremental",
            "forceFull": true
        }, true, true, true)));
        print("FULL_MODE=" + string(indexShouldUseFullRebuild({
            "mode": "full"
        }, true, true, true)));
        print("FULL_ART=" + string(indexShouldUseFullRebuild({
            "mode": "incremental"
        }, false, true, true)));
        print("FULL_FP=" + string(indexShouldUseFullRebuild({
            "mode": "incremental"
        }, true, false, true)));
        print("FULL_IDS=" + string(indexShouldUseFullRebuild({
            "mode": "incremental"
        }, true, true, false)));
        print("INCR_OK=" + string(indexShouldUseFullRebuild({
            "mode": "incremental",
            "forceFull": false
        }, true, true, true)));

        planIncremental([
            { "slug": "kept", "memoryNodeId": "node_0" },
            { "slug": "fresh" },
            { "slug": "changed" }
        ], ["node_2", "node_9"]);
        """;

    private static void AssertIncrementalIndexMockupOutput(string output)
    {
        Assert.Contains("ID_EMPTY=", output, StringComparison.Ordinal);
        Assert.Contains("ID_TRIM=node_3", output, StringComparison.Ordinal);
        Assert.Contains("HAS_IDS=true", output, StringComparison.Ordinal);
        Assert.Contains("NO_IDS=false", output, StringComparison.Ordinal);
        Assert.Contains("FP_OK=true", output, StringComparison.Ordinal);
        Assert.Contains("FP_MODE=false", output, StringComparison.Ordinal);
        Assert.Contains("FP_DIM=false", output, StringComparison.Ordinal);
        Assert.Contains("FP_LEGACY=false", output, StringComparison.Ordinal);
        Assert.Contains("FP_NULL=false", output, StringComparison.Ordinal);
        Assert.Contains("FULL_NULL=true", output, StringComparison.Ordinal);
        Assert.Contains("FULL_FORCE=true", output, StringComparison.Ordinal);
        Assert.Contains("FULL_MODE=true", output, StringComparison.Ordinal);
        Assert.Contains("FULL_ART=true", output, StringComparison.Ordinal);
        Assert.Contains("FULL_FP=true", output, StringComparison.Ordinal);
        Assert.Contains("FULL_IDS=true", output, StringComparison.Ordinal);
        Assert.Contains("INCR_OK=false", output, StringComparison.Ordinal);
        Assert.Contains("FORGET=node_2,node_9", output, StringComparison.Ordinal);
        Assert.Contains("SKIP=kept", output, StringComparison.Ordinal);
        Assert.Contains("UPSERT=fresh,changed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void IncrementalIndexMockup_RunsUnderInterpreter()
    {
        var output = RunProgram(IncrementalIndexMockupSource);
        AssertIncrementalIndexMockupOutput(output);
    }

    [Fact]
    public void IncrementalIndexMockup_RunsUnderTranspile()
    {
        var result = TranspiledTestRunner.CompileAndRunFromSource(IncrementalIndexMockupSource);
        Assert.Equal(0, result.ExitCode);
        Assert.True(
            string.IsNullOrWhiteSpace(result.StdErr) ||
            !result.StdErr.Contains("error", StringComparison.OrdinalIgnoreCase),
            "unexpected stderr: " + result.StdErr);
        AssertIncrementalIndexMockupOutput(result.StdOut);
    }
}
