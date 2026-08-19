// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace MaldaLang.DesktopIDE;

/// <summary>
/// Glyph margin lightbulb on lines that have a language-service autofix.
/// </summary>
public sealed class QuickFixMargin : AbstractMargin
{
    private static readonly SolidColorBrush BulbBrush = CreateFrozenBrush(Color.FromRgb(234, 179, 8));
    private static readonly SolidColorBrush StemBrush = CreateFrozenBrush(Color.FromRgb(202, 138, 4));

    private readonly TextArea _textArea;
    private readonly MainWindow _mainWindow;

    public QuickFixMargin(TextArea textArea, MainWindow mainWindow)
    {
        _textArea = textArea;
        _mainWindow = mainWindow;
        _textArea.TextView.VisualLinesChanged += (_, _) => InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(16, 0);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var textView = _textArea.TextView;
        if (textView.VisualLines.Count == 0)
        {
            return;
        }

        var width = RenderSize.Width;
        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (!_mainWindow.LineHasQuickFix(lineNumber))
            {
                continue;
            }

            var lineTop = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextTop)
                          - textView.VerticalOffset;
            var lineBottom = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextBottom)
                             - textView.VerticalOffset;
            var centerX = width / 2;
            var centerY = (lineTop + lineBottom) / 2;

            drawingContext.DrawEllipse(BulbBrush, null, new Point(centerX, centerY - 1), 4.2, 4.6);
            drawingContext.DrawRectangle(StemBrush, null, new Rect(centerX - 2, centerY + 3.2, 4, 2.2));
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var textView = _textArea.TextView;
        var pos = e.GetPosition(this);
        foreach (var visualLine in textView.VisualLines)
        {
            var lineTop = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextTop)
                          - textView.VerticalOffset;
            var lineBottom = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextBottom)
                             - textView.VerticalOffset;
            if (pos.Y < lineTop || pos.Y > lineBottom)
            {
                continue;
            }

            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (_mainWindow.LineHasQuickFix(lineNumber))
            {
                _mainWindow.ShowQuickFixesForEditorLine(lineNumber);
                e.Handled = true;
            }

            break;
        }
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
