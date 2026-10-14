// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Parser;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.IDE;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;

using MaldaLang.Compiler.OptionalPack;

namespace MaldaLang.Compiler;

public partial class CSharpTranspiler
{
    private void GenerateMaldaLangApi(List<Statement> statements, List<FunctionDeclaration> functions, List<ClassDeclaration> classes, List<ActorDeclaration> actors, List<PropertyDeclaration> properties)
    {
        _output.AppendLine();
        _output.AppendLine("/// <summary>");
        _output.AppendLine("/// Public API class for accessing transpiled MALDA code from other .NET programs.");
        _output.AppendLine("/// </summary>");
        _output.AppendLine("public static class MaldaLangApi");
        _output.AppendLine("{");
        _indentLevel++;
        
        // Initialize method
        WriteIndent();
        _output.AppendLine("/// <summary>");
        WriteIndent();
        _output.AppendLine("/// Initializes the library. Call this before using any functions or classes.");
        WriteIndent();
        _output.AppendLine("/// </summary>");
        WriteIndent();
        _output.AppendLine("public static async Task Initialize()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("await Program.Initialize();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        // ShutdownAsync method
        WriteIndent();
        _output.AppendLine("/// <summary>");
        WriteIndent();
        _output.AppendLine("/// Shuts down the library and cleans up resources (e.g., actors).");
        WriteIndent();
        _output.AppendLine("/// </summary>");
        WriteIndent();
        _output.AppendLine("public static async Task ShutdownAsync()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("await ActorsRuntime.ShutdownAsync();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        // Access to Program class for functions
        WriteIndent();
        _output.AppendLine("/// <summary>");
        WriteIndent();
        _output.AppendLine("/// Provides access to transpiled functions and classes.");
        WriteIndent();
        _output.AppendLine("/// </summary>");
        WriteIndent();
        _output.AppendLine("public static class ProgramAccess");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("/// <summary>");
        WriteIndent();
        _output.AppendLine("/// Gets the Program class containing all transpiled functions.");
        WriteIndent();
        _output.AppendLine("/// </summary>");
        WriteIndent();
        _output.AppendLine("public static Type ProgramType => typeof(Program);");
        if (properties.Count > 0)
        {
            _output.AppendLine();
            WriteIndent();
            _output.AppendLine("/// <summary>");
            WriteIndent();
            _output.AppendLine("/// Gets metadata for transpiled properties.");
            WriteIndent();
            _output.AppendLine("/// </summary>");
            WriteIndent();
            _output.AppendLine("public static System.Collections.Generic.IReadOnlyList<Program.TranspiledPropertyMetadata> GetProperties() => Program.GetTranspiledProperties();");
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void GenerateAttributeClasses(List<Statement> statements)
    {
        var decoratorNames = new HashSet<string>();
        CollectDecoratorNames(statements, decoratorNames);

        if (decoratorNames.Count == 0)
            return;

        foreach (var decoratorName in decoratorNames)
        {
            if (_generatedAttributeClasses.Contains(decoratorName))
                continue;

            _output.AppendLine($"public class {decoratorName}Attribute : System.Attribute");
            _output.AppendLine("{");
            _output.AppendLine("    public object[] Arguments { get; set; }");
            _output.AppendLine();
            _output.AppendLine($"    public {decoratorName}Attribute(params object[] args)");
            _output.AppendLine("    {");
            _output.AppendLine("        Arguments = args;");
            _output.AppendLine("    }");
            _output.AppendLine("}");
            _output.AppendLine();

            _generatedAttributeClasses.Add(decoratorName);
        }
    }

    private void CollectDecoratorNames(List<Statement> statements, HashSet<string> decoratorNames)
    {
        foreach (var statement in statements)
        {
            if (statement is FunctionDeclaration funcDecl)
            {
                if (funcDecl.Decorators != null)
                {
                    foreach (var decorator in funcDecl.Decorators)
                    {
                        if (!TargetPartitioner.IsCompileTimeTargetDecorator(decorator.Name))
                        {
                            decoratorNames.Add(decorator.Name);
                        }
                    }
                }
            }
            else if (statement is ClassDeclaration classDecl)
            {
                foreach (var member in classDecl.Members)
                {
                    if (member.Value is FunctionDeclaration memberFunc)
                    {
                        if (memberFunc.Decorators != null)
                        {
                            foreach (var decorator in memberFunc.Decorators)
                            {
                                if (!TargetPartitioner.IsCompileTimeTargetDecorator(decorator.Name))
                                {
                                    decoratorNames.Add(decorator.Name);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private bool HasDecorators(List<Statement> statements)
    {
        foreach (var statement in statements)
        {
            if (statement is FunctionDeclaration funcDecl && funcDecl.Decorators != null && funcDecl.Decorators.Any(d => !TargetPartitioner.IsCompileTimeTargetDecorator(d.Name)))
                return true;
            if (statement is ClassDeclaration classDecl)
            {
                foreach (var member in classDecl.Members)
                {
                    if (member.Value is FunctionDeclaration memberFunc &&
                        memberFunc.Decorators != null &&
                        memberFunc.Decorators.Any(d => !TargetPartitioner.IsCompileTimeTargetDecorator(d.Name)))
                        return true;
                }
            }
        }
        return false;
    }

    private void GenerateWindowsApiDeclarations()
    {
        // Generate Windows API declarations for enabling ANSI escape codes
        WriteIndent();
        _output.AppendLine("[System.Runtime.InteropServices.DllImport(\"kernel32.dll\", SetLastError = true)]");
        WriteIndent();
        _output.AppendLine("private static extern System.IntPtr GetStdHandle(int nStdHandle);");
        WriteIndent();
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("[System.Runtime.InteropServices.DllImport(\"kernel32.dll\", SetLastError = true)]");
        WriteIndent();
        _output.AppendLine("[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]");
        WriteIndent();
        _output.AppendLine("private static extern bool GetConsoleMode(System.IntPtr hConsoleHandle, out uint lpMode);");
        WriteIndent();
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("[System.Runtime.InteropServices.DllImport(\"kernel32.dll\", SetLastError = true)]");
        WriteIndent();
        _output.AppendLine("[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]");
        WriteIndent();
        _output.AppendLine("private static extern bool SetConsoleMode(System.IntPtr hConsoleHandle, uint dwMode);");
        WriteIndent();
        _output.AppendLine();
    }
}
