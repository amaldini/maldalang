// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace MaldaLang.DesktopIDE;

/// <summary>
/// Renders a subtle background highlight for the current debugger line.
/// </summary>
public class CurrentLineBackgroundRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private Brush _backgroundBrush;

    public int? CurrentLine { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public CurrentLineBackgroundRenderer(TextEditor editor)
    {
        _editor = editor;
        // Default to a semi-transparent yellow highlight
        _backgroundBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 0));
    }

    /// <summary>
    /// Update the base color (alpha is applied automatically).
    /// </summary>
    public void SetColor(Color baseColor)
    {
        _backgroundBrush = new SolidColorBrush(Color.FromArgb(56, baseColor.R, baseColor.G, baseColor.B));
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (CurrentLine == null)
            return;

        if (textView.Document == null)
            return;

        // Ensure visual lines are generated
        textView.EnsureVisualLines();

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (lineNumber != CurrentLine.Value)
                continue;

            var top = visualLine.GetTextLineVisualYPosition(
                visualLine.TextLines[0],
                VisualYPosition.TextTop);
            var bottom = visualLine.GetTextLineVisualYPosition(
                visualLine.TextLines[0],
                VisualYPosition.TextBottom);

            var rect = new Rect(0, top, textView.ActualWidth, bottom - top);
            drawingContext.DrawRectangle(_backgroundBrush, null, rect);
        }
    }
}
