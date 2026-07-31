// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Models;

using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// Represents an active download item in the UI.
/// </summary>
public class ActiveDownloadItem : INotifyPropertyChanged
{
    private int _percentage;
    private string _progressText = string.Empty;

    public string ModelId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    public int Percentage
    {
        get => _percentage;
        set
        {
            if (_percentage != value)
            {
                _percentage = value;
                OnPropertyChanged();
            }
        }
    }

    public string ProgressText
    {
        get => _progressText;
        set
        {
            if (_progressText != value)
            {
                _progressText = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}