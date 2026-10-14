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
    /// <summary>
    /// Emits <c>name__Typed</c>: the sync-twin body with typed parameters and return. Rejected
    /// (and dropped for the next pass) on the same markers as a sync twin.
    /// </summary>
    private void TranspileTypedTwin(FunctionDeclaration funcDecl, TypedTwinSignature signature)
    {
        var start = _output.Length;
        _output.AppendLine();
        WriteIndent();
        _output.Append("static ");
        _output.Append(GetClrTypeName(signature.Return));
        _output.Append(" ");
        _output.Append(GetTypedTwinName(funcDecl.Name));
        _output.Append("(");

        PushTypedScope();
        _currentTypedIntParams.Clear();
        _currentTypedDoubleParams.Clear();
        for (int i = 0; i < funcDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            var parameterType = signature.Parameters[i];
            RegisterTypedVariable(funcDecl.Parameters[i], parameterType);
            if (parameterType == TranspiledClrType.Int) _currentTypedIntParams.Add(funcDecl.Parameters[i]);
            if (parameterType == TranspiledClrType.Double) _currentTypedDoubleParams.Add(funcDecl.Parameters[i]);
            _output.Append(GetClrTypeName(parameterType));
            _output.Append(" ");
            _output.Append(EscapeIdentifier(funcDecl.Parameters[i]));
        }

        _output.Append(")");
        AppendComment(nameof(TranspileTypedTwin));
        _output.AppendLine();
        var previousCanAwait = _canAwait;
        _canAwait = false;
        _currentFunctionReturnType.Push(signature.Return);
        var previousLocals = EnterLocals(funcDecl, typed: true);
        TranspileFunctionBlock(funcDecl.Body, funcDecl.Name, funcDecl.Line, appendImplicitNullReturn: true);
        _activeLocals = previousLocals;
        _currentFunctionReturnType.Pop();
        PopTypedScope();
        _canAwait = previousCanAwait;
        _currentTypedIntParams.Clear();
        _currentTypedDoubleParams.Clear();

        var emitted = _output.ToString(start, _output.Length - start);
        if (emitted.Contains("await ", StringComparison.Ordinal) ||
            emitted.Contains("BlockOn(", StringComparison.Ordinal) ||
            emitted.Contains("GetAwaiter()", StringComparison.Ordinal) ||
            emitted.Contains(".Wait(", StringComparison.Ordinal) ||
            emitted.Contains(".Result", StringComparison.Ordinal))
        {
            _output.Length = start;
            _typedTwinRejected.Add(funcDecl.Name);
            _typedTwinRejectedThisPass = true;
        }
    }

    /// <summary>Makes the inferred locals of <paramref name="body"/> active; returns the previous ones.</summary>
    private NumericVariableInference.LocalResult EnterLocals(FunctionDeclaration body, bool typed)
    {
        var previous = _activeLocals;
        var locals = typed ? _typedLocals : _untypedLocals;
        _activeLocals = locals.TryGetValue(body, out var found) ? found : NumericVariableInference.LocalResult.Empty();
        return previous;
    }

    private string GetTypedTwinName(string functionName) => EscapeIdentifier(functionName) + "__Typed";

    /// <summary>
    /// True when a direct call can use <c>name__Typed</c>: the name is not shadowed by a
    /// block-scoped function and each argument already has its parameter's C# type (an int
    /// name or int literal for int, a typed double for double).
    /// </summary>
    private bool TryGetTypedTwinCall(string name, List<Expression> arguments, out TypedTwinSignature signature)
    {
        if (!_typedTwins.TryGetValue(name, out signature!) || signature.Parameters.Length != arguments.Count)
            return false;

        foreach (var frame in _localFunctionStack)
        {
            if (frame.ContainsKey(name))
                return false;
        }

        return TypedTwinArgumentsFit(signature, arguments);
    }

    /// <summary>
    /// True when <c>this.m(...)</c> or <c>Class.m(...)</c> can call <c>m__Typed</c> (see
    /// <see cref="ResolveTypedMethodKey"/>) with these arguments.
    /// </summary>
    private bool TryGetTypedMethodCall(FunctionCallExpression call, out TypedTwinSignature signature)
    {
        signature = null!;
        var key = ResolveTypedMethodKey(call, _currentClassName, _currentMemberIsStatic);
        return key != null &&
               _typedTwins.TryGetValue(key, out signature!) &&
               signature.Parameters.Length == call.Arguments.Count &&
               TypedTwinArgumentsFit(signature, call.Arguments);
    }

    private bool TypedTwinArgumentsFit(TypedTwinSignature signature, List<Expression> arguments)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var ok = signature.Parameters[i] switch
            {
                TranspiledClrType.Int => argument is LiteralExpression { Value: int } ||
                                         ResolveExpressionType(argument) == TranspiledClrType.Int,
                TranspiledClrType.Double => argument is not LiteralExpression { Value: int } &&
                                            IsDoubleTypedValue(argument),
                _ => true
            };
            if (!ok)
                return false;
        }

        return true;
    }

    /// <summary>
    /// A value that already is a C# double and certainly a MALDA float: resolved as Double and
    /// built only from float literals, inferred float names, typed float parameters and typed
    /// calls (an int-literal-only expression or a hinted call does not qualify).
    /// </summary>
    private bool IsDoubleTypedValue(Expression value)
    {
        if (ResolveExpressionType(value) != TranspiledClrType.Double)
            return false;

        var doubles = new HashSet<string>(_inferredNumerics.Doubles, StringComparer.Ordinal);
        doubles.UnionWith(_currentTypedDoubleParams);
        doubles.UnionWith(_activeLocals.Doubles);
        var ints = new HashSet<string>(_inferredNumerics.Ints, StringComparer.Ordinal);
        ints.UnionWith(_currentTypedIntParams);
        ints.UnionWith(_activeLocals.Ints);
        return NumericVariableInference.IsCertainlyDouble(value, doubles, ints, ResolveTypedCallKind);
    }

    private NumericVariableInference.Kind ResolveTypedCallKind(FunctionCallExpression call)
    {
        TypedTwinSignature? signature = null;
        if (call.Callee is IdentifierExpression id && TryGetTypedTwinCall(id.Name, call.Arguments, out var functionSignature))
            signature = functionSignature;
        else if (call.Callee is MemberAccessExpression && TryGetTypedMethodCall(call, out var methodSignature))
            signature = methodSignature;

        if (signature != null)
        {
            return signature.Return switch
            {
                TranspiledClrType.Int => NumericVariableInference.Kind.Int,
                TranspiledClrType.Double => NumericVariableInference.Kind.Double,
                _ => NumericVariableInference.Kind.None
            };
        }
        return NumericVariableInference.Kind.None;
    }

    /// <summary>A function or class method that may get a <c>__Typed</c> variant.</summary>
    private sealed record TypedTwinCandidate(FunctionDeclaration Function, string? ClassName, bool IsStatic);

    /// <summary>
    /// A call inside the program: the candidate it sits in (if any), its class context, the
    /// candidate a typed call would reach, and every candidate it gives call-site evidence for.
    /// </summary>
    private sealed record TypedCallSite(FunctionCallExpression Call, string? CallerKey, string? TypedTarget, List<string> EvidenceTargets);

    /// <summary>
    /// Chooses int/double parameter and return types for sync-twin functions and class methods.
    /// Functions are keyed by name, methods by <c>Class.method</c>.
    /// <para>
    /// Optimistic fixpoint, ints first, then doubles: a parameter keeps a type while some call from
    /// outside the function passes a value certainly of that type (in the caller's scope, where the
    /// caller's own typed parameters count) and every assignment to it in the body stores one. Calls
    /// with other arguments keep using the untyped twin. The return keeps
    /// a type while every <c>return</c> (and an implicit last-expression return) yields one and the
    /// body cannot fall off its end. Calls count through the same assumptions, so recursion works.
    /// </para>
    /// <para>
    /// A method call is a typed call only as <c>this.m(...)</c> (resolved to the one declaration
    /// every instance runs, see <see cref="ResolveTypedMethodKey"/>) or <c>Class.m(...)</c> for a
    /// static method. Other receivers reach the variant through the direct-dispatch type test,
    /// whose result stays boxed, so they count as evidence only.
    /// </para>
    /// <para>
    /// Soundness does not depend on the call-site rule: the typed variant is only called where each
    /// argument already has the parameter's type, and a typed return is only claimed for such a
    /// call, whose result the rules prove to be of that type.
    /// </para>
    /// </summary>
    private Dictionary<string, TypedTwinSignature> ComputeTypedTwins(List<FunctionDeclaration> functions, List<Statement> program)
    {
        var result = new Dictionary<string, TypedTwinSignature>(StringComparer.Ordinal);
        _typedStaticReceivers = new HashSet<string>(StringComparer.Ordinal);
        _untypedLocals = new Dictionary<FunctionDeclaration, NumericVariableInference.LocalResult>(ReferenceEqualityComparer.Instance);
        _typedLocals = new Dictionary<FunctionDeclaration, NumericVariableInference.LocalResult>(ReferenceEqualityComparer.Instance);
        if (!TypedTranspileEnabled)
            return result;

        // Top-level names start optimistic (all ints) and shrink with the variants below, so
        // `total = total + sumTo(j)` keeps `total` an int when sumTo__Typed returns one.
        var globals = NumericVariableInference.CollectGlobals(program);
        _inferredNumerics = new NumericVariableInference.Result(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(globals.Names, StringComparer.Ordinal));

        var topLevel = functions.Where(f => !ShaderFunction.IsMarked(f)).ToList();
        var byKey = new Dictionary<string, TypedTwinCandidate>(StringComparer.Ordinal);
        // Every body whose locals get typed, by the key its call sites use: functions by name
        // (only when the name is unique), methods by Class.method, constructors by Class..ctor.
        var bodyByKey = new Dictionary<string, FunctionDeclaration>(StringComparer.Ordinal);
        var allBodies = new List<FunctionDeclaration>(topLevel);
        foreach (var group in topLevel.GroupBy(f => f.Name, StringComparer.Ordinal))
        {
            if (group.Count() == 1)
                bodyByKey[group.Key] = group.First();
        }
        foreach (var classDecl in _classDeclarations.Values)
        {
            foreach (var member in classDecl.Members)
            {
                if (member.Value is FunctionDeclaration body && member.Type is MemberType.Method or MemberType.Constructor)
                {
                    allBodies.Add(body);
                    bodyByKey[classDecl.Name + (member.Type == MemberType.Constructor ? "..ctor" : "." + member.Name)] = body;
                }
            }
        }

        if (_syncTwinFunctions != null && !ProfilingEnabled)
        {
            foreach (var f in topLevel)
            {
                if (_syncTwinFunctions.Contains(f.Name) && !_typedTwinRejected.Contains(f.Name))
                    byKey[f.Name] = new TypedTwinCandidate(f, null, false);
            }
        }

        foreach (var classDecl in _classDeclarations.Values)
        {
            foreach (var member in classDecl.Members)
            {
                if (!ProfilingEnabled &&
                    member.Type == MemberType.Method && member.Value is FunctionDeclaration method &&
                    IsTypedMethodCandidate(classDecl, member, method))
                {
                    byKey[classDecl.Name + "." + member.Name] = new TypedTwinCandidate(method, classDecl.Name, member.IsStatic);
                }
            }
        }
        // `Class.m(...)` is a static call only where nothing else in the program is named Class.
        var bound = new HashSet<string>(StringComparer.Ordinal);
        foreach (var statement in program)
        {
            if (statement is not ClassDeclaration)
                bound.UnionWith(NumericVariableInference.BindingsWithin(statement, ignoreTypeReferences: true));
        }
        foreach (var classDecl in _classDeclarations.Values)
        {
            foreach (var member in classDecl.Members)
            {
                if (member.Value is FunctionDeclaration body)
                {
                    bound.UnionWith(body.Parameters);
                    bound.UnionWith(NumericVariableInference.BindingsWithin(body.Body, ignoreTypeReferences: true));
                }
                else if (member.Value is Expression initializer)
                {
                    bound.UnionWith(NumericVariableInference.BindingsWithin(initializer, ignoreTypeReferences: true));
                }
            }
        }
        foreach (var className in _classDeclarations.Keys)
        {
            if (!bound.Contains(className))
                _typedStaticReceivers.Add(className);
        }

        // Call sites with the candidate they sit in and their class context (null: top-level code,
        // field initializers, constructors).
        var sites = new List<TypedCallSite>();
        var typedTargetOf = new Dictionary<FunctionCallExpression, string>(ReferenceEqualityComparer.Instance);
        var nestedFunctionNames = new HashSet<string>(StringComparer.Ordinal);
        void Collect(object root, string? callerKey, string? className, bool isStatic)
        {
            foreach (var node in AstNodeWalker.Descendants(root))
            {
                if (node is FunctionCallExpression { Callee: IdentifierExpression or MemberAccessExpression } call)
                {
                    string? target = null;
                    var evidence = new List<string>();
                    if (call.Callee is IdentifierExpression id)
                    {
                        if (byKey.ContainsKey(id.Name))
                            target = id.Name;
                    }
                    else
                    {
                        var key = ResolveTypedMethodKey(call, className, isStatic);
                        if (key != null && byKey.TryGetValue(key, out var candidate) &&
                            candidate.Function.Parameters.Count == call.Arguments.Count)
                        {
                            target = key;
                        }
                        else if (call.Callee is MemberAccessExpression { Object: { } receiver } access)
                        {
                            foreach (var owner in FindDirectMethodTargets(receiver, access.Member, call.Arguments.Count))
                                evidence.Add(owner + "." + access.Member);
                        }
                    }
                    if (target != null)
                    {
                        evidence.Add(target);
                        typedTargetOf[call] = target;
                    }
                    sites.Add(new TypedCallSite(call, callerKey, target, evidence));
                }
                else if (node is FunctionDeclaration nested)
                {
                    nestedFunctionNames.Add(nested.Name);
                }
            }
        }

        foreach (var f in topLevel)
            Collect(f.Body, f.Name, null, false);
        foreach (var classDecl in _classDeclarations.Values)
        {
            foreach (var member in classDecl.Members)
            {
                if (member.Value is FunctionDeclaration body)
                {
                    var callerKey = classDecl.Name + (member.Type == MemberType.Constructor ? "..ctor" : "." + member.Name);
                    Collect(body.Body, callerKey, classDecl.Name, member.IsStatic);
                }
                else if (member.Value is Expression initializer)
                {
                    Collect(initializer, null, null, false);
                }
            }
        }
        foreach (var statement in program)
        {
            if (statement is FunctionDeclaration or ClassDeclaration)
                continue;
            Collect(statement, null, null, false);
        }

        // A block-scoped function shadows a top-level one of the same name somewhere.
        foreach (var name in nestedFunctionNames)
            byKey.Remove(name);
        foreach (var site in sites)
        {
            if (site.Call.Callee is IdentifierExpression id && byKey.TryGetValue(id.Name, out var target) &&
                target.ClassName == null && site.Call.Arguments.Count != target.Function.Parameters.Count)
            {
                byKey.Remove(id.Name);
            }
        }

        // Per-candidate facts: which parameters may be typed at all, stores and bumps per parameter,
        // and the body's own returns (not those of nested lambdas/functions).
        var parameterKinds = new Dictionary<string, TranspiledClrType[]>(StringComparer.Ordinal);
        var parameterEligible = new Dictionary<string, bool[]>(StringComparer.Ordinal);
        var parameterBumped = new Dictionary<string, bool[]>(StringComparer.Ordinal);
        var returnKinds = new Dictionary<string, TranspiledClrType>(StringComparer.Ordinal);
        var returnEligible = new Dictionary<string, bool>(StringComparer.Ordinal);
        var stores = new Dictionary<string, List<AssignmentStatement>[]>(StringComparer.Ordinal);
        var returns = new Dictionary<string, List<Expression>?>(StringComparer.Ordinal);
        foreach (var (key, candidate) in byKey)
        {
            var f = candidate.Function;
            var bodyBindings = NumericVariableInference.BindingsWithin(f.Body);
            var count = f.Parameters.Count;
            var kinds = new TranspiledClrType[count];
            var eligible = new bool[count];
            var bumped = new bool[count];
            var paramStores = new List<AssignmentStatement>[count];
            var nodes = AstNodeWalker.Descendants(f.Body).ToList();
            for (int i = 0; i < count; i++)
            {
                var parameter = f.Parameters[i];
                var hint = f.ParameterTypeHints != null && i < f.ParameterTypeHints.Count ? f.ParameterTypeHints[i] : null;
                var hinted = ResolveTranspiledTypeHint(hint);
                eligible[i] = string.IsNullOrWhiteSpace(hint) &&
                              !bodyBindings.Contains(parameter) &&
                              f.Parameters.Count(p => p == parameter) == 1;
                kinds[i] = eligible[i] ? TranspiledClrType.Int : hinted;
                paramStores[i] = nodes.OfType<AssignmentStatement>()
                    .Where(a => a.Target is IdentifierExpression t && t.Name == parameter).ToList();
                bumped[i] = nodes.Any(n =>
                    n is PostfixExpression { Left: IdentifierExpression pt } && pt.Name == parameter ||
                    n is UnaryExpression { Operator: TokenType.Increment or TokenType.Decrement, Right: IdentifierExpression ut } && ut.Name == parameter);
            }

            parameterKinds[key] = kinds;
            parameterEligible[key] = eligible;
            parameterBumped[key] = bumped;
            stores[key] = paramStores;
            returns[key] = CollectOwnReturnValues(f);
            returnEligible[key] = string.IsNullOrWhiteSpace(f.ReturnType) && returns[key] != null;
            returnKinds[key] = returnEligible[key] ? TranspiledClrType.Int : ResolveTranspiledTypeHint(f.ReturnType);
        }

        // The names known in a body: top-level inferred variables, its typed parameters (in the
        // typed variant of a candidate) and its own inferred locals. Cached until an assumption
        // changes.
        var scopeCache = new Dictionary<string, (HashSet<string> Ints, HashSet<string> Doubles)>(StringComparer.Ordinal);
        (HashSet<string> Ints, HashSet<string> Doubles) ScopeOf(string? callerKey)
        {
            if (callerKey != null && scopeCache.TryGetValue(callerKey, out var cached))
                return cached;

            var ints = new HashSet<string>(_inferredNumerics.Ints, StringComparer.Ordinal);
            var doubles = new HashSet<string>(_inferredNumerics.Doubles, StringComparer.Ordinal);
            if (callerKey == null)
                return (ints, doubles);

            parameterKinds.TryGetValue(callerKey, out var kinds);
            if (bodyByKey.TryGetValue(callerKey, out var body))
            {
                var locals = LocalsOf(body, kinds);
                ints.UnionWith(locals.Ints);
                doubles.UnionWith(locals.Doubles);
            }
            if (kinds != null)
            {
                var parameters = byKey[callerKey].Function.Parameters;
                for (int i = 0; i < kinds.Length; i++)
                {
                    if (kinds[i] == TranspiledClrType.Int) ints.Add(parameters[i]);
                    else if (kinds[i] == TranspiledClrType.Double) doubles.Add(parameters[i]);
                }
            }
            scopeCache[callerKey] = (ints, doubles);
            return (ints, doubles);
        }

        // Locals of a body; kinds are the typed parameters of its __Typed variant, or null for the
        // untyped versions (where every parameter is an object).
        NumericVariableInference.LocalResult LocalsOf(FunctionDeclaration body, TranspiledClrType[]? kinds)
        {
            var ints = new HashSet<string>(_inferredNumerics.Ints, StringComparer.Ordinal);
            var doubles = new HashSet<string>(_inferredNumerics.Doubles, StringComparer.Ordinal);
            for (int i = 0; kinds != null && i < kinds.Length; i++)
            {
                if (kinds[i] == TranspiledClrType.Int) ints.Add(body.Parameters[i]);
                else if (kinds[i] == TranspiledClrType.Double) doubles.Add(body.Parameters[i]);
            }
            return NumericVariableInference.InferLocals(body.Body, body.Parameters, ints, doubles, MakeResolver);
        }

        NumericVariableInference.CallKindResolver? resolver = null;
        bool ArgumentFits(Expression argument, TranspiledClrType kind, ISet<string> ints, ISet<string> doubles) => kind switch
        {
            TranspiledClrType.Int => NumericVariableInference.IsCertainlyInt(argument, ints, resolver),
            TranspiledClrType.Double => NumericVariableInference.IsCertainlyDouble(argument, doubles, ints, resolver),
            _ => true
        };

        // Calls are resolved in the scope they sit in; the resolver is rebuilt per scope.
        NumericVariableInference.CallKindResolver MakeResolver(ISet<string> ints, ISet<string> doubles)
        {
            NumericVariableInference.CallKindResolver self = null!;
            self = call =>
            {
                if (!typedTargetOf.TryGetValue(call, out var target) || !byKey.ContainsKey(target))
                    return NumericVariableInference.Kind.None;
                var kinds = parameterKinds[target];
                var previous = resolver;
                resolver = self;
                try
                {
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        if (!ArgumentFits(call.Arguments[i], kinds[i], ints, doubles))
                            return NumericVariableInference.Kind.None;
                    }
                }
                finally
                {
                    resolver = previous;
                }
                return returnKinds[target] switch
                {
                    TranspiledClrType.Int => NumericVariableInference.Kind.Int,
                    TranspiledClrType.Double => NumericVariableInference.Kind.Double,
                    _ => NumericVariableInference.Kind.None
                };
            };
            return self;
        }

        void Settle(TranspiledClrType kind)
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var (key, _) in byKey)
                {
                    var kinds = parameterKinds[key];
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        if (kinds[i] != kind || !parameterEligible[key][i])
                            continue;

                        // Evidence: some call from outside the body passes a value of this type.
                        // Other calls simply keep using the untyped version.
                        var ok = kind != TranspiledClrType.Double || !parameterBumped[key][i];
                        if (ok)
                        {
                            ok = false;
                            foreach (var site in sites)
                            {
                                if (site.CallerKey == key || !site.EvidenceTargets.Contains(key)) continue;
                                var (ints, doubles) = ScopeOf(site.CallerKey);
                                resolver = MakeResolver(ints, doubles);
                                if (ArgumentFits(site.Call.Arguments[i], kind, ints, doubles))
                                {
                                    ok = true;
                                    break;
                                }
                            }
                        }

                        if (ok)
                        {
                            var (ints, doubles) = ScopeOf(key);
                            var calls = MakeResolver(ints, doubles);
                            ok = stores[key][i].All(a => kind == TranspiledClrType.Int
                                ? NumericVariableInference.IsIntStore(a, ints, calls)
                                : NumericVariableInference.IsDoubleStore(a, doubles, ints, calls));
                        }

                        if (!ok)
                        {
                            kinds[i] = TranspiledClrType.Object;
                            changed = true;
                            scopeCache.Clear();
                        }
                    }

                    if (returnKinds[key] == kind && returnEligible[key])
                    {
                        var (ints, doubles) = ScopeOf(key);
                        var calls = MakeResolver(ints, doubles);
                        var ok = returns[key]!.All(v => kind == TranspiledClrType.Int
                            ? NumericVariableInference.IsCertainlyInt(v, ints, calls)
                            : NumericVariableInference.IsCertainlyDouble(v, doubles, ints, calls));
                        if (!ok)
                        {
                            returnKinds[key] = TranspiledClrType.Object;
                            changed = true;
                            scopeCache.Clear();
                        }
                    }
                }

                // Top-level names, with calls resolved in top-level scope (they are bound nowhere
                // else, so that scope is safe for stores inside bodies too).
                var shrinking = kind == TranspiledClrType.Int ? _inferredNumerics.Ints : _inferredNumerics.Doubles;
                var globalKind = kind == TranspiledClrType.Int ? NumericVariableInference.Kind.Int : NumericVariableInference.Kind.Double;
                foreach (var name in shrinking.ToList())
                {
                    var (ints, doubles) = ScopeOf(null);
                    if (!NumericVariableInference.GlobalQualifies(globals, name, globalKind, ints, doubles, MakeResolver(ints, doubles)))
                    {
                        shrinking.Remove(name);
                        changed = true;
                        scopeCache.Clear();
                    }
                }
            }
            while (changed);
        }

        Settle(TranspiledClrType.Int);

        // Whatever did not stay int gets a chance as a double (ints are now fixed).
        foreach (var (key, _) in byKey)
        {
            var kinds = parameterKinds[key];
            for (int i = 0; i < kinds.Length; i++)
            {
                if (parameterEligible[key][i] && kinds[i] == TranspiledClrType.Object)
                    kinds[i] = TranspiledClrType.Double;
            }
            if (returnEligible[key] && returnKinds[key] == TranspiledClrType.Object)
                returnKinds[key] = TranspiledClrType.Double;
        }
        _inferredNumerics.Doubles.UnionWith(globals.DoubleCandidates(_inferredNumerics.Ints));
        scopeCache.Clear();
        Settle(TranspiledClrType.Double);

        // A variant pays off only with a typed return or a typed parameter that the body does
        // arithmetic or comparisons on; otherwise it would just duplicate the untyped version.
        foreach (var (key, candidate) in byKey)
        {
            var kinds = parameterKinds[key];
            var worthIt = returnEligible[key] && returnKinds[key] != TranspiledClrType.Object;
            if (!worthIt)
            {
                var numericNames = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < kinds.Length; i++)
                {
                    if (parameterEligible[key][i] && kinds[i] != TranspiledClrType.Object)
                        numericNames.Add(candidate.Function.Parameters[i]);
                }
                worthIt = numericNames.Count > 0 && UsesNamesNumerically(candidate.Function.Body, numericNames);
            }
            if (worthIt)
                result[key] = new TypedTwinSignature(kinds, returnKinds[key]);
        }

        foreach (var (key, signature) in result)
            _typedLocals[byKey[key].Function] = LocalsOf(byKey[key].Function, signature.Parameters);
        foreach (var body in allBodies)
            _untypedLocals[body] = LocalsOf(body, null);

        return result;
    }

    /// <summary>
    /// A class method that may get <c>m__Typed</c>: plain (no decorators or budget), the only
    /// member with its name in the class, and no member anywhere already named like the variant.
    /// </summary>
    private bool IsTypedMethodCandidate(ClassDeclaration classDecl, ClassMember member, FunctionDeclaration method)
    {
        var twinName = member.Name + "__Typed";
        return (method.Decorators == null || method.Decorators.Count == 0) &&
               (method.ParameterDecorators == null || method.ParameterDecorators.All(d => d == null)) &&
               DeclarationBounds.TryGetResourceBudget(method) is not { HasAnyBound: true } &&
               classDecl.Members.Count(m => string.Equals(m.Name, member.Name, StringComparison.Ordinal)) == 1 &&
               !_classDeclarations.Values.Any(c => c.Members.Any(m => string.Equals(m.Name, twinName, StringComparison.Ordinal)));
    }

    /// <summary>
    /// The <c>Class.method</c> key a method call statically reaches, or null: <c>this.m(...)</c> in
    /// an instance member of <paramref name="className"/> when the nearest declaration of
    /// <c>m</c> at or above it is an instance method that no subclass redeclares (so every
    /// instance runs that one), or <c>Class.m(...)</c> for a static method of a class nothing else
    /// is named after. A private method only counts from inside its own class.
    /// </summary>
    private string? ResolveTypedMethodKey(FunctionCallExpression call, string? className, bool isStatic)
    {
        // Member names the interpreter or this transpiler route to built-ins (array/string
        // methods, server and console helpers) keep their existing handling.
        if (call.Callee is not MemberAccessExpression access ||
            IsBuiltInFunction(access.Member) ||
            IsStringExtensionMethod(access.Member) ||
            BuiltInMemberMethodNames.Contains(access.Member))
        {
            return null;
        }

        if (access.Object is ThisExpression)
        {
            if (className == null || isStatic)
                return null;

            string? owner = null;
            ClassMember? found = null;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (var current = className;
                 current != null && visited.Add(current) && _classDeclarations.TryGetValue(current, out var decl);
                 current = decl.Superclass)
            {
                found = decl.Members.FirstOrDefault(m => string.Equals(m.Name, access.Member, StringComparison.Ordinal));
                if (found != null)
                {
                    owner = current;
                    break;
                }
            }

            if (found == null || found.Type != MemberType.Method || found.IsStatic ||
                (found.Access == AccessModifier.Private && owner != className))
            {
                return null;
            }

            var redeclared = _classDeclarations.Values.Any(other =>
                InheritsFrom(other, className) &&
                other.Members.Any(m => string.Equals(m.Name, access.Member, StringComparison.Ordinal)));
            return redeclared ? null : owner + "." + access.Member;
        }

        if (access.Object is IdentifierExpression receiver &&
            receiver.Name is not ("ui" or "AnsiConsole") &&
            _typedStaticReceivers.Contains(receiver.Name) &&
            _classDeclarations.TryGetValue(receiver.Name, out var receiverDecl))
        {
            var method = receiverDecl.Members.FirstOrDefault(m => string.Equals(m.Name, access.Member, StringComparison.Ordinal));
            if (method is { Type: MemberType.Method, IsStatic: true } &&
                (method.Access != AccessModifier.Private || receiver.Name == className))
            {
                return receiver.Name + "." + access.Member;
            }
        }

        return null;
    }

    private static bool UsesNamesNumerically(BlockStatement body, HashSet<string> names)
    {
        static bool IsName(Expression e, HashSet<string> n) => e is IdentifierExpression id && n.Contains(id.Name);
        return AstNodeWalker.Descendants(body).Any(node => node switch
        {
            // `"label" + n` is a concatenation, not arithmetic.
            BinaryExpression { Operator: TokenType.Plus } concat when
                concat.Left is LiteralExpression { Value: string } || concat.Right is LiteralExpression { Value: string } => false,
            BinaryExpression b when b.Operator is TokenType.Plus or TokenType.Minus or TokenType.Multiply or
                TokenType.Divide or TokenType.Modulo or TokenType.LessThan or TokenType.LessThanOrEqual or
                TokenType.GreaterThan or TokenType.GreaterThanOrEqual or TokenType.Equal or TokenType.NotEqual =>
                IsName(b.Left, names) || IsName(b.Right, names),
            UnaryExpression u => IsName(u.Right, names),
            PostfixExpression p => IsName(p.Left, names),
            _ => false
        });
    }

    /// <summary>
    /// The values a function body returns, including the implicit last-expression return, or null
    /// when it can fall off its end (an implicit <c>null</c>) or has a bare <c>return;</c>.
    /// </summary>
    private static List<Expression>? CollectOwnReturnValues(FunctionDeclaration function)
    {
        var values = new List<Expression>();
        foreach (var node in AstNodeWalker.Descendants(function.Body, n => n is not LambdaExpression and not FunctionDeclaration))
        {
            if (node is ReturnStatement ret)
            {
                if (ret.Value == null)
                    return null;
                values.Add(ret.Value);
            }
        }

        var statements = function.Body.Statements;
        if (statements.Count > 0 && statements[^1] is ExpressionStatement last)
        {
            values.Add(last.Expression);
        }
        else if (!BlockDefinitelyReturns(function.Body))
        {
            return null;
        }

        return values;
    }

    /// <summary>
    /// Top-level functions that may get a synchronous twin. The async method stays the
    /// public shape (delegates, reflection, spawn, routes); the twin only serves direct calls,
    /// which otherwise pay an async state machine and a Task per call.
    /// </summary>
    private HashSet<string> ComputeSyncTwinCandidates(List<FunctionDeclaration> functions, List<ClassDeclaration> classes)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        if (ProfilingEnabled)
        {
            return candidates;
        }

        // A class method with the same name would win C# name lookup inside that class.
        var memberNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var classDecl in classes)
        {
            foreach (var member in classDecl.Members)
            {
                memberNames.Add(member.Name);
            }
        }

        var counts = functions
            .GroupBy(f => f.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var funcDecl in functions)
        {
            if (ShaderFunction.IsMarked(funcDecl) ||
                counts[funcDecl.Name] != 1 ||
                memberNames.Contains(funcDecl.Name) ||
                (funcDecl.Decorators != null && funcDecl.Decorators.Count > 0) ||
                (funcDecl.ParameterDecorators != null && funcDecl.ParameterDecorators.Any(d => d != null)) ||
                DeclarationBounds.TryGetResourceBudget(funcDecl) is { HasAnyBound: true })
            {
                continue;
            }

            candidates.Add(funcDecl.Name);
        }

        _syncTwinCallers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var funcDecl in functions)
        {
            foreach (var call in AstNodeWalker.Descendants(funcDecl.Body).OfType<FunctionCallExpression>())
            {
                if (call.Callee is IdentifierExpression callee && candidates.Contains(callee.Name))
                {
                    if (!_syncTwinCallers.TryGetValue(callee.Name, out var callers))
                    {
                        _syncTwinCallers[callee.Name] = callers = new HashSet<string>(StringComparer.Ordinal);
                    }
                    callers.Add(funcDecl.Name);
                }
            }
        }

        return candidates;
    }

    /// <summary>
    /// Emits <c>name__Sync</c> with the same body under <c>_canAwait = false</c>. If the body
    /// still needs to wait on anything (an await, a sync-over-async BlockOn, a defer epilogue),
    /// the twin is discarded and the function is rejected for the next pass.
    /// </summary>
    private void TranspileSyncTwin(FunctionDeclaration funcDecl, TranspiledClrType returnType)
    {
        var start = _output.Length;
        _output.AppendLine();
        WriteIndent();
        _output.Append("static ");
        _output.Append(GetClrTypeName(returnType));
        _output.Append(" ");
        _output.Append(GetSyncTwinName(funcDecl.Name));
        _output.Append("(");

        PushTypedScope();
        for (int i = 0; i < funcDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            var parameterType = (funcDecl.ParameterTypeHints != null && i < funcDecl.ParameterTypeHints.Count)
                ? ResolveTranspiledTypeHint(funcDecl.ParameterTypeHints[i])
                : TranspiledClrType.Object;
            RegisterTypedVariable(funcDecl.Parameters[i], parameterType);
            _output.Append(GetClrTypeName(parameterType));
            _output.Append(" ");
            _output.Append(EscapeIdentifier(funcDecl.Parameters[i]));
        }

        _output.Append(")");
        AppendComment(nameof(TranspileSyncTwin));
        _output.AppendLine();
        var previousCanAwait = _canAwait;
        _canAwait = false;
        _currentFunctionReturnType.Push(returnType);
        var previousLocals = EnterLocals(funcDecl, typed: false);
        TranspileFunctionBlock(funcDecl.Body, funcDecl.Name, funcDecl.Line, appendImplicitNullReturn: true);
        _activeLocals = previousLocals;
        _currentFunctionReturnType.Pop();
        PopTypedScope();
        _canAwait = previousCanAwait;

        var emitted = _output.ToString(start, _output.Length - start);
        if (emitted.Contains("await ", StringComparison.Ordinal) ||
            emitted.Contains("BlockOn(", StringComparison.Ordinal) ||
            emitted.Contains("GetAwaiter()", StringComparison.Ordinal) ||
            emitted.Contains(".Wait(", StringComparison.Ordinal) ||
            emitted.Contains(".Result", StringComparison.Ordinal))
        {
            _output.Length = start;
            _syncTwinRejected.Add(funcDecl.Name);
        }
    }

    private string GetSyncTwinName(string functionName) => EscapeIdentifier(functionName) + "__Sync";

    private void RejectSyncTwinsWithCallers(IEnumerable<string> rejected)
    {
        var pending = new Queue<string>(rejected);
        while (pending.Count > 0)
        {
            var name = pending.Dequeue();
            if (!_syncTwinFunctions!.Remove(name))
            {
                continue;
            }

            if (_syncTwinCallers.TryGetValue(name, out var callers))
            {
                foreach (var caller in callers)
                {
                    if (_syncTwinFunctions.Contains(caller))
                    {
                        pending.Enqueue(caller);
                    }
                }
            }
        }
    }

    /// <summary>
    /// True when a direct call to <paramref name="name"/> resolves to a top-level function that
    /// has a synchronous twin (not shadowed by a block-scoped function of the same name).
    /// </summary>
    private bool HasSyncTwin(string name)
    {
        if (_syncTwinFunctions == null || !_syncTwinFunctions.Contains(name))
        {
            return false;
        }

        foreach (var frame in _localFunctionStack)
        {
            if (frame.ContainsKey(name))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Emits <c>m__Typed</c> next to a class method: the same body with int/double parameters and
    /// return, same access and static-ness. Methods are already synchronous, so nothing can make
    /// the variant invalid.
    /// </summary>
    private void TranspileTypedMethodTwin(ClassMember member, FunctionDeclaration func, TypedTwinSignature signature)
    {
        WriteIndent();
        _output.Append(member.Access == AccessModifier.Private ? "private " : "public ");
        if (member.IsStatic)
            _output.Append("static ");
        _output.Append(GetClrTypeName(signature.Return));
        _output.Append(" ");
        _output.Append(GetTypedTwinName(member.Name));
        _output.Append("(");

        PushTypedScope();
        _currentTypedIntParams.Clear();
        _currentTypedDoubleParams.Clear();
        for (int i = 0; i < func.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            var parameterType = signature.Parameters[i];
            RegisterTypedVariable(func.Parameters[i], parameterType);
            if (parameterType == TranspiledClrType.Int) _currentTypedIntParams.Add(func.Parameters[i]);
            if (parameterType == TranspiledClrType.Double) _currentTypedDoubleParams.Add(func.Parameters[i]);
            _output.Append(GetClrTypeName(parameterType));
            _output.Append(" ");
            _output.Append(EscapeIdentifier(func.Parameters[i]));
        }

        _output.Append(")");
        AppendComment(nameof(TranspileTypedMethodTwin));
        _output.AppendLine();
        var previousCanAwait = _canAwait;
        _canAwait = false;
        _currentFunctionReturnType.Push(signature.Return);
        var previousLocals = EnterLocals(func, typed: true);
        TranspileFunctionBlock(func.Body, member.Name, func.Line, appendImplicitNullReturn: true);
        _activeLocals = previousLocals;
        _currentFunctionReturnType.Pop();
        PopTypedScope();
        _canAwait = previousCanAwait;
        _currentTypedIntParams.Clear();
        _currentTypedDoubleParams.Clear();
    }

    /// <summary>
    /// Arguments of a call to a <c>__Typed</c> variant: typed ones as they are (the caller checked
    /// their C# type), the rest as a method call would pass them.
    /// </summary>
    private void EmitTypedMethodArguments(List<Expression> arguments, TypedTwinSignature signature)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            if (signature.Parameters[i] is TranspiledClrType.Int or TranspiledClrType.Double)
                TranspileExpression(arguments[i]);
            else
                EmitObjectMethodArgument(arguments[i]);
        }
    }
}
