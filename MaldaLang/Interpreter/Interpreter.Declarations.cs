// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Declarations;
using System.Threading;
using MaldaLang.BuiltIns;
using MaldaLang.BuiltIns.LLMClientBridge.BackendAdapters;
using System.Reflection;
using System.IO;
using System;
using System.Globalization;
using System.Text.Json;
using System.Linq;
using MaldaLang.PackageManager;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Interpreter.Debug;

public partial class Interpreter
{
    private async Task DefineClassAsync(ClassDeclaration decl)
    {
        if (_classes.TryGetValue(decl.Name, out var existing))
        {
            var problems = ClassAugmentation.CollectProblems(ShapeOf(existing), decl);
            if (problems.Count > 0)
            {
                var message = problems.Count == 1
                    ? problems[0]
                    : string.Join(System.Environment.NewLine, problems);
                throw new RuntimeException(message, decl.Line, decl.SourceFile);
            }

            await AddClassMembersAsync(existing, decl);
            _globals.Define(decl.Name, RuntimeValue.Class(existing));
            return;
        }

        ClassDefinition? superclass = null;
        if (decl.Superclass != null)
        {
            if (!_classes.ContainsKey(decl.Superclass))
                throw new RuntimeException($"Superclass '{decl.Superclass}' not found.");
            superclass = _classes[decl.Superclass];
        }

        var klass = new ClassDefinition(decl.Name, superclass);
        await AddClassMembersAsync(klass, decl);
        _classes[decl.Name] = klass;
        _globals.Define(decl.Name, RuntimeValue.Class(klass));
    }

    private static ClassAugmentation.ExistingClass ShapeOf(ClassDefinition klass)
    {
        var names = new List<string>(klass.Fields.Count + klass.Methods.Count + klass.StaticMethods.Count + klass.StaticFields.Count + 1);
        names.AddRange(klass.Fields.Keys);
        names.AddRange(klass.Methods.Keys);
        names.AddRange(klass.StaticMethods.Keys);
        names.AddRange(klass.StaticFields.Keys);
        if (klass.Constructor?.Declaration != null)
            names.Add(klass.Constructor.Declaration.Name);
        return new ClassAugmentation.ExistingClass(klass.Name, klass.Superclass?.Name, klass.Constructor != null, names);
    }

    private async Task AddClassMembersAsync(ClassDefinition klass, ClassDeclaration decl)
    {
        foreach (var member in decl.Members)
        {
            if (member.Type == MemberType.Field)
            {
                klass.Fields[member.Name] = member;
                if (member.IsStatic)
                {
                    klass.StaticFieldAccess[member.Name] = member.Access;
                    if (member.Value is Expression initExpr)
                    {
                        klass.StaticFields[member.Name] = await EvaluateAsync(initExpr);
                    }
                }
            }
            else if (member.Type == MemberType.Method)
            {
                var funcDecl = (FunctionDeclaration)member.Value!;
                var method = new FunctionValue(funcDecl, _globals, false, decl.Name);
                method.Decorators = funcDecl.Decorators;
                method.ParameterDecorators = funcDecl.ParameterDecorators;
                if (member.IsStatic)
                {
                    klass.StaticMethods[member.Name] = method;
                    klass.StaticMethodAccess[member.Name] = member.Access;
                }
                else
                {
                    klass.Methods[member.Name] = method;
                    klass.MethodAccess[member.Name] = member.Access;
                }
            }
            else if (member.Type == MemberType.Constructor)
            {
                var funcDecl = (FunctionDeclaration)member.Value!;
                var constructor = new FunctionValue(funcDecl, _globals, true, decl.Name);
                constructor.Decorators = funcDecl.Decorators;
                constructor.ParameterDecorators = funcDecl.ParameterDecorators;
                klass.Constructor = constructor;
            }
        }
    }
    
    private void DefineFunction(FunctionDeclaration decl)
    {
        // Capture the global environment for function closure
        // This ensures functions can access other functions and global variables
        var function = new FunctionValue(decl, _globals);
        function.Decorators = decl.Decorators;
        function.ParameterDecorators = decl.ParameterDecorators;
        // Always define functions in the global environment to ensure they're accessible
        _globals.Define(decl.Name, RuntimeValue.Function(function));

        // Check for @Tool decorator and register tool
        if (decl.Decorators != null)
        {
            var toolDecorator = decl.Decorators.FirstOrDefault(d => d.Name == "Tool");
            if (toolDecorator != null)
            {
                RegisterToolFromDecorator(function, toolDecorator, decl);
            }
        }
    }

