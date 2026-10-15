// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.PackageManager;
using System.Threading;

namespace MaldaLang.IDE.Services;

public partial class LanguageService : ILanguageService
{
    private readonly object _parseCacheLock = new();
    private string? _parseCacheSource;
    private string? _parseCacheFileName;
    private List<Statement>? _parseCacheStatements;

    public List<Diagnostic> GetDiagnostics(
        string source,
        string? sourceFileName = null,
        CancellationToken cancellationToken = default,
        StrictTypesOptions? strictTypesOptions = null)
    {
        var diagnostics = new List<Diagnostic>();
        var typeOptions = strictTypesOptions ?? StrictTypesOptions.Default;
        
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lexer = new Lexer(source, sourceFileName);
            var tokens = lexer.Tokenize();
            cancellationToken.ThrowIfCancellationRequested();
            var parser = new MaldaLang.Parser.Parser(tokens, sourceFileName);
            var statements = parser.Parse(); // This will collect errors in parser.Errors
            StoreParsedStatements(source, sourceFileName, statements);
            cancellationToken.ThrowIfCancellationRequested();
            StdLibNamespaceDiagnostics.Validate(statements, diagnostics);
            ImportDiagnostics.Validate(source, sourceFileName, diagnostics, cancellationToken);
            WorkflowDeterminismDiagnostics.Validate(statements, diagnostics, sourceFileName);
            AgentTeamDiagnostics.Validate(statements, diagnostics);
            GotchaDiagnostics.Validate(statements, diagnostics);
            ToolSchemaDiagnostics.Validate(statements, diagnostics);
            UiLoopDiagnostics.Validate(statements, diagnostics);
            InterpolationDiagnostics.Validate(statements, diagnostics);
            StrictTypesAnalysis.Analyze(statements, typeOptions, diagnostics, sourceFileName);
            
            // Report all parser errors
            foreach (var error in parser.Errors)
            {
                // Extract just the error message (after "Parse error at line X, column Y: ")
                var message = error.Message;
                var colonIndex = message.LastIndexOf(": ");
                if (colonIndex >= 0 && colonIndex < message.Length - 2)
                {
                    message = message.Substring(colonIndex + 2);
                }
                
                var diagnostic = new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = message,
                    Line = error.Line - 1, // 0-based
                    Column = error.Column - 1,
                    Length = 1,
                    Source = !string.IsNullOrEmpty(error.DiagnosticCode) ? error.DiagnosticCode : "parser"
                };
                
                // Try to get autofix suggestion (pass the ParseException for better detection)
                diagnostic.AutoFix = GetAutoFix(source, diagnostic, error, cancellationToken);
                SnapDiagnosticToSemicolonAutofix(diagnostic);
                ApplyLearningSupport(diagnostic);
                
                diagnostics.Add(diagnostic);
            }
            
            // Validate decorators
            ValidateDecorators(statements, diagnostics, cancellationToken);
        }
        catch (MaldaLang.Parser.ParseException ex)
        {
            // Fallback for any errors that weren't collected
            var message = ex.Message;
            var colonIndex = message.LastIndexOf(": ");
            if (colonIndex >= 0 && colonIndex < message.Length - 2)
            {
                message = message.Substring(colonIndex + 2);
            }
            
            var diagnostic = new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = message,
                Line = ex.Line > 0 ? ex.Line - 1 : 0, // 0-based
                Column = ex.Column > 0 ? ex.Column - 1 : 0,
                Length = 1,
                Source = !string.IsNullOrEmpty(ex.DiagnosticCode) ? ex.DiagnosticCode : "parser"
            };
            
            // Try to get autofix suggestion (pass the ParseException for better detection)
            diagnostic.AutoFix = GetAutoFix(source, diagnostic, ex, cancellationToken);
            SnapDiagnosticToSemicolonAutofix(diagnostic);
            ApplyLearningSupport(diagnostic);
            
            diagnostics.Add(diagnostic);
        }
        catch (OperationCanceledException)
        {
            return diagnostics;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = ex.Message,
                Line = 0,
                Column = 0,
                Length = 1,
                Source = "lexer",
                LearningHint = "The source could not be tokenized cleanly yet. Start with a small runnable example and reintroduce changes one step at a time.",
                SuggestedFix = "Compare your code with a simple beginner example such as Hello World or Variables and Arithmetic.",
                RelatedExamplePath = "Basics/hello_world.malda",
                RelatedExampleTitle = "Hello World",
                RelatedDocumentationPath = "/?lesson=hello-world#lesson-hello-world",
                RelatedDocumentationTitle = "Hello World"
            });
        }
        
        return diagnostics;
    }
    
    private List<Statement> ParseStatements(
        string source,
        string? sourceFileName,
        CancellationToken cancellationToken)
    {
        lock (_parseCacheLock)
        {
            if (_parseCacheStatements != null &&
                _parseCacheSource == source &&
                string.Equals(_parseCacheFileName, sourceFileName, StringComparison.Ordinal))
            {
                return _parseCacheStatements;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var lexer = new Lexer(source, sourceFileName);
        var tokens = lexer.Tokenize();
        cancellationToken.ThrowIfCancellationRequested();
        var parser = new MaldaLang.Parser.Parser(tokens, sourceFileName);
        var statements = parser.Parse();
        StoreParsedStatements(source, sourceFileName, statements);
        return statements;
    }

    private void StoreParsedStatements(string source, string? sourceFileName, List<Statement> statements)
    {
        lock (_parseCacheLock)
        {
            _parseCacheSource = source;
            _parseCacheFileName = sourceFileName;
            _parseCacheStatements = statements;
        }
    }
}