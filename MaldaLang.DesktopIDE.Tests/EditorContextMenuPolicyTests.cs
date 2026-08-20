// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class EditorContextMenuPolicyTests
{
    [Fact]
    public void ShouldMoveCaretToClick_WhenNothingSelected()
    {
        Assert.True(EditorContextMenuPolicy.ShouldMoveCaretToClick(12, selectionStart: 0, selectionLength: 0));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(10)]
    public void ShouldMoveCaretToClick_InsideSelection_KeepsSelection(int clickOffset)
    {
        Assert.False(EditorContextMenuPolicy.ShouldMoveCaretToClick(clickOffset, selectionStart: 4, selectionLength: 6));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    public void ShouldMoveCaretToClick_OutsideSelection_MovesCaret(int clickOffset)
    {
        Assert.True(EditorContextMenuPolicy.ShouldMoveCaretToClick(clickOffset, selectionStart: 4, selectionLength: 6));
    }

    [Fact]
    public void Resolve_EnablesClipboardAndSymbolCommandsFromContext()
    {
        var state = EditorContextMenuPolicy.Resolve(new EditorContextMenuContext(
            HasSelection: true,
            ClipboardHasText: true,
            HasDefinition: true,
            HasRenameTarget: true,
            HasQuickFix: true));

        Assert.True(state.CanCut);
        Assert.True(state.CanCopy);
        Assert.True(state.CanPaste);
        Assert.True(state.CanGoToDefinition);
        Assert.True(state.CanFindReferences);
        Assert.True(state.CanRenameSymbol);
        Assert.True(state.CanQuickFix);
    }

    [Fact]
    public void Resolve_DisablesUnavailableCommands()
    {
        var state = EditorContextMenuPolicy.Resolve(new EditorContextMenuContext(
            HasSelection: false,
            ClipboardHasText: false,
            HasDefinition: false,
            HasRenameTarget: false,
            HasQuickFix: false));

        Assert.False(state.CanCut);
        Assert.False(state.CanCopy);
        Assert.False(state.CanPaste);
        Assert.False(state.CanGoToDefinition);
        Assert.False(state.CanFindReferences);
        Assert.False(state.CanRenameSymbol);
        Assert.False(state.CanQuickFix);
    }

    [Fact]
    public void Resolve_AllowsRenameWithoutADefinition()
    {
        var state = EditorContextMenuPolicy.Resolve(new EditorContextMenuContext(
            HasSelection: false,
            ClipboardHasText: true,
            HasDefinition: false,
            HasRenameTarget: true,
            HasQuickFix: false));

        Assert.False(state.CanGoToDefinition);
        Assert.True(state.CanFindReferences);
        Assert.True(state.CanRenameSymbol);
        Assert.True(state.CanPaste);
    }

    [Theory]
    [InlineData(EditorContextMenuCommand.Cut, false)]
    [InlineData(EditorContextMenuCommand.Copy, false)]
    [InlineData(EditorContextMenuCommand.Paste, true)]
    [InlineData(EditorContextMenuCommand.GoToDefinition, false)]
    [InlineData(EditorContextMenuCommand.FindReferences, true)]
    [InlineData(EditorContextMenuCommand.RenameSymbol, true)]
    [InlineData(EditorContextMenuCommand.QuickFix, false)]
    public void IsCommandEnabled_MatchesResolvedState(EditorContextMenuCommand command, bool expected)
    {
        var state = EditorContextMenuPolicy.Resolve(new EditorContextMenuContext(
            HasSelection: false,
            ClipboardHasText: true,
            HasDefinition: false,
            HasRenameTarget: true,
            HasQuickFix: false));

        Assert.Equal(expected, EditorContextMenuPolicy.IsCommandEnabled(command, state));
    }
}
