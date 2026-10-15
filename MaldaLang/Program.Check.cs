// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Interpreter;
using MaldaLang.PackageManager;
using System;
using System.Text;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using MaldaLang.Testing;
using MaldaLang.Scaffolding;
using MaldaLang.Deployment;
using MaldaLang.UIHost;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Cli;
using MaldaLang.Eval;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.BuiltIns;
using MaldaLang.DebugAdapter;
using SystemEnvironment = System.Environment;
using PackageManager = MaldaLang.PackageManager.PackageManager;

namespace MaldaLang;

partial class Program
{
    static void ValidateFromCommandLine(string code)
    {
        try
        {
            var compilerAssemblyPath = ResolveCompilerAssemblyPath();
            
            if (!File.Exists(compilerAssemblyPath))
            {
                Console.Error.WriteLine("Error: Compiler not found. Please build MaldaLang.Compiler project first.");
                SystemEnvironment.Exit(1);
                return;
            }
            
            var assembly = Assembly.LoadFrom(compilerAssemblyPath);
            var compilerType = assembly.GetType("MaldaLang.Compiler.Compiler");
            if (compilerType == null)
            {
                Console.Error.WriteLine("Error: Could not find Compiler class in MaldaLang.Compiler assembly.");
                SystemEnvironment.Exit(1);
                return;
            }
            
            var compiler = Activator.CreateInstance(compilerType);
            var validateMethod = compilerType.GetMethod("Validate", new[] { typeof(string) });
            if (validateMethod == null)
            {
                Console.Error.WriteLine("Error: Could not find Validate method in Compiler class.");
                SystemEnvironment.Exit(1);
                return;
            }
            
            var result = validateMethod.Invoke(compiler, new object[] { code });
            var resultType = result?.GetType();
            var successProperty = resultType?.GetProperty("Success");
            var errorMessageProperty = resultType?.GetProperty("ErrorMessage");
            var errorsProperty = resultType?.GetProperty("Errors");
            
            var success = (bool)(successProperty?.GetValue(result) ?? false);
            var errorMessage = errorMessageProperty?.GetValue(result) as string;
            var errors = errorsProperty?.GetValue(result) as System.Collections.IList;
            
            if (success)
            {
                Console.WriteLine("Validation successful: No syntax errors found.");
            }
            else
            {
                if (errors != null && errors.Count > 0)
                {
                    foreach (var error in errors)
                    {
                        Console.Error.WriteLine($"Error: {error}");
                    }
                }
                else if (!string.IsNullOrEmpty(errorMessage))
                {
                    Console.Error.WriteLine($"Error: {errorMessage}");
                }
                else
                {
                    Console.Error.WriteLine("Validation failed: Syntax errors found.");
                }
                SystemEnvironment.Exit(1);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during validation: {ex.Message}");
            SystemEnvironment.Exit(1);
        }
    }
    
    static void PrintSymbolsFromFile(string filePath)
    {
        try
        {
            // Resolve file path
            string fullPath;
            if (Path.IsPathRooted(filePath))
            {
                fullPath = Path.GetFullPath(filePath);
            }
            else
            {
                fullPath = Path.GetFullPath(Path.Combine(SystemEnvironment.CurrentDirectory, filePath));
            }
            
            if (!File.Exists(fullPath))
            {
                Console.Error.WriteLine($"Error: File not found: {fullPath}");
                SystemEnvironment.Exit(1);
                return;
            }
            
            // Read file
            var source = File.ReadAllText(fullPath);
            
            // Parse the source code
            var classes = new List<ClassDeclaration>();
            var functions = new List<FunctionDeclaration>();
            var actors = new List<ActorDeclaration>();
            var parseErrors = new List<Parser.ParseException>();
            
            try
            {
                var lexer = new Lexer(source, fullPath);
                var tokens = lexer.Tokenize();
                var parser = new MaldaLang.Parser.Parser(tokens, fullPath);
                var statements = parser.Parse();
                
                // Collect parse errors
                parseErrors.AddRange(parser.Errors);
                
                // Extract symbols from AST
                foreach (var stmt in statements)
                {
                    if (stmt is ClassDeclaration classDecl)
                    {
                        classes.Add(classDecl);
                    }
                    else if (stmt is FunctionDeclaration funcDecl)
                    {
                        functions.Add(funcDecl);
                    }
                    else if (stmt is ActorDeclaration actorDecl)
                    {
                        actors.Add(actorDecl);
                    }
                }
            }
            catch (Parser.ParseException ex)
            {
                parseErrors.Add(ex);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error parsing file: {ex.Message}");
                SystemEnvironment.Exit(1);
                return;
            }
            
            // Print results
            Console.WriteLine($"Symbols in {Path.GetFileName(fullPath)}:");
            Console.WriteLine();
            
            // Print parse errors if any
            if (parseErrors.Count > 0)
            {
                Console.WriteLine("⚠️  Parse Errors:");
                foreach (var error in parseErrors)
                {
                    Console.WriteLine($"  Line {error.Line}, Column {error.Column}: {error.Message}");
                }
                Console.WriteLine();
            }
            
            // Print classes
            if (classes.Count > 0)
            {
                Console.WriteLine($"📦 Classes ({classes.Count}):");
                foreach (var classDecl in classes)
                {
                    var superclassStr = classDecl.Superclass != null ? $" : {classDecl.Superclass}" : "";
                    Console.WriteLine($"  class {classDecl.Name}{superclassStr} (line {classDecl.Line})");
                    
                    if (classDecl.Members.Count > 0)
                    {
                        foreach (var member in classDecl.Members)
                        {
                            var staticStr = member.IsStatic ? "static " : "";
                            var accessStr = member.Access != AccessModifier.Public ? $"{member.Access.ToString().ToLower()} " : "";
                            
                            if (member.Type == MemberType.Method || member.Type == MemberType.Constructor)
                            {
                                if (member.Value is FunctionDeclaration memberFuncDecl)
                                {
                                    var paramsStr = string.Join(", ", memberFuncDecl.Parameters);
                                    var methodType = member.Type == MemberType.Constructor ? "constructor" : "method";
                                    Console.WriteLine($"    {accessStr}{staticStr}{methodType} {member.Name}({paramsStr}) (line {memberFuncDecl.Line})");
                                }
                                else
                                {
                                    Console.WriteLine($"    {accessStr}{staticStr}{member.Type.ToString().ToLower()} {member.Name}");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"    {accessStr}{staticStr}{member.Type.ToString().ToLower()} {member.Name}");
                            }
                        }
                    }
                }
                Console.WriteLine();
            }
            
            // Print actors
            if (actors.Count > 0)
            {
                Console.WriteLine($"🎭 Actors ({actors.Count}):");
                foreach (var actorDecl in actors)
                {
                    Console.WriteLine($"  actor {actorDecl.Name} (line {actorDecl.Line})");
                    
                    if (actorDecl.Members.Count > 0)
                    {
                        foreach (var member in actorDecl.Members)
                        {
                            var staticStr = member.IsStatic ? "static " : "";
                            var accessStr = member.Access != AccessModifier.Public ? $"{member.Access.ToString().ToLower()} " : "";
                            
                            if (member.Type == MemberType.Method || member.Type == MemberType.Constructor)
                            {
                                if (member.Value is FunctionDeclaration memberFuncDecl)
                                {
                                    var paramsStr = string.Join(", ", memberFuncDecl.Parameters);
                                    var methodType = member.Type == MemberType.Constructor ? "constructor" : "method";
                                    Console.WriteLine($"    {accessStr}{staticStr}{methodType} {member.Name}({paramsStr}) (line {memberFuncDecl.Line})");
                                }
                                else
                                {
                                    Console.WriteLine($"    {accessStr}{staticStr}{member.Type.ToString().ToLower()} {member.Name}");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"    {accessStr}{staticStr}{member.Type.ToString().ToLower()} {member.Name}");
                            }
                        }
                    }
                }
                Console.WriteLine();
            }
            
            // Print functions
            if (functions.Count > 0)
            {
                Console.WriteLine($"🔧 Functions ({functions.Count}):");
                foreach (var funcDecl in functions)
                {
                    var paramsStr = string.Join(", ", funcDecl.Parameters);
                    Console.WriteLine($"  function {funcDecl.Name}({paramsStr}) (line {funcDecl.Line})");
                }
                Console.WriteLine();
            }
            
            // Summary
            if (classes.Count == 0 && actors.Count == 0 && functions.Count == 0 && parseErrors.Count == 0)
            {
                Console.WriteLine("No symbols found in file.");
            }
            else
            {
                var totalSymbols = classes.Count + actors.Count + functions.Count;
                Console.WriteLine($"Total: {totalSymbols} symbol(s) found.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            SystemEnvironment.Exit(1);
        }
    }
}