    /// <summary>
    /// Binds a <c>function</c> declared inside a block, function body, loop, or match.
    /// The top-level declaration pass never sees these, and skipping them left the name
    /// undefined (for example <c>memoryEmbed</c> in the assistant script). The closure is
    /// the active environment, so the function reads enclosing locals when it is called.
    /// </summary>
    private RuntimeValue? BindLocalFunction(FunctionDeclaration decl)
    {
        var function = new FunctionValue(decl, _environment);
        function.Decorators = decl.Decorators;
        function.ParameterDecorators = decl.ParameterDecorators;
        _environment.Define(decl.Name, RuntimeValue.Function(function));

        if (decl.Decorators != null)
        {
            var toolDecorator = decl.Decorators.FirstOrDefault(d => d.Name == "Tool");
            if (toolDecorator != null && !LocalToolAlreadyRegistered(toolDecorator))
                RegisterToolFromDecorator(function, toolDecorator, decl);
        }

        return null;
    }

    private bool LocalToolAlreadyRegistered(Decorator toolDecorator)
    {
        if (toolDecorator.Arguments == null || toolDecorator.Arguments.Count == 0)
            return false;

        var nameValue = EvaluateDecoratorArgumentSync(toolDecorator.Arguments[0]);
        return nameValue.Type == ValueType.String
            && ToolRegistry.Instance.GetTool(nameValue.AsString()) != null;
    }
    
    private void DefinePrompt(PromptDeclaration decl)
    {
        // Capture the global environment for prompt closure
        // This ensures prompts can access other prompts, functions, and global variables
        var prompt = new PromptValue(decl, _globals);
        // Always define prompts in the global environment to ensure they're accessible
        _globals.Define(decl.Name, RuntimeValue.Prompt(prompt));
    }

    private void DefineWorkflow(WorkflowDeclaration decl)
    {
        _workflows[decl.Name] = decl;
    }

    private void DefineContext(ContextDeclaration decl)
    {
        var klass = new MaldaLang.BuiltIns.DeclaredContextClass(decl);
        _classes[decl.Name] = klass;
        _globals.Define(decl.Name, RuntimeValue.Class(klass));
    }

