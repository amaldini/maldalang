// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using ICSharpCode.AvalonEdit.Document;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.IDE.Models;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class EditorUndoableTextTests
{
    [Fact]
    public void ReplaceAll_RecordsSingleUndoThatRestoresOriginalText()
    {
        var document = new TextDocument("let foo = 1");

        EditorUndoableText.ReplaceAll(document, "let bar = 1");

        Assert.Equal("let bar = 1", document.Text);
        Assert.True(document.UndoStack.CanUndo);
        document.UndoStack.Undo();
        Assert.Equal("let foo = 1", document.Text);
    }

    [Fact]
    public void ReplaceAll_SameText_DoesNotPushUndo()
    {
        var document = new TextDocument("unchanged");

        EditorUndoableText.ReplaceAll(document, "unchanged");

        Assert.False(document.UndoStack.CanUndo);
    }

    [Fact]
    public void ApplyEdits_GroupsRenameOccurrencesIntoOneUndo()
    {
        var document = new TextDocument("let foo = foo + foo");
        var edits = new[]
        {
            new TextEditInfo { Span = new TextSpanInfo { Line = 0, Column = 4, Length = 3 }, NewText = "bar" },
            new TextEditInfo { Span = new TextSpanInfo { Line = 0, Column = 10, Length = 3 }, NewText = "bar" },
            new TextEditInfo { Span = new TextSpanInfo { Line = 0, Column = 16, Length = 3 }, NewText = "bar" }
        };

        var applied = EditorUndoableText.ApplyEdits(document, edits);

        Assert.Equal(3, applied);
        Assert.Equal("let bar = bar + bar", document.Text);
        Assert.True(document.UndoStack.CanUndo);
        document.UndoStack.Undo();
        Assert.Equal("let foo = foo + foo", document.Text);
        Assert.False(document.UndoStack.CanUndo);
    }

    [Fact]
    public void ApplyEdits_LeavesEarlierTypingOnTheUndoStack()
    {
        var document = new TextDocument("let foo = 1");
        document.Insert(document.TextLength, "2");

        EditorUndoableText.ApplyEdits(document, new[]
        {
            new TextEditInfo { Span = new TextSpanInfo { Line = 0, Column = 4, Length = 3 }, NewText = "bar" }
        });

        Assert.Equal("let bar = 12", document.Text);
        document.UndoStack.Undo();
        Assert.Equal("let foo = 12", document.Text);
        document.UndoStack.Undo();
        Assert.Equal("let foo = 1", document.Text);
    }

    [Fact]
    public void ApplyEdits_InsertsAutofixAtEndOfLine()
    {
        var document = new TextDocument("print(\"hi\"");
        var edit = EditorQuickFixService.ToEdit(new AutoFixInfo
        {
            Description = "Insert missing ')'",
            Line = 0,
            Column = 10,
            TextToInsert = ")",
            LengthToReplace = 0
        });

        var applied = EditorUndoableText.ApplyEdits(document, new[] { edit });

        Assert.Equal(1, applied);
        Assert.Equal("print(\"hi\")", document.Text);
        document.UndoStack.Undo();
        Assert.Equal("print(\"hi\"", document.Text);
    }

    [Fact]
    public void ApplyEdits_AppliesNonSimpleAndSimpleFixesInOneUndo()
    {
        var document = new TextDocument("print(\"hi\"\nfoo");
        var service = new EditorQuickFixService();
        var edits = service.ToBatchEdits(new[]
        {
            new Diagnostic
            {
                AutoFix = new AutoFixInfo
                {
                    Description = "Insert missing ')'",
                    Line = 0,
                    Column = 10,
                    TextToInsert = ")",
                    LengthToReplace = 0,
                    IsSimpleCharacterFix = true
                }
            },
            new Diagnostic
            {
                AutoFix = new AutoFixInfo
                {
                    Description = "Insert missing ';'",
                    Line = 1,
                    Column = 3,
                    TextToInsert = ";",
                    LengthToReplace = 0,
                    IsSimpleCharacterFix = false
                }
            }
        });

        var applied = EditorUndoableText.ApplyEdits(document, edits);

        Assert.Equal(2, applied);
        Assert.Equal("print(\"hi\")\nfoo;", document.Text);
        document.UndoStack.Undo();
        Assert.Equal("print(\"hi\"\nfoo", document.Text);
    }
}
