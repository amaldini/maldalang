// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System;

public class RuntimeException : Exception
{
    public int? Line { get; }
    public string? File { get; }
    public string? SourceLine { get; }
    
    public RuntimeException(string message) : base(message) 
    {
        Line = null;
        File = null;
        SourceLine = null;
    }
    
    public RuntimeException(string message, int? line, string? file) : base(message) 
    {
        Line = line;
        File = file;
        SourceLine = null;
    }
    
    public RuntimeException(string message, int? line, string? file, string? sourceLine) : base(message) 
    {
        Line = line;
        File = file;
        SourceLine = sourceLine;
    }

    public RuntimeException(string message, int? line, string? file, string? sourceLine, Exception? innerException)
        : base(message, innerException)
    {
        Line = line;
        File = file;
        SourceLine = sourceLine;
    }
}

public class ReturnException : Exception
{
    public RuntimeValue? Value { get; }
    public ReturnException(RuntimeValue? value) { Value = value; }
}

public class BreakException : Exception { }
public class ContinueException : Exception { }

public class MALDAException : Exception
{
    public RuntimeValue Value { get; }
    public int? Line { get; }
    public string? File { get; }
    
    public MALDAException(RuntimeValue value, int? line = null, string? file = null) 
        : base(value.ToString())
    {
        Value = value;
        Line = line;
        File = file;
    }
}

// Call stack frame for interpreter (separate from IDE model)
public class InterpreterCallStackFrame
{
    public string FunctionName { get; set; } = string.Empty;
    public string? ClassName { get; set; }
    public int Line { get; set; }
    public string File { get; set; } = string.Empty;
    public Environment? Environment { get; set; }
    public ObjectInstance? ThisObject { get; set; }
}
