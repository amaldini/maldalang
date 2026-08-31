// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

internal enum TargetBackend
{
    CSharp,
    JavaScript
}

internal sealed class TargetPartitionDiagnostic
{
    public string Message { get; }
    public int Line { get; }
    public int Column { get; }

    public TargetPartitionDiagnostic(string message, int line, int column)
    {
        Message = message;
        Line = line;
        Column = column;
    }

    public override string ToString()
    {
        if (Line > 0)
        {
            return $"Line {Line}:{Math.Max(1, Column)} - {Message}";
        }

        return Message;
    }
}

internal sealed class TargetPartitionResult
{
    public List<Statement> CSharpStatements { get; } = new();
    public List<Statement> JavaScriptStatements { get; } = new();
    public List<TargetPartitionDiagnostic> Diagnostics { get; } = new();
}

internal static class TargetPartitioner
{
    private static readonly HashSet<string> CompileTimeTargetDecorators = new(StringComparer.OrdinalIgnoreCase)
    {
        "client",
        "javascript",
        "server",
        "csharp",
        "shared"
    };

    private static readonly HashSet<string> RouteDecorators = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "POST",
        "PUT",
        "DELETE",
        "PATCH",
        "OPTIONS",
        "PAGE",
        "AIPAGE",
        "ACTION",
        "COMPONENT",
        "LIVE"
    };

    private static readonly HashSet<string> SharedServerOnlyBuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        "readFile",
        "writeFile",
        "copyFile",
        "appendFile",
        "deleteFile",
        "fileExists",
        "createDirectory",
        "directoryExists",
        "listDirectory",
        "sqliteOpen",
        "sqliteExecute",
        "sqliteQuery",
        "SqliteClient",
        "HttpServer",
        "RestServer"
    };

    private static readonly HashSet<string> SharedClientOnlyBuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        "domHtml",
        "domText",
        "domValue",
        "domSetValue",
        "domAppend",
        "domPrepend",
        "domRemove",
        "domOn",
        "domClassAdd",
        "domClassRemove",
        "domStyleSet",
        "localStorageGet",
        "localStorageSet",
        "sessionStorageGet",
        "sessionStorageSet"
    };

    public static bool IsCompileTimeTargetDecorator(string decoratorName)
    {
        return CompileTimeTargetDecorators.Contains(decoratorName);
    }

    public static IReadOnlyList<TargetPartitionDiagnostic> Validate(List<Statement> statements)
    {
        var result = Partition(statements);
        return result.Diagnostics;
    }

    public static TargetPartitionResult Partition(List<Statement> statements)
    {
        var result = new TargetPartitionResult();

        foreach (var statement in statements)
        {
            var csharpStatement = PartitionStatement(statement, TargetBackend.CSharp, result.Diagnostics);
            if (csharpStatement != null)
            {
                result.CSharpStatements.Add(csharpStatement);
            }

            var jsStatement = PartitionStatement(statement, TargetBackend.JavaScript, result.Diagnostics);
            if (jsStatement != null)
            {
                result.JavaScriptStatements.Add(jsStatement);
            }
        }

        return result;
    }

    private static Statement? PartitionStatement(Statement statement, TargetBackend backend, List<TargetPartitionDiagnostic> diagnostics)
    {
        switch (statement)
        {
            case FunctionDeclaration function:
                return IncludeFunction(function, backend, diagnostics)
                    ? CloneFunctionWithRuntimeDecorators(function)
                    : null;
            case PropertyDeclaration property:
                return IncludeProperty(property, backend)
                    ? property
                    : null;
            case ClassDeclaration classDeclaration:
                return PartitionClass(classDeclaration, backend, diagnostics);
            default:
                return statement;
        }
    }

    private static Statement? PartitionClass(ClassDeclaration classDeclaration, TargetBackend backend, List<TargetPartitionDiagnostic> diagnostics)
    {
        var filteredMembers = new List<ClassMember>();
        foreach (var member in classDeclaration.Members)
        {
            if (member.Value is not FunctionDeclaration functionMember)
            {
                filteredMembers.Add(member);
                continue;
            }

            if (!IncludeFunction(functionMember, backend, diagnostics))
            {
                continue;
            }

            var clonedFunction = CloneFunctionWithRuntimeDecorators(functionMember);
            filteredMembers.Add(new ClassMember(member.Access, member.IsStatic, member.Type, member.Name, clonedFunction, member.TypeHint));
        }

        return new ClassDeclaration(classDeclaration.Name, classDeclaration.Superclass, filteredMembers, classDeclaration.IsExported, classDeclaration.Line, classDeclaration.Column);
    }

    private static bool IncludeProperty(PropertyDeclaration property, TargetBackend backend)
    {
        var propertyTargetModes = property.GetTargetModes();
        if (propertyTargetModes.Count == 0)
        {
            return backend == TargetBackend.CSharp;
        }

        var mode = backend == TargetBackend.CSharp ? "csharp" : "js";
        return propertyTargetModes.Any(target => string.Equals(target, mode, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IncludeFunction(FunctionDeclaration function, TargetBackend backend, List<TargetPartitionDiagnostic> diagnostics)
    {
        var hasShared = HasDecorator(function.Decorators, "shared");
        var hasServer = HasDecorator(function.Decorators, "server") || HasDecorator(function.Decorators, "csharp");
        var hasClient = HasDecorator(function.Decorators, "client") || HasDecorator(function.Decorators, "javascript");
        var hasRouteDecorator = function.Decorators.Any(decorator => RouteDecorators.Contains(decorator.Name));

        if (hasClient && hasRouteDecorator)
        {
            diagnostics.Add(new TargetPartitionDiagnostic(
                $"Function '{function.Name}' cannot combine client-only target decorators with route decorators.",
                function.Line,
                function.Column));
        }

        if (hasShared)
        {
            ValidateSharedFunction(function, diagnostics);
        }

        if (hasShared)
        {
            return true;
        }

        if (hasServer && !hasClient)
        {
            return backend == TargetBackend.CSharp;
        }

        if (hasClient && !hasServer)
        {
            return backend == TargetBackend.JavaScript;
        }

        if (hasRouteDecorator)
        {
            return backend == TargetBackend.CSharp;
        }

        return true;
    }

    private static void ValidateSharedFunction(FunctionDeclaration function, List<TargetPartitionDiagnostic> diagnostics)
    {
        var callNames = EnumerateCalledIdentifiers(function.Body);
        foreach (var callName in callNames)
        {
            if (SharedServerOnlyBuiltIns.Contains(callName))
            {
                diagnostics.Add(new TargetPartitionDiagnostic(
                    $"Function '{function.Name}' is marked @shared() but calls server-only built-in '{callName}'.",
                    function.Line,
                    function.Column));
            }
            else if (SharedClientOnlyBuiltIns.Contains(callName))
            {
                diagnostics.Add(new TargetPartitionDiagnostic(
                    $"Function '{function.Name}' is marked @shared() but calls browser-only built-in '{callName}'.",
                    function.Line,
                    function.Column));
            }
        }
    }

    private static IEnumerable<string> EnumerateCalledIdentifiers(object rootNode)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Traverse(rootNode, names, visited);
        return names;
    }

    private static void Traverse(object? node, HashSet<string> names, HashSet<object> visited)
    {
        if (node == null)
        {
            return;
        }

        if (node is string || node is ValueType)
        {
            return;
        }

        if (!visited.Add(node))
        {
            return;
        }

        if (node is FunctionCallExpression functionCall && functionCall.Callee is IdentifierExpression identifier)
        {
            names.Add(identifier.Name);
        }

        if (node is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                Traverse(item, names, visited);
            }

            return;
        }

        var type = node.GetType();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch
            {
                continue;
            }

            Traverse(value, names, visited);
        }
    }

    private static FunctionDeclaration CloneFunctionWithRuntimeDecorators(FunctionDeclaration function)
    {
        var filteredDecorators = function.Decorators
            .Where(decorator => !IsCompileTimeTargetDecorator(decorator.Name))
            .ToList();
        var parameterDecorators = function.ParameterDecorators?.ToList();
        var parameterTypeHints = function.ParameterTypeHints?.ToList();
        return new FunctionDeclaration(
            function.Name,
            function.Parameters.ToList(),
            function.Body,
            filteredDecorators,
            parameterDecorators,
            parameterTypeHints,
            function.ReturnType,
            function.IsExported,
            function.Line,
            function.Column);
    }

    private static bool HasDecorator(List<Decorator>? decorators, string name)
    {
        if (decorators == null || decorators.Count == 0)
        {
            return false;
        }

        return decorators.Any(decorator => string.Equals(decorator.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
