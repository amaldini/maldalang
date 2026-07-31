// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using Xunit;

namespace MaldaLang.Tests;

public class ReadFileToolLogTests
{
    [Fact]
    public void FormatReadFileLogSummary_FullFile_ShowsLineCount()
    {
        var summary = ConversationInstance.FormatReadFileLogSummary(
            """{"filePath":"PRD.md"}""",
            "line one\nline two\nline three");

        Assert.Equal("full file · 3 lines", summary);
    }

    [Fact]
    public void FormatReadFileLogSummary_Range_ShowsRequestedRangeAndCount()
    {
        var summary = ConversationInstance.FormatReadFileLogSummary(
            """{"filePath":"snake.html","startLine":10,"endLine":25}""",
            string.Join('\n', Enumerable.Range(1, 16).Select(i => $"row {i}")));

        Assert.Equal("lines 10-25 · 16 lines", summary);
    }

    [Fact]
    public void FormatReadFileLogSummary_StartOnlyPositive_ShowsOpenEndedRange()
    {
        var summary = ConversationInstance.FormatReadFileLogSummary(
            """{"filePath":"app.js","startLine":40}""",
            "only\none\nchunk");

        Assert.Equal("lines 40-end · 3 lines", summary);
    }

    [Fact]
    public void FormatReadFileLogSummary_StartOnlyNegative_ShowsTailRange()
    {
        var summary = ConversationInstance.FormatReadFileLogSummary(
            """{"filePath":"app.js","startLine":-30}""",
            "tail\nlines");

        Assert.Equal("last 30 lines · 2 lines", summary);
    }

    [Fact]
    public void CountContentLines_EmptyContent_IsZero()
    {
        Assert.Equal(0, ConversationInstance.CountContentLines(""));
        Assert.Equal(0, ConversationInstance.CountContentLines(null));
    }
}
