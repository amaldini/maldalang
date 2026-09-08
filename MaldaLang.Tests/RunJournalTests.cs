// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Runtime.Journal;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class RunJournalTests : TestBase
{
    [Fact]
    public void CapRead_AndEvalPrompt_JournalEvents()
    {
        RunJournal.ResetForTesting();
        var output = RunProgram("""
            schema Card { name: string; }
            prompt extract(raw) -> Card { user: raw; }
            var checked = evalPrompt(extract("Ada"), dict { "name": "Ada" });
            var token = cap.dirList(".");
            cap.list(token);
            var j = trace.journal();
            print(j.length >= 1);
            """);
        Assert.Contains("true", output);
    }

    [Fact]
    public void SpanNestsEvents()
    {
        RunJournal.ResetForTesting();
        var output = RunProgram("""
            trace.span("outer", () => {
                var token = cap.dirList(".");
                cap.list(token);
            });
            var j = trace.journal();
            print(j.length >= 1);
            """);
        Assert.Contains("true", output);
    }
}
