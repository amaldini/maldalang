// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Decides when Desktop IntelliSense should re-query <see cref="MaldaLang.IDE.Services.LanguageService"/>.
/// AvalonEdit already filters an open completion list as the user types, so rebuilding
/// (and re-parsing) on every letter freezes the UI thread.
/// </summary>
public static class EditorIntelliSensePolicy
{
    public static bool ShouldQueryCompletions(string? triggerText, bool completionWindowOpen, bool manual)
    {
        if (manual)
        {
            return true;
        }

        if (string.IsNullOrEmpty(triggerText))
        {
            return false;
        }

        var ch = triggerText[0];
        if (IsCompletionRequeryTrigger(ch))
        {
            return true;
        }

        if (completionWindowOpen)
        {
            return false;
        }

        return char.IsLetterOrDigit(ch);
    }

    public static bool ShouldCloseExistingCompletion(string? triggerText, bool manual)
    {
        if (manual)
        {
            return true;
        }

        if (string.IsNullOrEmpty(triggerText))
        {
            return false;
        }

        return IsCompletionRequeryTrigger(triggerText[0]);
    }

    public static bool IsImmediateCompletionQuery(bool manual, string? triggerText)
    {
        if (manual)
        {
            return true;
        }

        return !string.IsNullOrEmpty(triggerText) && IsCompletionRequeryTrigger(triggerText[0]);
    }

    public static bool ShouldScheduleSignatureHelp(string? triggerText, bool caretMoved)
    {
        if (caretMoved)
        {
            return true;
        }

        if (string.IsNullOrEmpty(triggerText))
        {
            return false;
        }

        var ch = triggerText[0];
        return ch is '(' or ',' or ')';
    }

    private static bool IsCompletionRequeryTrigger(char ch) => ch is '.' or '@' or '(';
}
