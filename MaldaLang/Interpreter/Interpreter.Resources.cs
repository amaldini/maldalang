// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Statements;

public partial class Interpreter
{
    private readonly Stack<List<Func<Task>>> _deferFrames = new();

    private void PushDeferFrame() => _deferFrames.Push([]);

    private async Task RunAndPopDeferFrameAsync()
    {
        if (_deferFrames.Count == 0)
            return;

        var actions = _deferFrames.Pop();
        for (var i = actions.Count - 1; i >= 0; i--)
        {
            try
            {
                await actions[i]();
            }
            catch (BreakException)
            {
                throw;
            }
            catch (ContinueException)
            {
                throw;
            }
            catch (ReturnException)
            {
                throw;
            }
            catch (MALDAException)
            {
                // Defer cleanup errors should not mask the primary control flow.
            }
            catch (RuntimeException)
            {
            }
        }
    }

    private void RegisterDeferAction(DeferStatement defer)
    {
        if (_deferFrames.Count == 0)
            throw new RuntimeException("'defer' is only valid inside a block, function, or 'using' body.");

        var body = defer.Body;
        _deferFrames.Peek().Add(async () => await ExecuteBlockAsync(body, _environment));
    }

    private async Task<RuntimeValue?> ExecuteUsingResourceAsync(UsingResourceStatement stmt)
    {
        var resource = await EvaluateAsync(stmt.Initializer);
        var previous = _environment;
        var scopeEnv = new Environment(previous);
        _environment = scopeEnv;
        scopeEnv.Define(stmt.VariableName, resource);

        try
        {
            await ExecuteBlockAsync(stmt.Body, scopeEnv);
        }
        finally
        {
            _environment = previous;
            await TryDisposeResourceAsync(resource);
        }

        return null;
    }

    private async Task TryDisposeResourceAsync(RuntimeValue resource)
    {
        if (resource.Type == ValueType.Null)
            return;

        foreach (var methodName in new[] { "dispose", "close", "disconnect" })
        {
            if (await TryInvokeResourceMethodAsync(resource, methodName))
                return;
        }
    }

    private async Task<bool> TryInvokeResourceMethodAsync(RuntimeValue resource, string methodName)
    {
        if (resource.Type != ValueType.Object)
            return false;

        var obj = resource.AsObject();
        if (!obj.TryGet(methodName, out var member) || member == null || member.Type != ValueType.Function)
            return false;

        await CallFunctionAsync(member.AsFunction(), [], obj);
        return true;
    }
}
