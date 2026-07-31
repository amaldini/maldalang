// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

public enum PromptBodyType
{
    ObjectLiteral,
    Statements
}

public class PromptDeclaration : Statement
{
    public string Name { get; }
    public List<string> Parameters { get; }
    public string? ReturnType { get; }
    public List<Decorator> Decorators { get; }
    public PromptBodyType BodyType { get; }
    public ObjectLiteralExpression? ObjectBody { get; }
    public List<Statement>? StatementBody { get; }
    
    // Constructor for object literal body (backward compatible)
    public PromptDeclaration(
        string name,
        List<string> parameters,
        ObjectLiteralExpression body,
        string? returnType = null,
        List<Decorator>? decorators = null,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        Name = name;
        Parameters = parameters;
        BodyType = PromptBodyType.ObjectLiteral;
        ObjectBody = body;
        StatementBody = null;
        ReturnType = returnType;
        Decorators = decorators ?? new List<Decorator>();
    }
    
    // Constructor for statement-based body
    public PromptDeclaration(
        string name,
        List<string> parameters,
        List<Statement> statementBody,
        string? returnType = null,
        List<Decorator>? decorators = null,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        Name = name;
        Parameters = parameters;
        BodyType = PromptBodyType.Statements;
        ObjectBody = null;
        StatementBody = statementBody;
        ReturnType = returnType;
        Decorators = decorators ?? new List<Decorator>();
    }
    
    // Legacy property for backward compatibility
    public ObjectLiteralExpression Body => ObjectBody ?? throw new System.Exception("Prompt body is statement-based, not object literal");
}
