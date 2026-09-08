// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Runtime.Journal;
using MaldaLang.Runtime.LlmCassettes;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Runtime object for a declared <c>context Name</c>.
/// </summary>
public sealed class DeclaredContextInstance : ObjectInstance
{
    private readonly ContextDeclaration _decl;
    private readonly List<RuntimeValue> _turns = new();
    private readonly Interpreter? _interpreter;

    public static RuntimeValue Create(
        string name,
        int budgetTokens,
        List<string> pin,
        int retainLast,
        string evict,
        string? compactPrompt,
        Interpreter? interpreter)
    {
        var decl = new ContextDeclaration(name, budgetTokens, pin, retainLast, evict, compactPrompt);
        return RuntimeValue.Object(new DeclaredContextInstance(decl, interpreter));
    }

    public DeclaredContextInstance(ContextDeclaration decl, Interpreter? interpreter) : base(null)
    {
        _decl = decl;
        _interpreter = interpreter;
        Set("name", RuntimeValue.String(decl.Name));
        Set("budget", RuntimeValue.Integer(decl.BudgetTokens ?? 0));
        Set("turns", RuntimeValue.Array(_turns.ToList()));
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "add")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = "add"
            };
            return RuntimeValue.Function(wrapper);
        }

        return base.Get(name, accessingClass);
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter)
    {
        if (methodName != "add")
            throw new RuntimeException($"Unknown context method: {methodName}");
        BuiltInArity.Require("add", args, 1, 2, "roleOrText, text?");
        var turn = new JsonObject();
        if (args.Count == 1)
        {
            turn.Set("role", RuntimeValue.String("user"));
            turn.Set("content", args[0]);
        }
        else
        {
            turn.Set("role", args[0]);
            turn.Set("content", args[1]);
        }

        _turns.Add(RuntimeValue.Object(turn));
        CompactIfNeeded(interpreter);
        Set("turns", RuntimeValue.Array(_turns.ToList()));
        return RuntimeValue.Object(this);
    }

    private void CompactIfNeeded(Interpreter interpreter)
    {
        if (_decl.RetainLast is int keep && _turns.Count > keep + _decl.Pin.Count)
        {
            RunJournal.Current.Append(new JournalEvent
            {
                Kind = JournalKind.Prompt,
                Name = "context.compact",
                PromptHash = _decl.CompactPromptName == null
                    ? null
                    : PromptHasher.Hash(_decl.CompactPromptName)
            });

            if (!string.IsNullOrEmpty(_decl.CompactPromptName)
                && interpreter._globals.TryGet(_decl.CompactPromptName, out var promptVal)
                && promptVal.Type == ValueType.Prompt)
            {
                var prompt = promptVal.AsPrompt();
                try
                {
                    prompt.CallAsync(new List<RuntimeValue> { RuntimeValue.Array(_turns.ToList()) }, interpreter)
                        .GetAwaiter().GetResult();
                }
                catch
                {
                    // Offline / no client: drop oldest only.
                }
            }

            var pinCount = _decl.Pin.Count;
            while (_turns.Count > keep + pinCount)
            {
                if (string.Equals(_decl.Evict, "none", StringComparison.Ordinal))
                    break;
                var dropAt = pinCount;
                if (dropAt < _turns.Count)
                    _turns.RemoveAt(dropAt);
                else
                    break;
            }
        }
    }
}

public sealed class DeclaredContextClass : ClassDefinition
{
    public ContextDeclaration Declaration { get; }

    public DeclaredContextClass(ContextDeclaration declaration)
        : base(declaration.Name, superclass: null)
    {
        Declaration = declaration;
    }
}
