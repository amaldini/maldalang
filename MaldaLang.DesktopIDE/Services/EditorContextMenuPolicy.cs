// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

public enum EditorContextMenuCommand
{
    Cut,
    Copy,
    Paste,
    GoToDefinition,
    FindReferences,
    RenameSymbol,
    QuickFix
}

public readonly record struct EditorContextMenuContext(
    bool HasSelection,
    bool ClipboardHasText,
    bool HasDefinition,
    bool HasRenameTarget,
    bool HasQuickFix);

public readonly record struct EditorContextMenuState(
    bool CanCut,
    bool CanCopy,
    bool CanPaste,
    bool CanGoToDefinition,
    bool CanFindReferences,
    bool CanRenameSymbol,
    bool CanQuickFix);

/// <summary>
/// Caret placement and item enablement for the Desktop IDE editor context menu.
/// WPF wiring stays in <c>MainWindow</c>.
/// </summary>
public static class EditorContextMenuPolicy
{
    /// <summary>
    /// Right-click inside an existing selection keeps it (Cut/Copy stay useful).
    /// Clicks outside the selection move the caret so Rename / Go to Definition
    /// apply to the word under the pointer.
    /// </summary>
    public static bool ShouldMoveCaretToClick(int clickOffset, int selectionStart, int selectionLength)
    {
        if (selectionLength <= 0)
        {
            return true;
        }

        var selectionEnd = selectionStart + selectionLength;
        return clickOffset < selectionStart || clickOffset > selectionEnd;
    }

    public static EditorContextMenuState Resolve(EditorContextMenuContext context)
    {
        return new EditorContextMenuState(
            CanCut: context.HasSelection,
            CanCopy: context.HasSelection,
            CanPaste: context.ClipboardHasText,
            CanGoToDefinition: context.HasDefinition,
            CanFindReferences: context.HasRenameTarget,
            CanRenameSymbol: context.HasRenameTarget,
            CanQuickFix: context.HasQuickFix);
    }

    public static bool IsCommandEnabled(EditorContextMenuCommand command, EditorContextMenuState state)
    {
        return command switch
        {
            EditorContextMenuCommand.Cut => state.CanCut,
            EditorContextMenuCommand.Copy => state.CanCopy,
            EditorContextMenuCommand.Paste => state.CanPaste,
            EditorContextMenuCommand.GoToDefinition => state.CanGoToDefinition,
            EditorContextMenuCommand.FindReferences => state.CanFindReferences,
            EditorContextMenuCommand.RenameSymbol => state.CanRenameSymbol,
            EditorContextMenuCommand.QuickFix => state.CanQuickFix,
            _ => true
        };
    }
}
