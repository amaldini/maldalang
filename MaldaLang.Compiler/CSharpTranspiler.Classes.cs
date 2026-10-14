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
    private void TranspileClass(ClassDeclaration classDecl)
    {
        WriteIndent();
        _output.Append("public class ");
        _output.Append(EscapeIdentifier(classDecl.Name));
        
        if (classDecl.Superclass != null)
        {
            _output.Append(" : ");
            _output.Append(EscapeIdentifier(classDecl.Superclass));
        }
        
        _output.AppendLine();
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(TranspileClass) + " (open)");
        _output.AppendLine();
        _indentLevel++;
        
        var previousClassName = _currentClassName;
        _currentClassName = classDecl.Name;
        foreach (var member in classDecl.Members)
        {
            TranspileClassMember(member);
        }
        _currentClassName = previousClassName;

        // A MALDA subclass constructor that omits super(...) does not run the parent
        // constructor, but C# requires chaining to some base constructor. When this class is
        // extended and exposes no parameterless constructor to chain to, emit a protected
        // parameterless one so such subclasses compile instead of failing with CS7036.
        if (IsExtended(classDecl.Name) && !HasAccessibleParameterlessConstructor(classDecl))
        {
            WriteIndent();
            _output.Append("protected ");
            _output.Append(EscapeIdentifier(classDecl.Name));
            _output.AppendLine("() { }");
        }

        // A MALDA subclass with no constructor of its own inherits the nearest ancestor's
        // constructor and forwards the arguments it was constructed with (matching the
        // interpreter and the JavaScript backend). C# needs an explicit constructor to do
        // that, otherwise `new Sub(a, b)` fails with CS1729.
        if (!DeclaresOwnConstructor(classDecl))
        {
            var inheritedCtor = FindInheritedConstructor(classDecl);
            if (inheritedCtor != null && inheritedCtor.Parameters.Count > 0)
            {
                TranspileForwardingConstructor(classDecl, inheritedCtor);
            }
        }

        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(TranspileClass) + " (close)");
        _output.AppendLine();
    }

    /// <summary>
    /// True when <paramref name="classDecl"/> declares a constructor of its own (including a
    /// constructor synthesized from a primary constructor).
    /// </summary>
    private static bool DeclaresOwnConstructor(ClassDeclaration classDecl)
    {
        foreach (var member in classDecl.Members)
        {
            if (member.Type == MemberType.Constructor)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Walks the superclass chain for the nearest constructor a subclass without its own
    /// constructor would inherit, mirroring the interpreter's argument forwarding.
    /// </summary>
    private FunctionDeclaration? FindInheritedConstructor(ClassDeclaration classDecl)
        => FindInheritedConstructor(classDecl.Superclass);

    /// <summary>
    /// Walks the superclass chain starting at <paramref name="className"/> for the nearest
    /// declared constructor, or null when no ancestor declares one.
    /// </summary>
    private FunctionDeclaration? FindInheritedConstructor(string? className)
    {
        for (var current = className; current != null && _classDeclarations.TryGetValue(current, out var decl); current = decl.Superclass)
        {
            foreach (var member in decl.Members)
            {
                if (member.Type == MemberType.Constructor && member.Value is FunctionDeclaration ctor)
                {
                    return ctor;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Emits a constructor on <paramref name="classDecl"/> that forwards its arguments to the
    /// inherited base constructor, so a subclass without its own constructor behaves the same
    /// as the interpreter and the JavaScript backend.
    /// </summary>
    private void TranspileForwardingConstructor(ClassDeclaration classDecl, FunctionDeclaration inheritedCtor)
    {
        WriteIndent();
        _output.Append("public ");
        _output.Append(EscapeIdentifier(classDecl.Name));
        _output.Append("(");
        for (int i = 0; i < inheritedCtor.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            var parameterType = (inheritedCtor.ParameterTypeHints != null && i < inheritedCtor.ParameterTypeHints.Count)
                ? ResolveTranspiledTypeHint(inheritedCtor.ParameterTypeHints[i])
                : TranspiledClrType.Object;
            _output.Append(GetClrTypeName(parameterType));
            _output.Append(" ");
            _output.Append(EscapeIdentifier("__ctorArg" + i));
        }
        _output.Append(") : base(");
        for (int i = 0; i < inheritedCtor.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append(EscapeIdentifier("__ctorArg" + i));
        }
        _output.AppendLine(") { }");
    }

    /// <summary>
    /// True when some class in this compilation extends <paramref name="className"/>, so its
    /// subclasses may need to chain to a parameterless base constructor.
    /// </summary>
    private bool IsExtended(string className)
    {
        foreach (var decl in _classDeclarations.Values)
        {
            if (string.Equals(decl.Superclass, className, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="classDecl"/> already offers a parameterless constructor that a
    /// derived class can chain to: either it declares no constructor at all (C# supplies an
    /// implicit public parameterless one) or it declares an explicit zero-parameter constructor.
    /// </summary>
    private static bool HasAccessibleParameterlessConstructor(ClassDeclaration classDecl)
    {
        var hasAnyConstructor = false;
        foreach (var member in classDecl.Members)
        {
            if (member.Type != MemberType.Constructor)
                continue;

            hasAnyConstructor = true;
            if (member.Value is FunctionDeclaration ctor && ctor.Parameters.Count == 0)
                return true;
        }
        return !hasAnyConstructor;
    }

    private void TranspileClassMember(ClassMember member)
    {
        var previousMemberIsStatic = _currentMemberIsStatic;
        _currentMemberIsStatic = member.IsStatic;
        WriteIndent();
        
        // Access modifier. MALDA's default is public for both fields and methods, and
        // instance members are also reached through reflection, so emit `public` whenever
        // the member is not explicitly private.
        if (member.Access == AccessModifier.Private)
            _output.Append("private ");
        else
            _output.Append("public ");
        
        if (member.IsStatic)
            _output.Append("static ");
        
        // Member type
        switch (member.Type)
        {
            case MemberType.Field:
                var fieldType = ResolveTranspiledTypeHint(member.TypeHint);
                _output.Append(GetClrTypeName(fieldType));
                _output.Append(" ");
                _output.Append(member.Name);
                if (member.Value != null && member.Value is Expression expr)
                {
                    // Field initializers run before `this` exists in C#, so any member access
                    // here must not emit `this.GetType()` as the accessing class.
                    var initializerClassName = _currentClassName;
                    _currentClassName = null;
                    _output.Append(" = ");
                    _output.Append(GetCoercionExpressionPrefix(fieldType));
                    TranspileExpression(expr);
                    _output.Append(GetCoercionExpressionSuffix(fieldType));
                    _currentClassName = initializerClassName;
                }
                _output.AppendLine(";");
                break;
                
            case MemberType.Method:
                if (member.Value is FunctionDeclaration func)
                {
                    // Transpile method decorators
                    if (func.Decorators != null && func.Decorators.Count > 0)
                    {
                        foreach (var decorator in func.Decorators)
                        {
                            WriteIndent();
                            TranspileDecorator(decorator);
                            _output.AppendLine();
                        }
                    }

                    WriteIndent();
                    var methodReturnType = ResolveTranspiledTypeHint(func.ReturnType);
                    _output.Append(GetClrTypeName(methodReturnType));
                    _output.Append(" ");
                    _output.Append(EscapeIdentifier(member.Name));
                    _output.Append("(");
                    PushTypedScope();
                    for (int i = 0; i < func.Parameters.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");

                        // Parameter decorators
                        if (func.ParameterDecorators != null && i < func.ParameterDecorators.Count)
                        {
                            var decorator = func.ParameterDecorators[i];
                            if (decorator != null)
                            {
                                TranspileDecorator(decorator);
                                _output.Append(" ");
                            }
                        }

                        var parameterType = (func.ParameterTypeHints != null && i < func.ParameterTypeHints.Count)
                            ? ResolveTranspiledTypeHint(func.ParameterTypeHints[i])
                            : TranspiledClrType.Object;
                        RegisterTypedVariable(func.Parameters[i], parameterType);
                        _output.Append(GetClrTypeName(parameterType));
                        _output.Append(" ");
                        _output.Append(EscapeIdentifier(func.Parameters[i]));
                    }
                    _output.AppendLine(")");
                    var previousCanAwait = _canAwait;
                    _canAwait = false;
                    _currentFunctionReturnType.Push(methodReturnType);
                    var previousLocals = EnterLocals(func, typed: false);
                    TranspileFunctionBlock(func.Body, member.Name, func.Line, appendImplicitNullReturn: true,
                        budget: DeclarationBounds.TryGetResourceBudget(func));
                    _activeLocals = previousLocals;
                    _currentFunctionReturnType.Pop();
                    PopTypedScope();
                    _canAwait = previousCanAwait;

                    if (_currentClassName != null &&
                        _typedTwins.TryGetValue(_currentClassName + "." + member.Name, out var typedMethodSignature))
                    {
                        TranspileTypedMethodTwin(member, func, typedMethodSignature);
                    }
                }
                break;
                
            case MemberType.Constructor:
                if (member.Value is FunctionDeclaration ctor)
                {
                    // Transpile constructor decorators
                    if (ctor.Decorators != null && ctor.Decorators.Count > 0)
                    {
                        foreach (var decorator in ctor.Decorators)
                        {
                            WriteIndent();
                            TranspileDecorator(decorator);
                            _output.AppendLine();
                        }
                    }

                    WriteIndent();
                    // Visibility was already emitted above (public unless explicitly private);
                    // public is required for actor spawn.
                    _output.Append(EscapeIdentifier(member.Name));
                    _output.Append("(");
                    PushTypedScope();
                    for (int i = 0; i < ctor.Parameters.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");

                        // Parameter decorators
                        if (ctor.ParameterDecorators != null && i < ctor.ParameterDecorators.Count)
                        {
                            var decorator = ctor.ParameterDecorators[i];
                            if (decorator != null)
                            {
                                TranspileDecorator(decorator);
                                _output.Append(" ");
                            }
                        }

                        var parameterType = (ctor.ParameterTypeHints != null && i < ctor.ParameterTypeHints.Count)
                            ? ResolveTranspiledTypeHint(ctor.ParameterTypeHints[i])
                            : TranspiledClrType.Object;
                        RegisterTypedVariable(ctor.Parameters[i], parameterType);
                        _output.Append(GetClrTypeName(parameterType));
                        _output.Append(" ");
                        _output.Append(EscapeIdentifier(ctor.Parameters[i]));
                    }
                    _output.AppendLine(")");
                    var leadingSuperCall = TryGetLeadingSuperConstructorCall(ctor.Body);
                    if (leadingSuperCall != null)
                    {
                        // MALDA calls the parent constructor explicitly; mirror it with a C#
                        // constructor initializer so the parent's own field init/body run first.
                        // When no ancestor declares a constructor, super(...) is a no-op (the
                        // implicit parameterless base constructor runs), so drop the arguments
                        // rather than emitting an invalid base(...) call.
                        var superclassName = _currentClassName != null
                            && _classDeclarations.TryGetValue(_currentClassName, out var currentClassDecl)
                                ? currentClassDecl.Superclass
                                : null;
                        var inheritedCtor = FindInheritedConstructor(superclassName);
                        _output.Append(": base(");
                        if (inheritedCtor != null)
                        {
                            for (int i = 0; i < leadingSuperCall.Arguments.Count; i++)
                            {
                                if (i > 0) _output.Append(", ");
                                TranspileExpression(leadingSuperCall.Arguments[i]);
                            }
                        }
                        _output.AppendLine(")");
                    }
                    _currentFunctionReturnType.Push(TranspiledClrType.Object);
                    var previousLocals = EnterLocals(ctor, typed: false);
                    TranspileFunctionBlock(
                        ctor.Body,
                        member.Name + ".ctor",
                        ctor.Line,
                        skipLeadingStatement: leadingSuperCall != null);
                    _activeLocals = previousLocals;
                    _currentFunctionReturnType.Pop();
                    PopTypedScope();
                }
                break;
        }
        _currentMemberIsStatic = previousMemberIsStatic;
    }

    private void TranspileDecorator(Decorator decorator)
    {
        if (TargetPartitioner.IsCompileTimeTargetDecorator(decorator.Name))
        {
            return;
        }

        if (string.Equals(decorator.Name, "budget", StringComparison.OrdinalIgnoreCase))
        {
            // Runtime-enforced via ResourceBoundsContext / PromptInstance.Budget, not a CLR attribute.
            return;
        }

        _output.Append("[");
        _output.Append(decorator.Name);
        _output.Append("Attribute");
        if (decorator.Arguments != null && decorator.Arguments.Count > 0)
        {
            _output.Append("(");
            for (int i = 0; i < decorator.Arguments.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                var arg = decorator.Arguments[i] is NamedArgumentExpression named
                    ? named.Value
                    : decorator.Arguments[i];
                // Bare schema/sum-type names on @Tool / @MCPTool are not C# identifiers.
                if (i == 2 &&
                    arg is IdentifierExpression schemaName &&
                    (decorator.Name == "Tool" || decorator.Name == "MCPTool"))
                {
                    _output.Append(ToQuotedString(schemaName.Name));
                }
                else
                {
                    TranspileExpression(arg);
                }
            }
            _output.Append(")");
        }
        _output.Append("]");
    }

    /// <summary>
    /// Emits <c>super.method(args)</c>. A directly callable base member becomes
    /// <c>base.method(args)</c>; a private base method must go through reflection, because
    /// C# forbids <c>base.</c> access to a private member (CS0122) while MALDA allows it.
    /// </summary>
    private void TranspileSuperMethodCall(FunctionCallExpression call, string methodName)
    {
        if (_currentClassName == null
            || !_classDeclarations.TryGetValue(_currentClassName, out var currentDecl)
            || currentDecl.Superclass == null)
        {
            throw new NotSupportedException("'super' is only supported inside a class with a superclass.");
        }

        var targetMember = FindMethodMember(currentDecl.Superclass, methodName);

        if (targetMember != null && targetMember.Access == AccessModifier.Private)
        {
            // Pass the class that actually declares the method: with DeclaredOnly lookup, using
            // the immediate superclass would miss a private method declared on a grandparent.
            var declaringTypeName = FindMethodDeclaringClassName(currentDecl.Superclass, methodName)
                ?? currentDecl.Superclass;

            _output.Append("RuntimeHelpers.CallBaseMethod(this, typeof(");
            _output.Append(EscapeIdentifier(declaringTypeName));
            _output.Append("), \"");
            _output.Append(EscapeIdentifier(methodName));
            _output.Append("\", new List<object> { ");
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                TranspileExpression(call.Arguments[i]);
            }
            _output.Append(" })");
            return;
        }

        var paramTypes = targetMember?.Value is FunctionDeclaration targetFunc
            ? targetFunc.ParameterTypeHints
            : null;

        _output.Append("base.");
        _output.Append(EscapeIdentifier(methodName));
        _output.Append("(");
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            var paramType = (paramTypes != null && i < paramTypes.Count)
                ? ResolveTranspiledTypeHint(paramTypes[i])
                : TranspiledClrType.Object;
            _output.Append(GetCoercionExpressionPrefix(paramType));
            TranspileExpression(call.Arguments[i]);
            _output.Append(GetCoercionExpressionSuffix(paramType));
        }
        _output.Append(")");
    }

    /// <summary>
    /// Walks the superclass chain from <paramref name="className"/> to find
    /// <paramref name="methodName"/>, mirroring <c>ClassDefinition.FindMethod</c>.
    /// </summary>
    private ClassMember? FindMethodMember(string? className, string methodName)
    {
        for (var current = className; current != null && _classDeclarations.TryGetValue(current, out var decl); current = decl.Superclass)
        {
            foreach (var member in decl.Members)
            {
                if (member.Type == MemberType.Method
                    && string.Equals(member.Name, methodName, StringComparison.Ordinal))
                {
                    return member;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the name of the class in the superclass chain starting at
    /// <paramref name="className"/> that declares <paramref name="methodName"/>, or null when
    /// no declaration is found in this compilation.
    /// </summary>
    private string? FindMethodDeclaringClassName(string? className, string methodName)
    {
        for (var current = className; current != null && _classDeclarations.TryGetValue(current, out var decl); current = decl.Superclass)
        {
            foreach (var member in decl.Members)
            {
                if (member.Type == MemberType.Method
                    && string.Equals(member.Name, methodName, StringComparison.Ordinal))
                {
                    return decl.Name;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the leading <c>super(...)</c> call of a constructor body, or null when the body
    /// does not start with one. Only a leading call can be represented as a C# constructor
    /// initializer, since initializers always run before the body.
    /// </summary>
    private static FunctionCallExpression? TryGetLeadingSuperConstructorCall(BlockStatement body)
    {
        if (body.Statements.Count > 0
            && body.Statements[0] is ExpressionStatement { Expression: FunctionCallExpression { Callee: SuperExpression } superCall })
        {
            return superCall;
        }
        return null;
    }

    private void TranspileNew(NewExpression newExpr)
    {
        var className = newExpr.ClassName;
        var contextDecl = _contextDeclarations.FirstOrDefault(c => string.Equals(c.Name, className, StringComparison.Ordinal));
        if (contextDecl != null)
        {
            var pin = string.Join(", ", contextDecl.Pin.Select(p => "\"" + p.Replace("\"", "\\\"") + "\""));
            _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.DeclaredContextInstance.Create(");
            _output.Append($"\"{contextDecl.Name}\", {contextDecl.BudgetTokens ?? 0}, new List<string> {{ {pin} }}, ");
            _output.Append($"{contextDecl.RetainLast ?? 0}, \"{contextDecl.Evict}\", ");
            _output.Append(contextDecl.CompactPromptName == null ? "null" : $"\"{contextDecl.CompactPromptName}\"");
            _output.Append(", MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter()))");
            return;
        }

        var mappedClassName = MapBuiltInClassName(className);
        
        // Handle special cases that require parameterless constructors + initialization
        if (className == "LlamaCppClient" && newExpr.Arguments.Count == 0)
        {
            _output.Append("(new MaldaLang.BuiltIns.LlamaCppClientInstance())");
            return;
        }

        if (className == "LlamaCppClient" && newExpr.Arguments.Count == 1)
        {
            // LlamaCppClient(modelPath) -> new LlamaCppClientInstance() { ModelPath = modelPath }
            _output.Append("(new MaldaLang.BuiltIns.LlamaCppClientInstance() { ModelPath = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(") })");
            return;
        }
        
        if (className == "LlamaEmbedder" && newExpr.Arguments.Count == 1)
        {
            // LlamaEmbedder(modelPath) -> new LlamaEmbedderInstance() { ModelPath = modelPath }
            _output.Append("(new MaldaLang.BuiltIns.LlamaEmbedderInstance() { ModelPath = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(") })");
            return;
        }

        if (className == "OnnxModel" && newExpr.Arguments.Count == 1)
        {
            _output.Append("(new MaldaLang.BuiltIns.OnnxModelInstance(RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(")))");
            return;
        }

        if (className is "Dense" or "Sequential" or "Conv" or "Conv2D" or "MaxPool2D" or "AvgPool2D"
            or "Flatten" or "BatchNorm2D" or "Dropout2D" or "GlobalAvgPool2D" or "GlobalMaxPool2D"
            or "Activation" or "Embedding" or "Rnn" or "LayerNorm" or "Attention" or "Parallel" or "Identity")
        {
            var instanceType = MapBuiltInClassName(className);
            _output.Append("new ");
            _output.Append(instanceType);
            _output.Append("(new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { ");
            for (var i = 0; i < newExpr.Arguments.Count; i++)
            {
                if (i > 0)
                    _output.Append(", ");
                _output.Append("RuntimeHelpers.ToRuntimeValue(");
                TranspileExpression(newExpr.Arguments[i]);
                _output.Append(")");
            }

            _output.Append(" })");
            return;
        }
        
        if (className == "LLMClient" && newExpr.Arguments.Count == 3)
        {
            // LLMClient(apiUrl, apiKey, model) — instance uses parameterless ctor + property init (interpreter parity).
            _output.Append("(new System.Func<MaldaLang.BuiltIns.LLMClientInstance>(() => { ");
            _output.Append("var __llm = new MaldaLang.BuiltIns.LLMClientInstance(); ");
            _output.Append("__llm.ApiUrl = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append("); ");
            _output.Append("__llm.ApiKey = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append("); ");
            _output.Append("__llm.Model = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[2]);
            _output.Append("); ");
            _output.Append("return __llm; })())");
            return;
        }

        if (className == "Conversation" && newExpr.Arguments.Count == 2)
        {
            // Conversation(client, systemPrompt) -> new ConversationInstance() then Initialize()
            _output.Append("(new System.Func<MaldaLang.BuiltIns.ConversationInstance>(() => { ");
            _output.Append("var __conv = new MaldaLang.BuiltIns.ConversationInstance(); ");
            _output.Append("object __clientObj = ");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append("; ");
            _output.Append("MaldaLang.BuiltIns.LLMClientInstance? __llmClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LlamaCppClientInstance? __llamaClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance? __bridgeClient = null; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LLMClientInstance __llm) __llmClient = __llm; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LlamaCppClientInstance __llama) __llamaClient = __llama; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance __bridge) __bridgeClient = __bridge; ");
            _output.Append("__conv.Initialize(__llmClient, __llamaClient, __bridgeClient, RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append("), null); ");
            _output.Append("return __conv; })())");
            return;
        }

        // Tool(name, description, parameters, handler) — parameterless ctor + Initialize/SetFunctionHandler.
        if (className == "Tool" && newExpr.Arguments.Count == 4)
        {
            _output.Append("(new System.Func<MaldaLang.BuiltIns.ToolInstance>(() => { ");
            _output.Append("var __tool = new MaldaLang.BuiltIns.ToolInstance(); ");
            _output.Append("var __name = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append("); ");
            _output.Append("var __desc = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append("); ");
            _output.Append("var __paramsRv = RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(newExpr.Arguments[2]);
            _output.Append("); ");
            _output.Append("var __handlerRv = RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(newExpr.Arguments[3]);
            _output.Append("); ");
            _output.Append("__tool.Initialize(__name, __desc, __paramsRv, null); ");
            _output.Append("if (__handlerRv.Type == MaldaLang.Interpreter.ValueType.Function) { ");
            _output.Append("var __interp = MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter(); ");
            _output.Append("__tool.SetFunctionHandler(__handlerRv.AsFunction(), __interp); ");
            _output.Append("} ");
            _output.Append("return __tool; })())");
            return;
        }
        
        // OpenRouterClient(model?) — coerce model because locals/params are object-typed.
        if (className == "OpenRouterClient" && newExpr.Arguments.Count <= 1)
        {
            _output.Append("new MaldaLang.BuiltIns.OpenRouterClientInstance(");
            if (newExpr.Arguments.Count == 1)
            {
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(newExpr.Arguments[0]);
                _output.Append(")");
            }
            _output.Append(")");
            return;
        }

        // Agent(name, role, instructions, client?) — same optional-client convention as CodingAgent.
        // Vars/params are object-typed, so coerce strings and resolve client via runtime type tests.
        if (className == "Agent" && newExpr.Arguments.Count is 3 or 4)
        {
            _output.Append("(new System.Func<MaldaLang.BuiltIns.AgentInstance>(() => { ");
            _output.Append("var __agent = new MaldaLang.BuiltIns.AgentInstance(); ");
            _output.Append("var __name = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append("); ");
            _output.Append("var __role = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append("); ");
            _output.Append("var __instructions = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[2]);
            _output.Append("); ");
            _output.Append("MaldaLang.BuiltIns.LLMClientInstance? __llmClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LlamaCppClientInstance? __llamaClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance? __bridgeClient = null; ");
            if (newExpr.Arguments.Count == 4)
            {
                _output.Append("object __clientObj = ");
                TranspileExpression(newExpr.Arguments[3]);
                _output.Append("; ");
                _output.Append("if (__clientObj is MaldaLang.BuiltIns.LLMClientInstance __llm) __llmClient = __llm; ");
                _output.Append("else if (__clientObj is MaldaLang.BuiltIns.LlamaCppClientInstance __llama) __llamaClient = __llama; ");
                _output.Append("else if (__clientObj is MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance __bridge) __bridgeClient = __bridge; ");
                _output.Append("else if (__clientObj != null) throw new System.Exception(\"Agent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge\"); ");
            }
            // No client / null → default local LLM (same as CodingAgent when client is omitted).
            _output.Append("if (__llmClient == null && __llamaClient == null && __bridgeClient == null) ");
            _output.Append("__llamaClient = MaldaLang.BuiltIns.DefaultLocalLlm.GetDefaultLocalClient(); ");
            _output.Append("__agent.Initialize(__name, __role, __instructions, __llmClient, __llamaClient, __bridgeClient, null); ");
            _output.Append("__agent.SetInterpreter(MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter()); ");
            _output.Append("return __agent; })())");
            return;
        }

        // CodingAgent(name, role, instructions, client?, workingDirectory?)
        // 4th arg is either workingDirectory (string) or client (object) — disambiguate at runtime.
        if (className == "CodingAgent" && newExpr.Arguments.Count is >= 3 and <= 5)
        {
            TranspileCodingAgentFamilyNew(newExpr, "MaldaLang.BuiltIns.CodingAgentInstance");
            return;
        }

        if (className == "GitAgent" && newExpr.Arguments.Count is >= 3 and <= 5)
        {
            TranspileCodingAgentFamilyNew(newExpr, "MaldaLang.BuiltIns.GitAgentInstance");
            return;
        }

        if (className == "HumanAgent" && newExpr.Arguments.Count is >= 3 and <= 5)
        {
            TranspileCodingAgentFamilyNew(newExpr, "MaldaLang.BuiltIns.HumanAgentInstance");
            return;
        }

        if (className == "MALDACodingAgent" && newExpr.Arguments.Count is >= 3 and <= 5)
        {
            TranspileCodingAgentFamilyNew(newExpr, "MaldaLang.BuiltIns.MALDACodingAgentInstance");
            return;
        }

        if (className == "DevAgent" && newExpr.Arguments.Count == 7)
        {
            // DevAgent(name, role, instructions, client, workingDirectory, includeSymbols, prdAuthorOnly)
            _output.Append("(new System.Func<MaldaLang.BuiltIns.DevAgentInstance>(() => { ");
            _output.Append("object __clientObj = ");
            TranspileExpression(newExpr.Arguments[3]);
            _output.Append("; ");
            _output.Append("MaldaLang.BuiltIns.LLMClientInstance? __llmClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LlamaCppClientInstance? __llamaClient = null; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LLMClientInstance __llm) __llmClient = __llm; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LlamaCppClientInstance __llama) __llamaClient = __llama; ");
            _output.Append("var __devAgent = new MaldaLang.BuiltIns.DevAgentInstance(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(", ");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append(", RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[2]);
            _output.Append("), __llmClient, __llamaClient, null, RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[4]);
            _output.Append("), RuntimeHelpers.CoerceToBool(");
            TranspileExpression(newExpr.Arguments[5]);
            _output.Append("), null, false, RuntimeHelpers.CoerceToBool(");
            TranspileExpression(newExpr.Arguments[6]);
            _output.Append(")); __devAgent.SetInterpreter(MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter()); return __devAgent; })())");
            return;
        }

        if (className == "DevAgent" && newExpr.Arguments.Count == 6)
        {
            // DevAgent(name, role, instructions, client, workingDirectory, includeSymbols)
            _output.Append("(new System.Func<MaldaLang.BuiltIns.DevAgentInstance>(() => { ");
            _output.Append("object __clientObj = ");
            TranspileExpression(newExpr.Arguments[3]);
            _output.Append("; ");
            _output.Append("MaldaLang.BuiltIns.LLMClientInstance? __llmClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LlamaCppClientInstance? __llamaClient = null; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LLMClientInstance __llm) __llmClient = __llm; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LlamaCppClientInstance __llama) __llamaClient = __llama; ");
            _output.Append("var __devAgent = new MaldaLang.BuiltIns.DevAgentInstance(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(", ");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append(", RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[2]);
            _output.Append("), __llmClient, __llamaClient, null, RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[4]);
            _output.Append("), RuntimeHelpers.CoerceToBool(");
            TranspileExpression(newExpr.Arguments[5]);
            _output.Append(")); __devAgent.SetInterpreter(MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter()); return __devAgent; })())");
            return;
        }

        // VectorDB(dimension, precision) — coerce because top-level vars are object-typed.
        if (className == "VectorDB")
        {
            if (newExpr.Arguments.Count != 2)
                throw new NotSupportedException("VectorDB() expects 2 arguments: VectorDB(dimension, precision).");

            _output.Append("new MaldaLang.Interpreter.VectorDBInstance((int)RuntimeHelpers.CoerceToInt(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append("), RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append("))");
            return;
        }

        // RestServer(port?, host?) — coerce because top-level vars are object-typed.
        if (className == "RestServer")
        {
            _output.Append("new MaldaLang.BuiltIns.RestServerInstance(");
            if (newExpr.Arguments.Count >= 1)
            {
                _output.Append("(int)RuntimeHelpers.CoerceToInt(");
                TranspileExpression(newExpr.Arguments[0]);
                _output.Append(")");
            }
            else
            {
                _output.Append("0");
            }

            if (newExpr.Arguments.Count >= 2)
            {
                _output.Append(", RuntimeHelpers.CoerceToString(");
                TranspileExpression(newExpr.Arguments[1]);
                _output.Append(")");
            }

            _output.Append(")");
            return;
        }

        // HttpServer(port, webDirectory?, pathBase?, host?) needs explicit coercion because top-level vars are object-typed.
        if (className == "HttpServer" && newExpr.Arguments.Count >= 1)
        {
            _output.Append("new MaldaLang.BuiltIns.HttpServerInstance(RuntimeHelpers.CoerceToInt(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(")");

            if (newExpr.Arguments.Count >= 2)
            {
                _output.Append(", RuntimeHelpers.CoerceToString(");
                TranspileExpression(newExpr.Arguments[1]);
                _output.Append(")");
            }
            else
            {
                _output.Append(", (string)null");
            }

            // Transpiled code has no interpreter; pathBase is 3rd MALDA arg, host is 4th.
            _output.Append(", (MaldaLang.Interpreter.Interpreter)null");
            if (newExpr.Arguments.Count >= 3)
            {
                _output.Append(", ");
                TranspileExpression(newExpr.Arguments[2]);
            }
            else
            {
                _output.Append(", (object)null");
            }
            if (newExpr.Arguments.Count >= 4)
            {
                _output.Append(", RuntimeHelpers.CoerceToString(");
                TranspileExpression(newExpr.Arguments[3]);
                _output.Append(")");
            }
            else
            {
                _output.Append(", (object)null");
            }
            _output.Append(")");
            return;
        }
        
        // Default: use constructor with arguments
        _output.Append("new ");
        _output.Append(mappedClassName);
        
        _output.Append("(");
        for (int i = 0; i < newExpr.Arguments.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            TranspileExpression(newExpr.Arguments[i]);
        }
        _output.Append(")");
    }

    /// <summary>
    /// CodingAgent / GitAgent / HumanAgent / MALDACodingAgent share the same ctor shape and
    /// optional 4th-arg disambiguation (workingDirectory string vs LLM client object).
    /// </summary>
    private void TranspileCodingAgentFamilyNew(NewExpression newExpr, string instanceTypeName)
    {
        _output.Append("(new System.Func<");
        _output.Append(instanceTypeName);
        _output.Append(">(() => { ");
        _output.Append("var __name = RuntimeHelpers.CoerceToString(");
        TranspileExpression(newExpr.Arguments[0]);
        _output.Append("); ");
        _output.Append("var __role = RuntimeHelpers.CoerceToString(");
        TranspileExpression(newExpr.Arguments[1]);
        _output.Append("); ");
        _output.Append("var __instructions = RuntimeHelpers.CoerceToString(");
        TranspileExpression(newExpr.Arguments[2]);
        _output.Append("); ");
        _output.Append("MaldaLang.BuiltIns.LLMClientInstance? __llmClient = null; ");
        _output.Append("MaldaLang.BuiltIns.LlamaCppClientInstance? __llamaClient = null; ");
        _output.Append("MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance? __bridgeClient = null; ");
        _output.Append("string? __workDir = null; ");

        if (newExpr.Arguments.Count >= 4)
        {
            _output.Append("object __arg3 = ");
            TranspileExpression(newExpr.Arguments[3]);
            _output.Append("; ");
            if (newExpr.Arguments.Count == 4)
            {
                // 4th arg: string → workingDirectory; object → client (interpreter parity).
                _output.Append("if (__arg3 is string __workDirStr) __workDir = __workDirStr; ");
                _output.Append("else if (__arg3 is MaldaLang.BuiltIns.LLMClientInstance __llm) __llmClient = __llm; ");
                _output.Append("else if (__arg3 is MaldaLang.BuiltIns.LlamaCppClientInstance __llama) __llamaClient = __llama; ");
                _output.Append("else if (__arg3 is MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance __bridge) __bridgeClient = __bridge; ");
                _output.Append("else if (__arg3 != null) throw new System.Exception(\"CodingAgent/GitAgent fourth argument must be an LLM client or workingDirectory string\"); ");
            }
            else
            {
                // 5 args: client + workingDirectory
                _output.Append("if (__arg3 is MaldaLang.BuiltIns.LLMClientInstance __llm) __llmClient = __llm; ");
                _output.Append("else if (__arg3 is MaldaLang.BuiltIns.LlamaCppClientInstance __llama) __llamaClient = __llama; ");
                _output.Append("else if (__arg3 is MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance __bridge) __bridgeClient = __bridge; ");
                _output.Append("else if (__arg3 != null) throw new System.Exception(\"CodingAgent/GitAgent fourth argument must be an LLM client\"); ");
                _output.Append("__workDir = RuntimeHelpers.CoerceToString(");
                TranspileExpression(newExpr.Arguments[4]);
                _output.Append("); ");
            }
        }

        _output.Append(instanceTypeName);
        _output.Append(" __agent; ");
        _output.Append("if (__llamaClient != null && __llmClient == null && __bridgeClient == null) ");
        _output.Append("__agent = new ");
        _output.Append(instanceTypeName);
        _output.Append("(__name, __role, __instructions, __llamaClient, __workDir, null); ");
        _output.Append("else if (__bridgeClient != null) ");
        _output.Append("__agent = new ");
        _output.Append(instanceTypeName);
        _output.Append("(__name, __role, __instructions, __llmClient, __llamaClient, __bridgeClient, __workDir ?? \".\", null); ");
        _output.Append("else ");
        _output.Append("__agent = new ");
        _output.Append(instanceTypeName);
        _output.Append("(__name, __role, __instructions, __llmClient, __workDir, null); ");
        _output.Append("__agent.SetInterpreter(MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter()); ");
        _output.Append("return __agent; })())");
    }

    private string MapBuiltInClassName(string className)
    {
        return className switch
        {
            "LLMClient" => "MaldaLang.BuiltIns.LLMClientInstance",
            "OpenRouterClient" => "MaldaLang.BuiltIns.OpenRouterClientInstance",
            "LlamaCppClient" => "MaldaLang.BuiltIns.LlamaCppClientInstance",
            "LlamaEmbedder" => "MaldaLang.BuiltIns.LlamaEmbedderInstance",
            "OnnxModel" => "MaldaLang.BuiltIns.OnnxModelInstance",
            "Dense" => "MaldaLang.BuiltIns.DenseInstance",
            "Sequential" => "MaldaLang.BuiltIns.SequentialInstance",
            "Conv" => "MaldaLang.BuiltIns.ConvInstance",
            "Conv2D" => "MaldaLang.BuiltIns.Conv2DInstance",
            "MaxPool2D" => "MaldaLang.BuiltIns.MaxPool2DInstance",
            "AvgPool2D" => "MaldaLang.BuiltIns.AvgPool2DInstance",
            "Flatten" => "MaldaLang.BuiltIns.FlattenInstance",
            "BatchNorm2D" => "MaldaLang.BuiltIns.BatchNorm2DInstance",
            "Dropout2D" => "MaldaLang.BuiltIns.Dropout2DInstance",
            "GlobalAvgPool2D" => "MaldaLang.BuiltIns.GlobalAvgPool2DInstance",
            "GlobalMaxPool2D" => "MaldaLang.BuiltIns.GlobalMaxPool2DInstance",
            "Activation" => "MaldaLang.BuiltIns.ActivationInstance",
            "Embedding" => "MaldaLang.BuiltIns.EmbeddingInstance",
            "Rnn" => "MaldaLang.BuiltIns.RnnInstance",
            "LayerNorm" => "MaldaLang.BuiltIns.LayerNormInstance",
            "Attention" => "MaldaLang.BuiltIns.AttentionInstance",
            "Parallel" => "MaldaLang.BuiltIns.ParallelInstance",
            "Identity" => "MaldaLang.BuiltIns.IdentityInstance",
            "Conversation" => "MaldaLang.BuiltIns.ConversationInstance",
            "Tool" => "MaldaLang.BuiltIns.ToolInstance",
            "Agent" => "MaldaLang.BuiltIns.AgentInstance",
            "CodingAgent" => "MaldaLang.BuiltIns.CodingAgentInstance",
            "GitAgent" => "MaldaLang.BuiltIns.GitAgentInstance",
            "DevAgent" => "MaldaLang.BuiltIns.DevAgentInstance",
            "GraphMemory" => "MaldaLang.BuiltIns.GraphMemoryInstance",
            "VectorDB" => "MaldaLang.Interpreter.VectorDBInstance",
            "MALDACodingAgent" => "MaldaLang.BuiltIns.MALDACodingAgentInstance",
            "RestServer" => "MaldaLang.BuiltIns.RestServerInstance",
            "RestClient" => "MaldaLang.BuiltIns.RestClientInstance",
            "HttpServer" => "MaldaLang.BuiltIns.HttpServerInstance",
            "HTMLCache" => "MaldaLang.BuiltIns.HTMLCacheInstance",
            "MCPServer" => "MaldaLang.BuiltIns.MCPServerInstance",
            "MCPClient" => "MaldaLang.BuiltIns.MCPClientInstance",
            "ACPClient" => "MaldaLang.BuiltIns.ACP.ACPClientInstance",
            "ACPServer" => "MaldaLang.BuiltIns.ACP.ACPServerInstance",
            "ACPAgentTool" => "MaldaLang.BuiltIns.ACP.ACPAgentToolInstance",
            "LLMClientBridge" => "MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance",
            "LLMServer" => "MaldaLang.BuiltIns.LLMServerInstance",
            "SqlServerClient" => "MaldaLang.BuiltIns.SqlServerClientInstance",
            "PostgresClient" => "MaldaLang.BuiltIns.PostgresClientInstance",
            "SqliteClient" => "MaldaLang.BuiltIns.SqliteClientInstance",
            "SerialConnection" => "MaldaLang.BuiltIns.SerialConnectionInstance",
            "ArduinoConnection" => "MaldaLang.BuiltIns.ArduinoConnectionInstance",
            _ => className // User-defined classes use as-is
        };
    }
}
