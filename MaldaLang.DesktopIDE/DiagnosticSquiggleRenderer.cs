// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.IDE.Models;

namespace MaldaLang.DesktopIDE;

/// <summary>
/// Draws wavy underlines for diagnostics in the active editor document.
/// </summary>
public sealed class DiagnosticSquiggleRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly Pen _errorPen;
    private readonly Pen _warningPen;
    private readonly Pen _infoPen;
    private IReadOnlyList<EditorDiagnosticSpan> _spans = Array.Empty<EditorDiagnosticSpan>();

    public DiagnosticSquiggleRenderer(TextEditor editor)
    {
        _editor = editor;
        _errorPen = CreatePen(Color.FromRgb(220, 38, 38));
        _warningPen = CreatePen(Color.FromRgb(217, 119, 6));
        _infoPen = CreatePen(Color.FromRgb(37, 99, 235));
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public IReadOnlyList<EditorDiagnosticSpan> Spans => _spans;

    public void SetDiagnostics(IReadOnlyList<EditorDiagnosticSpan> spans)
    {
        _spans = spans ?? Array.Empty<EditorDiagnosticSpan>();
    }

    public EditorDiagnosticSpan? HitTest(int offset)
    {
        foreach (var span in _spans)
        {
            if (offset >= span.Offset && offset < span.Offset + span.Length)
            {
                return span;
            }
        }

        return null;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document == null || _spans.Count == 0)
        {
            return;
        }

        textView.EnsureVisualLines();
        var textLength = textView.Document.TextLength;

        foreach (var span in _spans)
        {
            if (span.Offset < 0 || span.Offset >= textLength || span.Length <= 0)
            {
                continue;
            }

            var safeLength = Math.Min(span.Length, textLength - span.Offset);
            if (safeLength <= 0)
            {
                continue;
            }

            var segment = new TextSegment
            {
                StartOffset = span.Offset,
                Length = safeLength
            };

            var rects = BackgroundGeometryBuilder.GetRectsForSegment(textView, segment);
            var pen = PenFor(span.Severity);
            foreach (var rect in rects)
            {
                DrawSquiggle(drawingContext, rect, pen);
            }
        }
    }

    private Pen PenFor(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Warning => _warningPen,
            DiagnosticSeverity.Info => _infoPen,
            _ => _errorPen
        };
    }

    private static Pen CreatePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.0);
        pen.Freeze();
        return pen;
    }

    private static void DrawSquiggle(DrawingContext drawingContext, Rect rect, Pen pen)
    {
        if (rect.Width < 1)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var y = rect.Bottom - 1.5;
            var x = rect.X;
            ctx.BeginFigure(new Point(x, y), false, false);
            var up = true;
            while (x < rect.Right)
            {
                x += 2;
                ctx.LineTo(new Point(Math.Min(x, rect.Right), up ? y - 2 : y + 1), true, false);
                up = !up;
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}
