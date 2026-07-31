// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Services;

public class FileService
{
    private string? _currentFilePath;
    private string _currentContent = "";
    
    public string? CurrentFilePath => _currentFilePath;
    public string CurrentContent => _currentContent;
    
    public void SetContent(string content)
    {
        _currentContent = content;
    }
    
    public void SetFilePath(string? filePath)
    {
        _currentFilePath = filePath;
    }
    
    public bool HasUnsavedChanges { get; set; }
    
    public string GetFileName()
    {
        if (string.IsNullOrEmpty(_currentFilePath))
            return "Untitled.malda";
        return Path.GetFileName(_currentFilePath);
    }
}