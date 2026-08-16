// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MaldaLang.DesktopIDE;

/// <summary>
/// Renders highlights for search results in the active editor document.
/// </summary>
public sealed class SearchResultsBackgroundRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly Brush _matchBrush;
    private readonly Brush _activeMatchBrush;
    private readonly Pen _matchBorderPen;
    private readonly Pen _activeMatchBorderPen;
    private IReadOnlyList<SearchMatchSegment> _segments = new List<SearchMatchSegment>();
    private int? _activeOffset;

    public SearchResultsBackgroundRenderer(TextEditor editor)
        : this(
            editor,
            Color.FromArgb(72, 255, 225, 140),
            Color.FromArgb(120, 255, 180, 90),
            Color.FromArgb(110, 194, 132, 48),
            Color.FromArgb(180, 143, 84, 24))
    {
    }

    public SearchResultsBackgroundRenderer(
        TextEditor editor,
        Color match,
        Color activeMatch,
        Color matchBorder,
        Color activeMatchBorder)
    {
        _editor = editor;
        _matchBrush = new SolidColorBrush(match);
        _activeMatchBrush = new SolidColorBrush(activeMatch);
        _matchBorderPen = new Pen(new SolidColorBrush(matchBorder), 1.0);
        _activeMatchBorderPen = new Pen(new SolidColorBrush(activeMatchBorder), 1.0);
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetMatches(IReadOnlyList<SearchMatchSegment> segments, int? activeOffset)
    {
        _segments = segments;
        _activeOffset = activeOffset;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document == null || _segments.Count == 0)
        {
            return;
        }

        textView.EnsureVisualLines();
        var textLength = textView.Document.TextLength;

        foreach (var match in _segments)
        {
            if (match.Offset < 0 || match.Offset >= textLength || match.Length <= 0)
            {
                continue;
            }

            var safeLength = Math.Min(match.Length, textLength - match.Offset);
            if (safeLength <= 0)
            {
                continue;
            }

            var segment = new TextSegment
            {
                StartOffset = match.Offset,
                Length = safeLength
            };

            var rects = BackgroundGeometryBuilder.GetRectsForSegment(textView, segment);
            var isActive = _activeOffset.HasValue && _activeOffset.Value == match.Offset;
            var brush = isActive ? _activeMatchBrush : _matchBrush;
            var pen = isActive ? _activeMatchBorderPen : _matchBorderPen;

            foreach (var rect in rects)
            {
                var minWidth = Math.Max(1.0, rect.Width);
                drawingContext.DrawRectangle(brush, pen, new Rect(rect.X, rect.Y, minWidth, rect.Height));
            }
        }
    }
}

public sealed class SearchMatchSegment
{
    public int Offset { get; init; }
    public int Length { get; init; }
}
