// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser;

using System;

public class ParseException : Exception
{
    public int Line { get; }
    public int Column { get; }
    public string? SourceFileName { get; }
    public string Details { get; }
    public TokenType? ExpectedType { get; }
    public TokenType? ActualType { get; }
    public bool IsSyntheticInsertion { get; }
    /// <summary>Optional diagnostic code (e.g., WF1003, WF1004 for workflow diagnostics).</summary>
    public string? DiagnosticCode { get; }
    
    public ParseException(string message, string? diagnosticCode = null) : base(message) 
    {
        Line = 0;
        Column = 0;
        SourceFileName = null;
        Details = message;
        ExpectedType = null;
        ActualType = null;
        IsSyntheticInsertion = false;
        DiagnosticCode = diagnosticCode;
    }
    
    public ParseException(int line, int column, string message, string? sourceFileName = null, string? diagnosticCode = null) : base(BuildMessage(line, column, message, sourceFileName))
    {
        Line = line;
        Column = column;
        SourceFileName = sourceFileName;
        Details = message;
        ExpectedType = null;
        ActualType = null;
        IsSyntheticInsertion = false;
        DiagnosticCode = diagnosticCode;
    }
    
    public ParseException(int line, int column, TokenType expectedType, TokenType actualType, bool isSyntheticInsertion = false, string? sourceFileName = null, string? diagnosticCode = null) 
        : base(BuildMessage(line, column,
            isSyntheticInsertion
                ? $"Missing '{expectedType}' - inserted automatically"
                : $"Expected '{expectedType}' but found '{actualType}'",
            sourceFileName))
    {
        Line = line;
        Column = column;
        SourceFileName = sourceFileName;
        Details = isSyntheticInsertion
            ? $"Missing '{expectedType}' - inserted automatically"
            : $"Expected '{expectedType}' but found '{actualType}'";
        ExpectedType = expectedType;
        ActualType = actualType;
        IsSyntheticInsertion = isSyntheticInsertion;
        DiagnosticCode = diagnosticCode;
    }

    private static string BuildMessage(int line, int column, string details, string? sourceFileName)
    {
        if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            return $"Parse error in {sourceFileName} at line {line}, column {column}: {details}";
        }

        return $"Parse error at line {line}, column {column}: {details}";
    }
}
