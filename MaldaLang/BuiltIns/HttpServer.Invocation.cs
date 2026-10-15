// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using System.IO;
using MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;

public partial class HttpServerInstance
{
    private async Task<RuntimeValue> CallTranspiledRouteFunctionAsync(Route route, List<RuntimeValue> functionArgs)
    {
        // Call transpiled static method via reflection
        // The Program class is in the GeneratedCode namespace
        // The transpiled code is in the entry assembly (the executable), not the executing assembly
        // Use GetType() instead of GetTypes() to avoid loading all types and their dependencies
        // This prevents issues with missing optional dependencies like LLamaSharp
        Type? programType = null;
        
        // Try entry assembly first (where the transpiled code is)
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            programType = entryAssembly.GetType("GeneratedCode.Program");
        }
        
        // Fallback to executing assembly if not found
        if (programType == null)
        {
            var executingAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            programType = executingAssembly.GetType("GeneratedCode.Program");
        }
        
        // Last resort: search all loaded assemblies (but avoid GetTypes() to prevent loading dependencies)
        if (programType == null)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    programType = assembly.GetType("GeneratedCode.Program");
                    if (programType != null)
                        break;
                }
                catch
                {
                    // Skip assemblies that can't be queried (e.g., missing dependencies)
                    continue;
                }
            }
        }
        
        if (programType == null)
        {
            throw new Exception("GeneratedCode.Program class not found. Make sure the transpiled code is in the GeneratedCode namespace.");
        }
        
        return await CallTranspiledFunctionFromProgramTypeAsync(programType, route.FunctionName, functionArgs);
    }

    private async Task<RuntimeValue> CallTranspiledFunctionByNameAsync(string functionName, List<RuntimeValue> functionArgs)
    {
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            var entryProgramType = entryAssembly.GetType("GeneratedCode.Program");
            if (entryProgramType != null)
            {
                return await CallTranspiledFunctionFromProgramTypeAsync(entryProgramType, functionName, functionArgs);
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var programType = assembly.GetType("GeneratedCode.Program");
                if (programType != null)
                {
                    return await CallTranspiledFunctionFromProgramTypeAsync(programType, functionName, functionArgs);
                }
            }
            catch
            {
                // Ignore assemblies that cannot be queried.
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type == null)
                {
                    continue;
                }

                var method = type.GetMethod(functionName,
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (method != null)
                {
                    return await CallTranspiledFunctionFromProgramTypeAsync(type, functionName, functionArgs);
                }
            }
        }

        throw new Exception("GeneratedCode.Program class not found. Make sure the transpiled code is in the GeneratedCode namespace.");
    }

    private async Task<RuntimeValue> CallTranspiledFunctionFromProgramTypeAsync(Type programType, string functionName, List<RuntimeValue> functionArgs)
    {
        var method = programType.GetMethod(functionName,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        if (method == null)
        {
            throw new Exception($"Transpiled function '{functionName}' not found");
        }

        var args = functionArgs.Select(arg => arg.Type switch
        {
            ValueType.Integer => (object)arg.AsInteger(),
            ValueType.Float => (object)arg.AsFloat(),
            ValueType.String => (object)arg.AsString(),
            ValueType.Boolean => (object)arg.AsBoolean(),
            ValueType.Function => (object)arg.AsFunction(),
            ValueType.Object => (object)arg.AsObject(),
            ValueType.Array => arg.AsArray(),
            _ => null
        }).ToArray();

        var task = (Task<object>)method.Invoke(null, args)!;
        var result = await task;
        return WebRuntimeHelpers.ConvertTranspiledResultToRuntimeValue(result);
    }
    
    private List<RuntimeValue> BindParameters(Route route, Dictionary<string, string> pathParams, 
        Dictionary<string, string> queryParams, RuntimeValue? requestBody,
        RequestContextInstance requestContext, ResponseContextInstance responseContext)
    {
        var args = new List<RuntimeValue>();
        var paramNames = route.ParameterNames;
        
        // Simple binding: path parameters by name, then query params, then body
        foreach (var paramName in paramNames)
        {
            if (paramName == "request" || paramName == "req")
            {
                args.Add(RuntimeValue.Object(requestContext));
                continue;
            }

            if (paramName == "response" || paramName == "res")
            {
                args.Add(RuntimeValue.Object(responseContext));
                continue;
            }
            if (paramName == "params" || paramName == "pathParams")
            {
                args.Add(requestContext.Get("params", null));
                continue;
            }
            if (paramName == "headers")
            {
                args.Add(requestContext.Get("headers", null));
                continue;
            }
            if (paramName == "cookies")
            {
                args.Add(requestContext.Get("cookies", null));
                continue;
            }

            if (route.PathParameterNames.Contains(paramName))
            {
                // Path parameter
                var value = pathParams.ContainsKey(paramName) 
                    ? RuntimeValue.String(pathParams[paramName]) 
                    : RuntimeValue.Null();
                args.Add(value);
            }
            else if (paramName == "queryParams" || paramName == "query")
            {
                // Special case: parameter named "queryParams" or "query" receives entire query params as object
                var jsonObj = new JsonObject();
                foreach (var kvp in queryParams)
                {
                    jsonObj.Set(kvp.Key, RuntimeValue.String(kvp.Value));
                }
                args.Add(RuntimeValue.Object(jsonObj));
            }
            else if (queryParams.ContainsKey(paramName))
            {
                // Query parameter
                args.Add(RuntimeValue.String(queryParams[paramName]));
            }
            else if (paramName == "body" && requestBody != null)
            {
                // Request body parameter (by convention, parameter named "body")
                args.Add(requestBody);
            }
            else
            {
                // No value provided
                args.Add(RuntimeValue.Null());
            }
        }
        
        return args;
    }

    private RequestContextInstance CreateRequestContext(
        HttpListenerRequest request,
        string path,
        Dictionary<string, string> pathParams,
        Dictionary<string, string> queryParams,
        RuntimeValue? requestBody,
        string correlationId)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in request.Headers.AllKeys)
        {
            if (key != null)
            {
                headers[key] = request.Headers[key] ?? string.Empty;
            }
        }

        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Cookie cookie in request.Cookies)
        {
            cookies[cookie.Name] = cookie.Value;
        }

        var remoteIp = ResolveClientIp(request);

        return new RequestContextInstance(
            request.HttpMethod,
            path,
            queryParams,
            headers,
            cookies,
            requestBody,
            pathParams,
            correlationId,
            remoteIp,
            request.Url?.Query ?? string.Empty,
            request.Url?.ToString(),
            request.Url?.Scheme,
            request.UserHostName ?? request.Headers["Host"] ?? string.Empty,
            request.ContentType ?? string.Empty,
            pathBase: _pathBase,
            sessionOptions: _sessionOptions);
    }

    private void CommitAndApplyResponse(
        RequestContextInstance requestContext,
        ResponseContextInstance responseContext,
        HttpListenerResponse response,
        HttpListenerRequest request)
    {
        if (responseContext.IsFlushed)
            return;

        SessionRuntime.CommitSession(requestContext, responseContext, IsRequestSecure(request));
        responseContext.ApplyTo(response, _pathBase, request);
    }

    private async Task<RuntimeValue> CallRouteFunctionAsync(FunctionValue function, List<RuntimeValue> args, Interpreter interpreter)
    {
        // Use the provided interpreter to call the function
        try
        {
            return await interpreter.CallFunctionAsync(function, args);
        }
        catch (Exception ex)
        {
            // Check if it's a RuntimeException with status
            if (ex is WebRuntimeException webRuntimeException)
            {
                throw webRuntimeException;
            }

            if (ex is RuntimeException rex)
            {
                throw rex;
            }
            throw (RuntimeException)RuntimeDiagnostics.PreserveContext(ex, interpreter);
        }
    }

    private async Task<bool> ExecuteMiddlewareChainAsync(RequestContextInstance requestContext, ResponseContextInstance responseContext)
    {
        if (_middlewareChain.Count == 0)
        {
            return true;
        }

        return await _middlewareChain.ExecuteAsync(
            requestContext,
            responseContext,
            async (registration, args) =>
            {
                if (registration.Function != null && _interpreter != null)
                {
                    var middlewareInterpreter = _interpreter.CreateExecutionInterpreter();
                    return await middlewareInterpreter.CallFunctionAsync(registration.Function, args);
                }

                if (!string.IsNullOrEmpty(registration.FunctionName))
                {
                    if (_interpreter != null)
                    {
                        var middlewareInterpreter = _interpreter.CreateExecutionInterpreter();
                        var function = ResolveFunction(middlewareInterpreter, registration.FunctionName!);
                        if (function == null)
                        {
                            throw new Exception($"Middleware function '{registration.FunctionName}' not found");
                        }
                        return await middlewareInterpreter.CallFunctionAsync(function, args);
                    }

                    return await CallTranspiledFunctionByNameAsync(registration.FunctionName!, args);
                }

                throw new Exception("Invalid middleware registration");
            });
    }

    private FunctionValue? ResolveFunction(Interpreter interpreter, string functionName)
    {
        if (functionName.Contains("."))
        {
            var parts = functionName.Split('.', 2);
            var className = parts[0];
            var methodName = parts[1];

            if (interpreter._classes.TryGetValue(className, out var klass))
            {
                if (klass.Methods.TryGetValue(methodName, out var classMethod))
                {
                    return classMethod;
                }

                if (klass.StaticMethods.TryGetValue(methodName, out var staticMethod))
                {
                    return staticMethod;
                }
            }

            return null;
        }

        try
        {
            var funcValue = interpreter._globals.Get(functionName);
            if (funcValue.Type == ValueType.Function)
            {
                return funcValue.AsFunction();
            }
        }
        catch
        {
            // Function not found.
        }

        return null;
    }
}
