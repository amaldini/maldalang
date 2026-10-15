// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;

public class WebMiddlewareRegistration
{
    public FunctionValue? Function { get; }
    public string? FunctionName { get; }
    public IReadOnlyList<string> ExceptPaths { get; }

    public WebMiddlewareRegistration(FunctionValue function, IReadOnlyList<string>? exceptPaths = null)
    {
        Function = function;
        ExceptPaths = exceptPaths ?? Array.Empty<string>();
    }

    public WebMiddlewareRegistration(string functionName, IReadOnlyList<string>? exceptPaths = null)
    {
        FunctionName = functionName;
        ExceptPaths = exceptPaths ?? Array.Empty<string>();
    }

    public bool ShouldSkipPath(string path)
    {
        if (ExceptPaths.Count == 0)
        {
            return false;
        }

        var normalizedPath = string.IsNullOrEmpty(path) ? "/" : path;
        foreach (var pattern in ExceptPaths)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (PathMatchesExceptPattern(normalizedPath, pattern.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PathMatchesExceptPattern(string path, string pattern)
    {
        if (pattern.EndsWith("*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1];
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase);
    }
}

public class MiddlewareNextCallbackInstance : ObjectInstance
{
    private readonly Action _onNext;

    public MiddlewareNextCallbackInstance(Action onNext) : base(null)
    {
        _onNext = onNext;
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "invoke")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = "invoke"
            };
            return RuntimeValue.Function(wrapper);
        }

        throw new Exception($"Undefined property '{name}' on middleware next callback.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        if (methodName != "invoke")
        {
            throw new Exception($"Unknown method: {methodName}");
        }

        if (args.Count != 0)
        {
            throw new Exception("next() expects 0 arguments");
        }

        _onNext();
        return RuntimeValue.Null();
    }
}

public class WebMiddlewareChain
{
    private readonly List<WebMiddlewareRegistration> _middlewares = new();

    public int Count => _middlewares.Count;

    public void Add(FunctionValue function, IReadOnlyList<string>? exceptPaths = null)
    {
        _middlewares.Add(new WebMiddlewareRegistration(function, exceptPaths));
    }

    public void Add(string functionName, IReadOnlyList<string>? exceptPaths = null)
    {
        _middlewares.Add(new WebMiddlewareRegistration(functionName, exceptPaths));
    }

    public async Task<bool> ExecuteAsync(
        RequestContextInstance request,
        ResponseContextInstance response,
        Func<WebMiddlewareRegistration, List<RuntimeValue>, Task<RuntimeValue>> invokeMiddleware)
    {
        for (var i = 0; i < _middlewares.Count; i++)
        {
            var registration = _middlewares[i];
            if (registration.ShouldSkipPath(request.Path))
            {
                continue;
            }

            var continuePipeline = false;
            var nextCallback = new MiddlewareNextCallbackInstance(() => continuePipeline = true);
            var args = new List<RuntimeValue>
            {
                RuntimeValue.Object(request),
                RuntimeValue.Object(response),
                RuntimeValue.Function(new FunctionValue(null, null, false, null)
                {
                    BuiltInInstance = nextCallback,
                    BuiltInMethod = "invoke"
                })
            };

            await invokeMiddleware(registration, args);

            if (!continuePipeline)
            {
                return false;
            }
        }

        return true;
    }
}
