// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Editing;

namespace MaldaLang.DesktopIDE;

public class BreakpointMargin : AbstractMargin
{
    private readonly TextArea _textArea;
    private readonly MainWindow _mainWindow;
    
    public BreakpointMargin(TextArea textArea, MainWindow mainWindow)
    {
        _textArea = textArea;
        _mainWindow = mainWindow;
        
        // Subscribe to text view updates to redraw when needed
        _textArea.TextView.VisualLinesChanged += (s, e) => InvalidateVisual();
    }
    
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(18, 0);
    }
    
    protected override void OnRender(DrawingContext drawingContext)
    {
        var textView = _textArea.TextView;
        var renderSize = RenderSize;
        
        if (textView.VisualLines.Count == 0)
            return;
        
        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (_mainWindow.IsBreakpointLine(lineNumber))
            {
                var lineTop = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextTop) - textView.VerticalOffset;
                var lineBottom = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextBottom) - textView.VerticalOffset;
                
                var centerX = renderSize.Width / 2;
                var centerY = (lineTop + lineBottom) / 2;
                var radius = 5;
                
                // Draw breakpoint circle (red)
                var brush = new SolidColorBrush(Color.FromRgb(220, 50, 47));
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(180, 30, 27)), 1);
                
                drawingContext.DrawEllipse(brush, pen, new Point(centerX, centerY), radius, radius);
            }
        }
    }
    
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        
        if (e.ChangedButton == MouseButton.Left)
        {
            var textView = _textArea.TextView;
            var pos = e.GetPosition(this);
            
            // Find which line was clicked
            foreach (var visualLine in textView.VisualLines)
            {
                var lineTop = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextTop) - textView.VerticalOffset;
                var lineBottom = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextBottom) - textView.VerticalOffset;
                
                if (pos.Y >= lineTop && pos.Y <= lineBottom)
                {
                    var lineNumber = visualLine.FirstDocumentLine.LineNumber;
                    _mainWindow.ToggleBreakpointAtLine(lineNumber); // AvalonEdit line is 1-based; service stores 1-based
                    e.Handled = true;
                    break;
                }
            }
        }
    }
}