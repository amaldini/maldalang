// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System.IO;
using System.Reflection;
using System.Text;
using MaldaLang.BuiltIns;

public sealed class RuntimeDiagnosticInfo
{
    public string Message { get; init; } = string.Empty;
    public string ExceptionType { get; init; } = string.Empty;
    public int? Line { get; init; }
    public string? File { get; init; }
    public string? SourceLine { get; init; }
}

public static class RuntimeDiagnostics
{
    public static Exception Unwrap(Exception ex)
    {
        if (ex is AggregateException aggregateException)
        {
            var flattened = aggregateException.Flatten();
            if (flattened.InnerExceptions.Count > 0)
            {
                return Unwrap(flattened.InnerExceptions[0]);
            }
        }

        if (ex is TargetInvocationException && ex.InnerException != null)
        {
            return Unwrap(ex.InnerException);
        }

        return ex;
    }

    public static Exception PreserveContext(Exception ex, Interpreter? interpreter = null)
    {
        var normalized = Unwrap(ex);

        // WebRuntimeException subclasses RuntimeException (catchable in Malda) but
        // must keep its identity for HTTP status mapping — never rewrite it.
        if (normalized is WebRuntimeException webRuntimeException)
        {
            return webRuntimeException;
        }

        if (normalized is RuntimeException runtimeException)
        {
            return EnsureSourceLine(runtimeException, interpreter);
        }

        if (normalized is MALDAException maldaException)
        {
            var sourceLine = ResolveSourceLine(maldaException.Line, maldaException.File, interpreter);
            return new RuntimeException(
                maldaException.Message,
                maldaException.Line,
                maldaException.File,
                sourceLine,
                maldaException);
        }

        return new RuntimeException(
            normalized.Message,
            null,
            interpreter?.GetCurrentFile(),
            null,
            normalized);
    }

    public static RuntimeException EnsureSourceLine(RuntimeException runtimeException, Interpreter? interpreter = null)
    {
        if (!runtimeException.Line.HasValue)
        {
            return runtimeException;
        }

        var sourceLine = runtimeException.SourceLine;
        if (string.IsNullOrWhiteSpace(sourceLine))
        {
            sourceLine = ResolveSourceLine(runtimeException.Line, runtimeException.File, interpreter);
        }

        if (sourceLine == runtimeException.SourceLine)
        {
            return runtimeException;
        }

        return new RuntimeException(
            runtimeException.Message,
            runtimeException.Line,
            runtimeException.File,
            sourceLine,
            runtimeException.InnerException);
    }

    public static RuntimeDiagnosticInfo CreateDiagnosticInfo(Exception ex, Interpreter? interpreter = null)
    {
        var normalized = PreserveContext(ex, interpreter);
        if (normalized is RuntimeException runtimeException)
        {
            return new RuntimeDiagnosticInfo
            {
                Message = runtimeException.Message,
                ExceptionType = runtimeException.GetType().Name,
                Line = runtimeException.Line,
                File = runtimeException.File,
                SourceLine = runtimeException.SourceLine
            };
        }

        return new RuntimeDiagnosticInfo
        {
            Message = normalized.Message,
            ExceptionType = normalized.GetType().Name
        };
    }

    public static string FormatForConsole(Exception ex, Interpreter? interpreter = null)
    {
        var info = CreateDiagnosticInfo(ex, interpreter);
        var builder = new StringBuilder();
        builder.Append("Error: ").AppendLine(info.Message);

        if (!string.IsNullOrWhiteSpace(info.File) || info.Line.HasValue)
        {
            builder.Append("Location: ");
            if (!string.IsNullOrWhiteSpace(info.File))
            {
                builder.Append(info.File);
            }
            else
            {
                builder.Append("<unknown>");
            }

            if (info.Line.HasValue)
            {
                builder.Append(':').Append(info.Line.Value);
            }

            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(info.SourceLine))
        {
            builder.Append("Source: ").AppendLine(info.SourceLine.TrimEnd());
        }

        return builder.ToString().TrimEnd();
    }

    private static string? ResolveSourceLine(int? line, string? file, Interpreter? interpreter)
    {
        if (!line.HasValue || line.Value < 1)
        {
            return null;
        }

        if (interpreter != null)
        {
            var fromInterpreter = interpreter.GetSourceLine(line.Value);
            if (!string.IsNullOrWhiteSpace(fromInterpreter))
            {
                return fromInterpreter;
            }
        }

        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            return null;
        }

        try
        {
            var lines = File.ReadAllLines(file);
            if (line.Value <= lines.Length)
            {
                return lines[line.Value - 1];
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
