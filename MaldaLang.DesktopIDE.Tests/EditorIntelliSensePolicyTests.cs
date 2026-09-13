// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class EditorIntelliSensePolicyTests
{
    [Fact]
    public void Letter_WhenListAlreadyOpen_DoesNotRequery()
    {
        Assert.False(EditorIntelliSensePolicy.ShouldQueryCompletions("m", completionWindowOpen: true, manual: false));
        Assert.False(EditorIntelliSensePolicy.ShouldCloseExistingCompletion("m", manual: false));
    }

    [Fact]
    public void FirstLetter_WhenListClosed_QueriesLanguageService()
    {
        Assert.True(EditorIntelliSensePolicy.ShouldQueryCompletions("p", completionWindowOpen: false, manual: false));
        Assert.False(EditorIntelliSensePolicy.IsImmediateCompletionQuery(manual: false, "p"));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("@")]
    [InlineData("(")]
    public void MemberDecoratorOrCallTrigger_RequeriesAndClosesExistingList(string trigger)
    {
        Assert.True(EditorIntelliSensePolicy.ShouldQueryCompletions(trigger, completionWindowOpen: true, manual: false));
        Assert.True(EditorIntelliSensePolicy.ShouldCloseExistingCompletion(trigger, manual: false));
        Assert.True(EditorIntelliSensePolicy.IsImmediateCompletionQuery(manual: false, trigger));
    }

    [Fact]
    public void CtrlSpace_AlwaysRequeriesImmediately()
    {
        Assert.True(EditorIntelliSensePolicy.ShouldQueryCompletions("x", completionWindowOpen: true, manual: true));
        Assert.True(EditorIntelliSensePolicy.ShouldCloseExistingCompletion("x", manual: true));
        Assert.True(EditorIntelliSensePolicy.IsImmediateCompletionQuery(manual: true, "x"));
    }

    [Fact]
    public void SignatureHelp_SchedulesOnCallPunctuationOrCaretMove()
    {
        Assert.True(EditorIntelliSensePolicy.ShouldScheduleSignatureHelp("(", caretMoved: false));
        Assert.True(EditorIntelliSensePolicy.ShouldScheduleSignatureHelp(",", caretMoved: false));
        Assert.True(EditorIntelliSensePolicy.ShouldScheduleSignatureHelp(null, caretMoved: true));
        Assert.False(EditorIntelliSensePolicy.ShouldScheduleSignatureHelp("a", caretMoved: false));
    }
}
