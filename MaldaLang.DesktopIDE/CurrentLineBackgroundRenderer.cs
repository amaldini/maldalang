// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace MaldaLang.DesktopIDE;

/// <summary>
/// Renders a debugger execution highlight for the current statement.
/// Coordinates match AvalonEdit's own <c>CurrentLineHighlightRenderer</c>:
/// document Y minus <see cref="TextView.ScrollOffset"/>, on the Selection layer.
/// </summary>
public class CurrentLineBackgroundRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private Brush _backgroundBrush;
    private Brush _accentBrush;
    private Pen _borderPen;
    private int? _currentLine;

    public int? CurrentLine
    {
        get => _currentLine;
        set
        {
            if (_currentLine == value)
            {
                return;
            }

            _currentLine = value;
            Invalidate();
        }
    }

    public void Invalidate()
    {
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Selection layer so the wash sits above the editor background, same as
    /// AvalonEdit's caret-line highlighter. Background-layer draws are easy to miss.
    /// </summary>
    public KnownLayer Layer => KnownLayer.Selection;

    public CurrentLineBackgroundRenderer(TextEditor editor)
    {
        _editor = editor;
        _backgroundBrush = FreezeBrush(Color.FromArgb(90, 250, 204, 21));
        _accentBrush = FreezeBrush(Color.FromArgb(230, 217, 119, 6));
        _borderPen = FreezePen(Color.FromArgb(140, 217, 119, 6), 1);
    }

    /// <summary>
    /// Theme chrome accent is too faint as a full-line wash. Keep an amber
    /// execution highlight and only tint the left rail from the theme.
    /// </summary>
    public void SetColor(Color baseColor)
    {
        _backgroundBrush = FreezeBrush(Color.FromArgb(90, 250, 204, 21));
        _accentBrush = FreezeBrush(Color.FromArgb(230, baseColor.R, baseColor.G, baseColor.B));
        _borderPen = FreezePen(Color.FromArgb(160, 217, 119, 6), 1);
        Invalidate();
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (CurrentLine is not int currentLine)
        {
            return;
        }

        if (textView.Document == null)
        {
            return;
        }

        textView.EnsureVisualLines();
        var visualLine = textView.GetVisualLine(currentLine);
        if (visualLine == null)
        {
            return;
        }

        // VisualTop is document-relative; Draw() uses viewport coordinates.
        var linePosY = ToViewportY(visualLine.VisualTop, textView.ScrollOffset.Y);
        var width = Math.Max(0, textView.ActualWidth);
        var rect = new Rect(0, linePosY, width, visualLine.Height);
        drawingContext.DrawRectangle(_backgroundBrush, _borderPen, rect);
        drawingContext.DrawRectangle(_accentBrush, null, new Rect(0, linePosY, 3, visualLine.Height));
    }

    /// <summary>
    /// Convert a document-relative visual Y into a viewport Y for <see cref="IBackgroundRenderer.Draw"/>.
    /// </summary>
    public static double ToViewportY(double visualTop, double scrollOffsetY)
        => visualTop - scrollOffsetY;

    private static SolidColorBrush FreezeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FreezePen(Color color, double thickness)
    {
        var pen = new Pen(FreezeBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
