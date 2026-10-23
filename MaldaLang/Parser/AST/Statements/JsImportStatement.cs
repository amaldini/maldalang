// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using System.Collections.Generic;

/// <summary>What an <c>import js</c> statement binds.</summary>
public enum JsImportKind
{
    /// <summary><c>import js THREE = "three";</c>: the module namespace.</summary>
    Namespace,
    /// <summary><c>import js { Vector3, Mesh } from "three";</c>: named exports.</summary>
    Named,
    /// <summary><c>import js global document, window;</c>: properties of <c>globalThis</c>.</summary>
    Global
}

/// <summary>
/// <c>import js …</c>: binds JavaScript modules or globals on the JavaScript backend. The
/// interpreter and C# transpile reject it. <c>js</c> and <c>global</c> are contextual words,
/// not reserved.
/// </summary>
public class JsImportStatement : Statement
{
    /// <summary>Why the interpreter and C# transpile reject the statement (one wording for both).</summary>
    public const string HostBackendMessage =
        "'import js' binds JavaScript modules and runs only on the JavaScript backend: compile with " +
        "'malda compile <file> --mode js'. The interpreter and C# transpile do not run JavaScript.";

    public JsImportKind Kind { get; }

    /// <summary>The module specifier (npm name, relative path or URL); null for <see cref="JsImportKind.Global"/>.</summary>
    public string? Specifier { get; }

    /// <summary>The MALDA names this statement binds, in source order.</summary>
    public IReadOnlyList<string> Names { get; }

    public JsImportStatement(JsImportKind kind, string? specifier, IReadOnlyList<string> names, int line = 0, int column = 0)
        : base(line, column)
    {
        Kind = kind;
        Specifier = specifier;
        Names = names;
    }
}
