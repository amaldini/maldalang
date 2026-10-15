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

public partial class LanguageService
{
    private static readonly Dictionary<string, DecoratorInfo> SupportedDecorators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PAGE"] = new DecoratorInfo
        {
            Name = "PAGE",
            Description = "Server-rendered HTML page decorator",
            Format = "@PAGE(\"/path\")",
            Documentation = "Marks a function as an HTTP GET page route for HttpServer. If omitted, path defaults to '/'.",
            MinArgs = 0,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string, optional): Page route path, e.g., \"/\" or \"/users/{id}\"" }
        },
        ["AIPAGE"] = new DecoratorInfo
        {
            Name = "AIPAGE",
            Description = "AI-generated HTML page decorator",
            Format = "@AIPAGE(\"/path\", \"description\")",
            Documentation = "Marks a function as an AI-generated page route. The first argument is the route path and the second is a natural-language description of the desired page.",
            MinArgs = 2,
            MaxArgs = 2,
            ArgDescriptions = new List<string>
            {
                "path (string): Page route path, e.g., \"/contact\"",
                "description (string): Prompt describing the page to generate"
            }
        },
        ["COMPONENT"] = new DecoratorInfo
        {
            Name = "COMPONENT",
            Description = "Component route decorator",
            Format = "@COMPONENT(\"/path\")",
            Documentation = "Marks a function as a component route. If path is omitted, a default component route is derived from the function name.",
            MinArgs = 0,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string, optional): Component route path" }
        },
        ["LIVE"] = new DecoratorInfo
        {
            Name = "LIVE",
            Description = "Live component endpoint decorator",
            Format = "@LIVE(\"/path\")",
            Documentation = "Marks a function as a live (SSE) endpoint for component updates. If path is omitted, a default live path is derived from the function name.",
            MinArgs = 0,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string, optional): Live endpoint path" }
        },
        ["ACTION"] = new DecoratorInfo
        {
            Name = "ACTION",
            Description = "Component action endpoint decorator",
            Format = "@ACTION(\"/path\")",
            Documentation = "Marks a function as an action endpoint. It is registered as an HTTP POST route for form and component actions.",
            MinArgs = 0,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string, optional): Action endpoint path" }
        },
        ["client"] = new DecoratorInfo
        {
            Name = "client",
            Description = "Compile-time client target decorator",
            Format = "@client()",
            Documentation = "Marks a function as JavaScript-only during transpilation. The function is excluded from C# output.",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["javascript"] = new DecoratorInfo
        {
            Name = "javascript",
            Description = "Alias of @client() compile-time target decorator",
            Format = "@javascript()",
            Documentation = "Marks a function as JavaScript-only during transpilation. Alias of @client().",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["server"] = new DecoratorInfo
        {
            Name = "server",
            Description = "Compile-time server target decorator",
            Format = "@server()",
            Documentation = "Marks a function as C# server-only during transpilation. The function is excluded from JavaScript output.",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["csharp"] = new DecoratorInfo
        {
            Name = "csharp",
            Description = "Alias of @server() compile-time target decorator",
            Format = "@csharp()",
            Documentation = "Marks a function as C# server-only during transpilation. Alias of @server().",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["shared"] = new DecoratorInfo
        {
            Name = "shared",
            Description = "Compile-time cross-target decorator",
            Format = "@shared()",
            Documentation = "Marks a function as shared between C# and JavaScript transpilation targets. Shared functions should avoid target-specific built-ins.",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["GET"] = new DecoratorInfo
        {
            Name = "GET",
            Description = "HTTP GET endpoint decorator",
            Format = "@GET(\"/path\")",
            Documentation = "Marks a function as a GET endpoint. The first argument must be a string path (e.g., \"/api/users\"). Supports path parameters with {paramName} syntax.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path, e.g., \"/api/users\" or \"/api/users/{id}\"" }
        },
        ["POST"] = new DecoratorInfo
        {
            Name = "POST",
            Description = "HTTP POST endpoint decorator",
            Format = "@POST(\"/path\")",
            Documentation = "Marks a function as a POST endpoint. The first argument must be a string path. Use a parameter named 'body' to receive the request body.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path, e.g., \"/api/users\"" }
        },
        ["PUT"] = new DecoratorInfo
        {
            Name = "PUT",
            Description = "HTTP PUT endpoint decorator",
            Format = "@PUT(\"/path\")",
            Documentation = "Marks a function as a PUT endpoint. The first argument must be a string path. Use a parameter named 'body' to receive the request body.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path, e.g., \"/api/users/{id}\"" }
        },
        ["DELETE"] = new DecoratorInfo
        {
            Name = "DELETE",
            Description = "HTTP DELETE endpoint decorator",
            Format = "@DELETE(\"/path\")",
            Documentation = "Marks a function as a DELETE endpoint. The first argument must be a string path.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path, e.g., \"/api/users/{id}\"" }
        },
        ["PATCH"] = new DecoratorInfo
        {
            Name = "PATCH",
            Description = "HTTP PATCH endpoint decorator",
            Format = "@PATCH(\"/path\")",
            Documentation = "Marks a function as a PATCH endpoint. The first argument must be a string path. Use a parameter named 'body' to receive the request body.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path, e.g., \"/api/users/{id}\"" }
        },
        ["OPTIONS"] = new DecoratorInfo
        {
            Name = "OPTIONS",
            Description = "HTTP OPTIONS endpoint decorator",
            Format = "@OPTIONS(\"/path\")",
            Documentation = "Marks a function as an OPTIONS endpoint. The first argument must be a string path.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path" }
        },
        ["Tool"] = new DecoratorInfo
        {
            Name = "Tool",
            Description = "LLM tool decorator",
            Format = "@Tool(\"name\", \"description\", schema?)",
            Documentation = "Registers a function as an LLM tool. Requires at least 2 arguments: tool name and description. Optional third argument is a MALDA schema/sum-type name (\"NoteArgs\" or NoteArgs), a JSON schema object string, or omitted (auto-generated string properties).",
            MinArgs = 2,
            MaxArgs = 3,
            ArgDescriptions = new List<string>
            {
                "name (string): Tool identifier",
                "description (string): Human-readable description",
                "schema (string/name, optional): Registered schema or sum-type name, or a JSON schema object string. If omitted, auto-generated from function parameters as strings."
            }
        },
        ["MCPTool"] = new DecoratorInfo
        {
            Name = "MCPTool",
            Description = "MCP tool decorator",
            Format = "@MCPTool(\"name\", \"description\", schema?)",
            Documentation = "Registers a function as an MCP (Model Context Protocol) tool. Requires at least 2 arguments: tool name and description. Optional third argument is a MALDA schema/sum-type name (\"WeatherArgs\" or WeatherArgs), a JSON schema object string, or omitted (auto-generated string properties).",
            MinArgs = 2,
            MaxArgs = 3,
            ArgDescriptions = new List<string>
            {
                "name (string): Tool identifier",
                "description (string): Human-readable description",
                "schema (string/name, optional): Registered schema or sum-type name, or a JSON schema object string. If omitted, auto-generated from function parameters as strings."
            }
        },
        ["PathParam"] = new DecoratorInfo
        {
            Name = "PathParam",
            Description = "Path parameter decorator",
            Format = "@PathParam(\"paramName\")",
            Documentation = "Marks a function parameter as a path parameter. Used in REST endpoints with decorator-based parameter binding. The argument is the path parameter name from the route.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "paramName (string): Name of the path parameter in the route (e.g., \"id\" for route \"/users/{id}\")" }
        },
        ["QueryParam"] = new DecoratorInfo
        {
            Name = "QueryParam",
            Description = "Query parameter decorator",
            Format = "@QueryParam(\"paramName\")",
            Documentation = "Marks a function parameter as a query parameter. Used in REST endpoints with decorator-based parameter binding.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "paramName (string): Query parameter name (e.g., \"limit\" for ?limit=10)" }
        },
        ["Body"] = new DecoratorInfo
        {
            Name = "Body",
            Description = "Request body decorator",
            Format = "@Body()",
            Documentation = "Marks a function parameter as the request body. Used in REST endpoints with decorator-based parameter binding. Takes no arguments.",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["shader"] = new DecoratorInfo
        {
            Name = "shader",
            Description = "GLSL shader kernel decorator (JavaScript backend)",
            Format = "@shader()",
            Documentation = "Marks a typed MALDA function as a GLSL kernel. JavaScript compile inlines it through glsl.compile({ ... }) and does not emit a JS function. Type hints use GLSL names (vec3, float, out float). Interpreter and C# backends skip these functions.",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["within"] = new DecoratorInfo
        {
            Name = "within",
            Description = "Wall-clock bound decorator",
            Format = "@within(ms)",
            Documentation = "Enforces a wall-clock timeout on a function body or await prompt(...) / agent.think() turn. Expects a single positive integer literal (milliseconds).",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "ms (positive int): Deadline in milliseconds" }
        },
        ["budget"] = new DecoratorInfo
        {
            Name = "budget",
            Description = "Resource budget decorator",
            Format = "@budget(tokens: N, tools: N, cost: N)",
            Documentation = "Aborts a function or prompt/agent turn when a declared resource bound trips. Named keys only: tokens (prompt+completion, or chars/4 best-effort), tools (invocation count, not allow-list length), optional cost when the backend reports it. Unknown keys are errors under --strict-types. MALDA_AGENT_CONTEXT_BUDGET_TOKENS remains a context-trim fallback for undeclared agents.",
            MinArgs = 1,
            MaxArgs = 3,
            ArgDescriptions = new List<string>
            {
                "tokens (positive int, optional): Max prompt+completion tokens for the turn",
                "tools (positive int, optional): Max tool invocations in the turn",
                "cost (positive number, optional): Max reported usage cost when the backend exposes it"
            }
        },
        ["pure"] = new DecoratorInfo
        {
            Name = "pure",
            Description = "Purity decorator",
            Format = "@pure()",
            Documentation = "Marks a helper that must not perform I/O. Checked under CLI --strict-types / full strict analysis (malda-pure). Cannot be combined with @effects. Prefer this on normalize/derive helpers before impure tool work.",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["effects"] = new DecoratorInfo
        {
            Name = "effects",
            Description = "Side-effect allow-list decorator",
            Format = "@effects(\"print\", …)",
            Documentation = "Declares a name allow-list of side effects on a handler (print, io, cap, await, …). Checked under CLI --strict-types / full strict analysis (malda-effects). Cannot be combined with @pure. @effects(\"io\") names the effect only — it does not bind a path, URL, tool, or argv. Use capability tokens when a tool must not invent a path.",
            MinArgs = 1,
            MaxArgs = 0,
            ArgDescriptions = new List<string> { "effect (string, repeatable): Allowed effect name, e.g. \"print\", \"io\", or \"cap\"" }
        }
    };
    
    /// <summary>
    /// Gets all supported decorators with their information.
    /// This is used to provide decorator documentation to the AI system.
    /// </summary>
    public static Dictionary<string, DecoratorInfo> GetSupportedDecorators()
    {
        return SupportedDecorators;
    }

    private string? GetDecoratorContext(string source, int line, int column)
    {
        try
        {
            var lines = source.Split('\n');
            if (line < 0 || line >= lines.Length)
                return null;
            
            var currentLine = lines[line];
            if (column < 0)
                return null;
            
            // Column is 0-based and points to the position after the last typed character
            // With full source text, the line should include all typed characters
            
            // Look backwards from cursor position to find @
            // Start from column - 1, but clamp to valid line bounds
            int searchStart = column > 0 ? Math.Min(column - 1, currentLine.Length - 1) : (currentLine.Length > 0 ? currentLine.Length - 1 : 0);
            if (searchStart < 0) return null;
            
            for (int i = searchStart; i >= 0; i--)
            {
                if (currentLine[i] == '@')
                {
                    // Found @, now check if cursor is within or right after the decorator name
                    int nameStart = i + 1;
                    
                    // If cursor is at or past nameStart, we're in decorator context
                    // This handles:
                    // - cursor right after @ (column == nameStart) -> return ""
                    // - cursor within the name (column > nameStart) -> return partial name
                    if (column >= nameStart)
                    {
                        // Extract partial name if any (from @ to cursor position)
                        if (nameStart < column && nameStart < currentLine.Length)
                        {
                            int extractLength = Math.Min(column - nameStart, currentLine.Length - nameStart);
                            if (extractLength > 0)
                            {
                                return currentLine.Substring(nameStart, extractLength);
                            }
                        }
                        return ""; // Just @, no name yet or cursor is right after @
                    }
                    break;
                }
                if (!char.IsWhiteSpace(currentLine[i]) && currentLine[i] != '@')
                {
                    // Hit a non-whitespace, non-@ character before finding @
                    break;
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return null;
    }

    private List<CompletionItem> GetDecoratorCompletions(string? partialName)
    {
        var completions = new List<CompletionItem>();
        
        foreach (var decorator in SupportedDecorators.Values)
        {
            // Filter by partial name if provided
            if (partialName != null && !decorator.Name.StartsWith(partialName, StringComparison.OrdinalIgnoreCase))
                continue;
            
            var insertText = decorator.Format;
            // If we have a partial name, replace it in the insert text
            if (!string.IsNullOrEmpty(partialName) && decorator.Name.StartsWith(partialName, StringComparison.OrdinalIgnoreCase))
            {
                insertText = decorator.Format.Replace(decorator.Name, partialName + decorator.Name.Substring(partialName.Length));
            }
            
            completions.Add(new CompletionItem
            {
                Label = decorator.Name,
                Kind = "decorator",
                Detail = decorator.Description,
                Documentation = decorator.Documentation,
                InsertText = insertText
            });
        }
        
        return completions;
    }
    
    private string? GetDecoratorHoverInfo(string source, int line, int column, Token token)
    {
        // Check if the token is a decorator name
        if (SupportedDecorators.TryGetValue(token.Lexeme, out var decoratorInfo))
        {
            // Verify we're actually in a decorator context (after @)
            var decoratorContext = GetDecoratorContext(source, line, column);
            if (decoratorContext != null)
            {
                var info = $"{decoratorInfo.Description}\n\nFormat: {decoratorInfo.Format}";
                if (!string.IsNullOrEmpty(decoratorInfo.Documentation))
                {
                    info += $"\n\n{decoratorInfo.Documentation}";
                }
                if (decoratorInfo.ArgDescriptions.Count > 0)
                {
                    info += "\n\nArguments:";
                    for (int i = 0; i < decoratorInfo.ArgDescriptions.Count; i++)
                    {
                        info += $"\n  {i + 1}. {decoratorInfo.ArgDescriptions[i]}";
                    }
                }
                if (decoratorInfo.MinArgs != decoratorInfo.MaxArgs)
                {
                    info += $"\n\nRequired arguments: {decoratorInfo.MinArgs}";
                    if (decoratorInfo.MaxArgs > decoratorInfo.MinArgs)
                    {
                        info += $", optional up to {decoratorInfo.MaxArgs}";
                    }
                }
                else if (decoratorInfo.MinArgs > 0)
                {
                    info += $"\n\nRequired arguments: {decoratorInfo.MinArgs}";
                }
                return info;
            }
        }
        return null;
    }
    
    private void ValidateDecorators(List<MaldaLang.Parser.AST.Statements.Statement> statements, List<Diagnostic> diagnostics, CancellationToken cancellationToken = default)
    {
        foreach (var stmt in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stmt is FunctionDeclaration funcDecl && funcDecl.Decorators != null)
            {
                foreach (var decorator in funcDecl.Decorators)
                {
                    // Check if decorator is supported
                    if (!SupportedDecorators.TryGetValue(decorator.Name, out var decoratorInfo))
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Warning,
                            Message = $"Unknown decorator '@{decorator.Name}'. Supported decorators: {string.Join(", ", SupportedDecorators.Keys.Select(k => "@" + k))}",
                            Line = decorator.Line - 1,
                            Column = decorator.Column - 1,
                            Length = decorator.Name.Length,
                            Source = "decorator"
                        });
                        continue;
                    }
                    
                    // Validate argument count
                    var argCount = decorator.Arguments?.Count ?? 0;
                    if (argCount < decoratorInfo.MinArgs)
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Message = $"@{decorator.Name} requires at least {decoratorInfo.MinArgs} argument(s), but {argCount} provided. Format: {decoratorInfo.Format}",
                            Line = decorator.Line - 1,
                            Column = decorator.Column - 1,
                            Length = decorator.Name.Length,
                            Source = "decorator"
                        });
                    }
                    else if (decoratorInfo.MaxArgs > 0 && argCount > decoratorInfo.MaxArgs)
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Message = $"@{decorator.Name} accepts at most {decoratorInfo.MaxArgs} argument(s), but {argCount} provided. Format: {decoratorInfo.Format}",
                            Line = decorator.Line - 1,
                            Column = decorator.Column - 1,
                            Length = decorator.Name.Length,
                            Source = "decorator"
                        });
                    }
                }
            }
            
            // Also check class methods for decorators
            if (stmt is ClassDeclaration classDecl)
            {
                foreach (var member in classDecl.Members)
                {
                    if (member.Type == MaldaLang.Parser.AST.Declarations.MemberType.Method && member.Value is FunctionDeclaration methodDecl && methodDecl.Decorators != null)
                    {
                        foreach (var decorator in methodDecl.Decorators)
                        {
                            if (!SupportedDecorators.TryGetValue(decorator.Name, out var decoratorInfo))
                            {
                                diagnostics.Add(new Diagnostic
                                {
                                    Severity = DiagnosticSeverity.Warning,
                                    Message = $"Unknown decorator '@{decorator.Name}'. Supported decorators: {string.Join(", ", SupportedDecorators.Keys.Select(k => "@" + k))}",
                                    Line = decorator.Line - 1,
                                    Column = decorator.Column - 1,
                                    Length = decorator.Name.Length,
                                    Source = "decorator"
                                });
                                continue;
                            }
                            
                            var argCount = decorator.Arguments?.Count ?? 0;
                            if (argCount < decoratorInfo.MinArgs)
                            {
                                diagnostics.Add(new Diagnostic
                                {
                                    Severity = DiagnosticSeverity.Error,
                                    Message = $"@{decorator.Name} requires at least {decoratorInfo.MinArgs} argument(s), but {argCount} provided. Format: {decoratorInfo.Format}",
                                    Line = decorator.Line - 1,
                                    Column = decorator.Column - 1,
                                    Length = decorator.Name.Length,
                                    Source = "decorator"
                                });
                            }
                            else if (decoratorInfo.MaxArgs > 0 && argCount > decoratorInfo.MaxArgs)
                            {
                                diagnostics.Add(new Diagnostic
                                {
                                    Severity = DiagnosticSeverity.Error,
                                    Message = $"@{decorator.Name} accepts at most {decoratorInfo.MaxArgs} argument(s), but {argCount} provided. Format: {decoratorInfo.Format}",
                                    Line = decorator.Line - 1,
                                    Column = decorator.Column - 1,
                                    Length = decorator.Name.Length,
                                    Source = "decorator"
                                });
                            }
                        }
                    }
                }
            }
        }
    }
}