    private async Task<RuntimeValue?> ExecuteWithinAsync(WithinStatement stmt)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1, stmt.TimeoutMs)));
        var previous = GetActivation().CancelSource;
        GetActivation().CancelSource = cts;
        WithinBoundsContext.Push(stmt.TimeoutMs);
        if (stmt.HasBudget)
            ResourceBoundsContext.Push(new ResourceBudget(stmt.BudgetTokens, stmt.BudgetTools, stmt.BudgetCost), "within");
        try
        {
            await ExecuteAsync(stmt.Body);
            WithinBoundsContext.EnsureWithinBound("within");
            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or RuntimeException)
        {
            MaldaLang.Runtime.Journal.RunJournal.Current.Append(new MaldaLang.Runtime.Journal.JournalEvent
            {
                Kind = MaldaLang.Runtime.Journal.JournalKind.Prompt,
                Name = "within",
                Ok = false,
                Ms = stmt.TimeoutMs,
                Error = "Timeout"
            });
            if (ex is RuntimeException re && re.Message.Contains("within", StringComparison.OrdinalIgnoreCase))
                throw new RuntimeException($"Timeout: exceeded within ({stmt.TimeoutMs}ms)");
            throw;
        }
        finally
        {
            WithinBoundsContext.Pop();
            if (stmt.HasBudget)
                ResourceBoundsContext.Pop();
            GetActivation().CancelSource = previous;
        }
    }

    private void DefineProperty(PropertyDeclaration decl)
    {
        if (_properties.ContainsKey(decl.Name))
        {
            throw new RuntimeException($"Property '{decl.Name}' is already defined");
        }

        _properties[decl.Name] = decl;
    }

    internal WorkflowDeclaration? GetWorkflow(string name) => _workflows.TryGetValue(name, out var w) ? w : null;
    internal PropertyDeclaration? GetProperty(string name) => _properties.TryGetValue(name, out var p) ? p : null;

    private void DefineSumType(TypeDeclaration decl)
    {
        MaldaLang.BuiltIns.SumTypeRegistry.Register(decl);
        var ns = new MaldaLang.BuiltIns.JsonObject();
        foreach (var ctor in decl.Constructors)
        {
            var fv = new FunctionValue(null, null)
            {
                VariantConstructorTag = ctor.Name,
                VariantConstructorArity = ctor.ParameterNames.Count
            };
            var fn = RuntimeValue.Function(fv);
            _globals.Define(ctor.Name, fn);
            ns.Set(ctor.Name, fn);
        }

        // Type name is a constructor namespace: r.Ok(...) / Result.Err(...).
        _globals.Define(decl.TypeName, RuntimeValue.Object(ns));
    }

    private void RegisterToolFromDecorator(FunctionValue function, Decorator decorator, FunctionDeclaration decl)
    {
        try
        {
            // @Tool decorator should have at least 2 arguments: name and description
            if (decorator.Arguments == null || decorator.Arguments.Count < 2)
            {
                throw new RuntimeException($"@Tool decorator requires at least 2 arguments: name and description");
            }
            
            // Evaluate name (first argument)
            var nameValue = EvaluateDecoratorArgumentSync(decorator.Arguments[0]);
            if (nameValue.Type != ValueType.String)
            {
                throw new RuntimeException("@Tool decorator first argument (name) must be a string");
            }
            var toolName = nameValue.AsString();
            
            // Evaluate description (second argument)
            var descValue = EvaluateDecoratorArgumentSync(decorator.Arguments[1]);
            if (descValue.Type != ValueType.String)
            {
                throw new RuntimeException("@Tool decorator second argument (description) must be a string");
            }
            var toolDescription = descValue.AsString();
            
            // Evaluate optional schema (third argument): schema/sum-type name, JSON string, or object.
            RuntimeValue? schema = null;
            if (decorator.Arguments.Count >= 3)
            {
                schema = ToolSchemaResolver.Resolve(
                    EvaluateDecoratorArgumentSync(decorator.Arguments[2]));
            }
            
            // Generate schema if not provided
            var finalSchema = ToolSchemaGenerator.GenerateSchema(decl, schema);
            
            // Create ToolInstance
            var tool = new ToolInstance();
            tool.Initialize(toolName, toolDescription, finalSchema, null, "");
            if (schema != null && schema.Type == ValueType.Object)
                tool.MarkAttachedSchema();
            tool.SetFunctionHandler(function, this);
            
            // Register in ToolRegistry
            ToolRegistry.Instance.RegisterTool(tool);
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"Error registering tool from @Tool decorator: {ex.Message}", decl.Line, _currentFile);
        }
    }
    
    private RuntimeValue JsonToRuntimeValue(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var jsonObj = new JsonObject();
                foreach (var prop in element.EnumerateObject())
                {
                    jsonObj.Set(prop.Name, JsonToRuntimeValue(prop.Value));
                }
                return RuntimeValue.Object(jsonObj);
            
            case System.Text.Json.JsonValueKind.Array:
                var arr = new List<RuntimeValue>();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Add(JsonToRuntimeValue(item));
                }
                return RuntimeValue.Array(arr);
            
            case System.Text.Json.JsonValueKind.String:
                return RuntimeValue.String(element.GetString() ?? "");
            
            case System.Text.Json.JsonValueKind.Number:
                if (element.TryGetInt32(out var intVal))
                    return RuntimeValue.Integer(intVal);
                return RuntimeValue.Float(element.GetDouble());
            
            case System.Text.Json.JsonValueKind.True:
                return RuntimeValue.Boolean(true);
            
            case System.Text.Json.JsonValueKind.False:
                return RuntimeValue.Boolean(false);
            
            case System.Text.Json.JsonValueKind.Null:
                return RuntimeValue.Null();
            
            default:
                return RuntimeValue.Null();
        }
    }
    
    private RuntimeValue EvaluateDecoratorArgumentSync(Expression expr)
    {
        try
        {
            return ToolSchemaResolver.EvaluateNameOrLiteral(expr, this);
        }
        catch (Exception ex)
        {
            throw new RuntimeException(
                $"Decorator argument must be a literal (string, number, boolean, null) or a schema/sum-type name. {ex.Message}");
        }
    }
}
