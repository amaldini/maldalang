// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Converts between MALDA 0-based (Line, Column) and LSP Position/Range.
/// LanguageService already uses 0-based line/column.
/// </summary>
public static class LspPositionHelper
{
    public static Position ToPosition(int line, int column)
    {
        return new Position(line, column);
    }

    /// <summary>
    /// Build LSP Range from 0-based line, column and length (single line).
    /// </summary>
    public static Range ToRange(int line, int column, int length)
    {
        return new Range(
            new Position(line, column),
            new Position(line, column + length)
        );
    }

    /// <summary>
    /// Build LSP Range for a name span: start (line, column) to (line, column + name.Length).
    /// </summary>
    public static Range ToNameRange(int line, int column, string name)
    {
        return ToRange(line, column, name?.Length ?? 0);
    }
}
