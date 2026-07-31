// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Models;

namespace MaldaLang.DesktopIDE.UserControls;

public partial class CodeDiffView : UserControl
{
    public DiffResult? DiffResult { get; set; }
    public string? SuggestedCode { get; set; }
    
    public event Action? OnApply;
    public event Action? OnDiscard;
    public event Action? OnCopy;
    public event Action? OnExpand;
    
    public CodeDiffView()
    {
        InitializeComponent();
    }
    
    public void SetDiffResult(DiffResult diffResult)
    {
        DiffResult = diffResult;
        UpdateDiffDisplay();
    }
    
    private void UpdateDiffDisplay()
    {
        if (DiffResult == null) return;
        
        DiffItemsControl.Items.Clear();
        
        foreach (var line in DiffResult.Lines)
        {
            var viewModel = new DiffLineViewModel(line);
            DiffItemsControl.Items.Add(viewModel);
        }
    }
    
    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        OnApply?.Invoke();
    }
    
    private void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        OnDiscard?.Invoke();
    }
    
    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SuggestedCode))
        {
            Clipboard.SetText(SuggestedCode);
        }
        OnCopy?.Invoke();
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        OnExpand?.Invoke();
    }
    
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        
        if (e.Property == DataContextProperty && DiffResult != null)
        {
            UpdateDiffDisplay();
        }
    }
}

// ViewModel for diff lines
public class DiffLineViewModel
{
    private readonly DiffLine _diffLine;
    
    public DiffLineViewModel(DiffLine diffLine)
    {
        _diffLine = diffLine;
    }
    
    public string LineNumberText
    {
        get
        {
            if (_diffLine.Type == DiffLineType.Added)
                return $"  +{_diffLine.NewLineNumber}";
            if (_diffLine.Type == DiffLineType.Removed)
                return $"{_diffLine.OriginalLineNumber}-";
            if (_diffLine.OriginalLineNumber == _diffLine.NewLineNumber)
                return $"  {_diffLine.OriginalLineNumber}";
            return $"{_diffLine.OriginalLineNumber}→{_diffLine.NewLineNumber}";
        }
    }
    
    public string? OriginalContent => _diffLine.OriginalContent ?? "";
    public string? NewContent => _diffLine.NewContent ?? "";
    
    public Brush LineBackground
    {
        get
        {
            return _diffLine.Type switch
            {
                DiffLineType.Added => new SolidColorBrush(Color.FromRgb(200, 255, 200)),
                DiffLineType.Removed => new SolidColorBrush(Color.FromRgb(255, 200, 200)),
                DiffLineType.Modified => new SolidColorBrush(Color.FromRgb(255, 255, 200)),
                _ => new SolidColorBrush(Colors.Transparent)
            };
        }
    }
    
    public Brush OriginalBackground
    {
        get
        {
            return _diffLine.Type switch
            {
                DiffLineType.Removed => new SolidColorBrush(Color.FromRgb(255, 220, 220)),
                DiffLineType.Modified => new SolidColorBrush(Color.FromRgb(255, 255, 220)),
                _ => new SolidColorBrush(Colors.White)
            };
        }
    }
    
    public Brush OriginalForeground => new SolidColorBrush(Colors.Black);
    
    public Brush NewBackground
    {
        get
        {
            return _diffLine.Type switch
            {
                DiffLineType.Added => new SolidColorBrush(Color.FromRgb(220, 255, 220)),
                DiffLineType.Modified => new SolidColorBrush(Color.FromRgb(255, 255, 220)),
                _ => new SolidColorBrush(Colors.White)
            };
        }
    }
    
    public Brush NewForeground => new SolidColorBrush(Colors.Black);
}