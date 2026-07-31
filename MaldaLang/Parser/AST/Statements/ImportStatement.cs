// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

public class ImportStatement : Statement
{
    public string? FilePath { get; }
    public string? PackageName { get; }
    public string? SubModule { get; }
    public string? Alias { get; }

    public bool IsFileImport => !string.IsNullOrEmpty(FilePath);

    public ImportStatement(
        string? filePath,
        string? packageName,
        string? subModule,
        string? alias,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        FilePath = filePath;
        PackageName = packageName;
        SubModule = subModule;
        Alias = alias;
    }
}
