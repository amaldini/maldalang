// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Models;

/// <summary>
/// Represents an autofix suggestion for a parser error
/// </summary>
public class AutoFixInfo
{
    /// <summary>
    /// Description of autofix action
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Line where fix should be applied (0-based)
    /// </summary>
    public int Line { get; set; }
    
    /// <summary>
    /// Column where fix should be applied (0-based)
    /// </summary>
    public int Column { get; set; }
    
    /// <summary>
    /// Text to insert
    /// </summary>
    public string TextToInsert { get; set; } = string.Empty;
    
    /// <summary>
    /// Length of text to replace (0 means insert only)
    /// </summary>
    public int LengthToReplace { get; set; }
    
    /// <summary>
    /// Whether this is a simple character insert (semicolon, brace, etc.)
    /// </summary>
    public bool IsSimpleCharacterFix { get; set; }
}
