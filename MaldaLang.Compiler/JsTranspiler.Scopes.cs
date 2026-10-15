// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

public partial class JsTranspiler
{
    private void ResetLocalScopes()
    {
        _localScopes.Clear();
        _localScopes.Push(new HashSet<string>(StringComparer.Ordinal));
    }

    private void CopyLocalScopesFrom(JsTranspiler other)
    {
        _localScopes.Clear();
        foreach (var scope in other._localScopes.Reverse())
        {
            _localScopes.Push(new HashSet<string>(scope, StringComparer.Ordinal));
        }
    }

    private void ResetConstScopes()
    {
        _moduleConstNames.Clear();
        _constScopeStack.Clear();
        _constScopeStack.Push(new HashSet<string>(StringComparer.Ordinal));
    }

    private void CopyConstScopesFrom(JsTranspiler other)
    {
        _moduleConstNames.Clear();
        foreach (var name in other._moduleConstNames)
            _moduleConstNames.Add(name);
        _constScopeStack.Clear();
        foreach (var scope in other._constScopeStack.Reverse())
            _constScopeStack.Push(new HashSet<string>(scope, StringComparer.Ordinal));
    }

    private void PushConstScope()
    {
        _constScopeStack.Push(new HashSet<string>(StringComparer.Ordinal));
    }

    private void PopConstScope()
    {
        if (_constScopeStack.Count > 1)
            _constScopeStack.Pop();
    }

    private void RegisterConstBinding(string name)
    {
        if (string.IsNullOrEmpty(name))
            return;
        if (_constScopeStack.Count > 0)
            _constScopeStack.Peek().Add(name);
        else
            _moduleConstNames.Add(name);
    }

    private bool IsConstIdentifierPattern(string name)
    {
        if (_variantConstructorArities.ContainsKey(name))
            return false;
        if (_moduleConstNames.Contains(name))
            return true;
        foreach (var scope in _constScopeStack)
        {
            if (scope.Contains(name))
                return true;
        }
        return false;
    }

    private void PushLocalScope()
    {
        _localScopes.Push(new HashSet<string>(StringComparer.Ordinal));
    }

    private void PopLocalScope()
    {
        if (_localScopes.Count > 1)
        {
            _localScopes.Pop();
        }
    }

    private void DeclareLocal(string name)
    {
        if (string.IsNullOrEmpty(name) || _localScopes.Count == 0)
        {
            return;
        }

        _localScopes.Peek().Add(name);
    }

    private bool IsLocalName(string name)
    {
        foreach (var scope in _localScopes)
        {
            if (scope.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    private string TranspileIdentifierReference(string name)
    {
        if (_rewriteImplicitMembers
            && _currentClassName != null
            && !IsLocalName(name)
            && TryFindClassField(_currentClassName, name, out var isStatic))
        {
            if (isStatic)
                return $"{EscapeIdentifier(_currentClassName)}.{EscapeIdentifier(name)}";
            if (!_currentMemberIsStatic)
                return $"this.{EscapeIdentifier(name)}";
        }

        if (_rewriteImplicitMembers
            && _currentActor != null
            && !IsLocalName(name)
            && TryFindActorMember(name, out var actorMember))
        {
            if (actorMember.IsStatic)
                return $"{EscapeIdentifier(_currentActor.Name)}.{EscapeIdentifier(name)}";
            if (!_currentMemberIsStatic)
                return $"this.{EscapeIdentifier(name)}";
        }

        return EscapeIdentifier(name);
    }

    /// <summary>Field or method of the actor being emitted (constructors excluded).</summary>
    private bool TryFindActorMember(string name, out ClassMember member)
    {
        member = _currentActor?.Members.FirstOrDefault(m =>
            m.Type is MemberType.Field or MemberType.Method &&
            string.Equals(m.Name, name, StringComparison.Ordinal))!;
        return member != null;
    }

    private bool TryFindClassField(string className, string fieldName, out bool isStatic)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = className;
        while (!string.IsNullOrEmpty(current) && seen.Add(current))
        {
            if (!_classDeclarations.TryGetValue(current, out var decl))
                break;
            foreach (var member in decl.Members)
            {
                if (member.Type == MemberType.Field
                    && string.Equals(member.Name, fieldName, StringComparison.Ordinal))
                {
                    isStatic = member.IsStatic;
                    return true;
                }
            }

            current = decl.Superclass ?? string.Empty;
        }

        isStatic = false;
        return false;
    }

    private bool IsGameLoopStop(MemberAccessExpression stopCall) =>
        stopCall.Object is IdentifierExpression { Name: "game" } gameName &&
        IsUnshadowedRuntimeModule(gameName.Name);

    private bool IsUnshadowedRuntimeModule(string name) =>
        JsRuntimeModules.Contains(name) && !IsLocalName(name);

    /// <summary>
    /// Emits a loop whose <c>continue</c> is a plain JS <c>continue</c>, so it does not also
    /// emit the increment of an enclosing desugared <c>for</c>.
    /// </summary>
    private void WithOwnContinue(Action emitLoop)
    {
        _desugaredForLoopIncrements.Push(null);
        try
        {
            emitLoop();
        }
        finally
        {
            _desugaredForLoopIncrements.Pop();
        }
    }

    private void InLocalScope(IEnumerable<string> names, Action action)
    {
        PushLocalScope();
        PushConstScope();
        foreach (var name in names)
        {
            DeclareLocal(name);
        }

        try
        {
            action();
        }
        finally
        {
            PopConstScope();
            PopLocalScope();
        }
    }

    private T InLocalScope<T>(IEnumerable<string> names, Func<T> action)
    {
        PushLocalScope();
        PushConstScope();
        foreach (var name in names)
        {
            DeclareLocal(name);
        }

        try
        {
            return action();
        }
        finally
        {
            PopConstScope();
            PopLocalScope();
        }
    }
}
