// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInLoadAssembly(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("loadAssembly() expects 1 string argument (path or assembly name)");

        var id = args[0].AsString();
        Assembly assembly;

        try
        {
            // Treat rooted paths or .dll-suffixed values as file paths
            if (Path.IsPathRooted(id) || id.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = Path.GetFullPath(id);
                if (!File.Exists(fullPath))
                    throw new Exception($"Assembly file not found: {fullPath}");

                assembly = Assembly.LoadFrom(fullPath);
            }
            else
            {
                // Load by simple or full assembly name
                assembly = Assembly.Load(id);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"loadAssembly() failed: {ex.Message}");
        }

        return RuntimeValue.Object(new DotNetAssemblyInstance(assembly));
    }

    private static RuntimeValue BuiltInGetDotNetType(List<RuntimeValue> args)
    {
        if (args.Count == 1 && args[0].Type == ValueType.String)
        {
            var typeName = args[0].AsString();
            var type = Type.GetType(typeName, throwOnError: false, ignoreCase: false);

            if (type == null)
            {
                // Search loaded assemblies as a fallback
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(typeName, throwOnError: false, ignoreCase: false);
                    if (type != null)
                        break;
                }
            }

            if (type == null)
                throw new Exception($"Type '{typeName}' not found in loaded assemblies.");

            return RuntimeValue.Object(new DotNetTypeInstance(type));
        }

        if (args.Count == 2 && args[0].Type == ValueType.Object && args[1].Type == ValueType.String)
        {
            var asmObj = args[0].AsObject();
            if (asmObj is not DotNetAssemblyInstance asm)
                throw new Exception("getDotNetType() first argument must be an Assembly returned by loadAssembly()");

            var typeName = args[1].AsString();
            var type = asm.Assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type == null)
                throw new Exception($"Type '{typeName}' not found in assembly '{asm.Assembly.FullName}'.");

            return RuntimeValue.Object(new DotNetTypeInstance(type));
        }

        throw new Exception("getDotNetType() expects either (string fullTypeName) or (assembly, string typeName)");
    }

    private static RuntimeValue BuiltInDotNetNew(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("dotnetNew() expects at least 1 argument: (typeOrTypeName, ...ctorArgs)");

        Type? type = null;

        // First argument: either DotNetTypeInstance or string type name
        if (args[0].Type == ValueType.Object)
        {
            var obj = args[0].AsObject();
            if (obj is not DotNetTypeInstance typeHandle)
                throw new Exception("dotnetNew() first argument must be a type handle (from getDotNetType) or a string type name");
            type = typeHandle.Type;
        }
        else if (args[0].Type == ValueType.String)
        {
            var typeName = args[0].AsString();
            type = Type.GetType(typeName, throwOnError: false, ignoreCase: false);

            if (type == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(typeName, throwOnError: false, ignoreCase: false);
                    if (type != null)
                        break;
                }
            }

            if (type == null)
                throw new Exception($"Type '{typeName}' not found in loaded assemblies.");
        }
        else
        {
            throw new Exception("dotnetNew() first argument must be a type handle (from getDotNetType) or a string type name");
        }

        var ctorArgsRuntime = args.Skip(1).ToList();
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        if (ctors.Length == 0 && ctorArgsRuntime.Count == 0)
        {
            // Value type or type with implicit default ctor
            var defaultInstance = Activator.CreateInstance(type);
            if (defaultInstance == null)
                throw new Exception($"Could not create instance of '{type.FullName}'.");
            return RuntimeValue.Object(new DotNetObjectInstance(defaultInstance));
        }

        ConstructorInfo? chosen = null;
        object?[]? converted = null;

        foreach (var ctor in ctors)
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length != ctorArgsRuntime.Count)
                continue;

            try
            {
                var temp = new object?[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    temp[i] = DotNetInteropHelpers.ConvertToClr(ctorArgsRuntime[i], parameters[i].ParameterType);
                }

                chosen = ctor;
                converted = temp;
                break;
            }
            catch
            {
                continue;
            }
        }

        if (chosen == null)
        {
            throw new Exception($"No suitable constructor found for type '{type.FullName}'.");
        }

        try
        {
            var instance = chosen.Invoke(converted);
            if (instance == null)
                throw new Exception($"Constructor for type '{type.FullName}' returned null.");

            return RuntimeValue.Object(new DotNetObjectInstance(instance));
        }
        catch (TargetInvocationException tie)
        {
            throw new Exception(tie.InnerException?.Message ?? tie.Message);
        }
    }
    
    private static RuntimeValue BuiltInLoadNativeModule(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("loadNativeModule() expects exactly 1 string argument");

        var module = NativeModuleRegistry.LoadModule(args[0].AsString());
        return RuntimeValue.Object(new DotNetObjectInstance(module));
    }

    private static RuntimeValue BuiltInCreateNativeCallback(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count != 1 || args[0].Type != ValueType.Function)
            throw new Exception("createNativeCallback() expects exactly 1 function argument");
        if (interpreter == null)
            throw new Exception("createNativeCallback() is not available in this runtime context");

        return RuntimeValue.Object(new DotNetObjectInstance(new NativeCallbackBridge(args[0].AsFunction(), interpreter)));
    }
}
