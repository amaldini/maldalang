// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections.Generic;
using System.IO;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

public class ToolInstance : ObjectInstance
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    private RuntimeValue _parameters;
    private RuntimeValue? _handler;
    private FunctionValue? _functionHandler;
    private Interpreter? _interpreter;
    private System.Reflection.MethodInfo? _transpiledMethod; // For transpiled code
    
    public ToolInstance() : base(null)
    {
        _parameters = RuntimeValue.Null();
        WorkingDirectory = "";
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "name")
            return RuntimeValue.String(Name);
        if (name == "description")
            return RuntimeValue.String(Description);
        
        // Handle method access - create a FunctionValue wrapper
        if (name == "getSchema" || name == "execute" || name == "describe")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on Tool.");
    }
    
    public void Initialize(string name, string description, RuntimeValue parameters, RuntimeValue? handler, string workingDirectory = "")
    {
        Name = name;
        Description = description;
        _parameters = parameters;
        _handler = handler;
        WorkingDirectory = workingDirectory;
    }
    
    public void SetFunctionHandler(FunctionValue function, Interpreter? interpreter = null)
    {
        _functionHandler = function;
        _interpreter = interpreter;
    }
    
    public void SetTranspiledMethod(System.Reflection.MethodInfo method)
    {
        _transpiledMethod = method;
    }
    
    public FunctionValue? GetFunctionHandler()
    {
        return _functionHandler;
    }
    
    public Interpreter? GetInterpreter()
    {
        return _interpreter;
    }
    
    /// <summary>JSON Schema object stored as the tool's parameters (input schema).</summary>
    public RuntimeValue GetParametersSchema() => _parameters;

    public System.Reflection.MethodInfo? GetTranspiledMethod()
    {
        return _transpiledMethod;
    }
    
    public bool IsPathAllowed(string path)
    {
        if (string.IsNullOrEmpty(WorkingDirectory))
            return true; // No restriction if no working directory set
        
        try
        {
            if (EmbeddedFolderStore.IsEmbedPath(WorkingDirectory))
                return ResolveEmbedPathUnderWorkingDirectory(path) != null;
            return GetFullPathUnderWorkingDirectory(path) != null;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Normalizes an LLM-supplied path for tool execution.
    /// Relative paths are returned unchanged when allowed.
    /// Absolute paths under <see cref="WorkingDirectory"/> are converted to relative paths.
    /// Returns null when the path is outside the working directory.
    /// </summary>
    public string? NormalizePathForWorkingDirectory(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        
        if (string.IsNullOrEmpty(WorkingDirectory))
            return path;

        if (EmbeddedFolderStore.IsEmbedPath(WorkingDirectory))
        {
            var resolved = ResolveEmbedPathUnderWorkingDirectory(path);
            if (resolved == null)
                return null;

            if (EmbeddedFolderStore.IsEmbedPath(path))
            {
                var root = WorkingDirectory.TrimEnd('/');
                if (resolved.Equals(root, StringComparison.OrdinalIgnoreCase))
                    return ".";
                if (resolved.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                    return resolved.Substring(root.Length + 1);
            }

            return path.Replace('\\', '/').TrimStart('/');
        }
        
        var targetFull = GetFullPathUnderWorkingDirectory(path);
        if (targetFull == null)
            return null;
        
        if (Path.IsPathRooted(path))
        {
            var workingDirFull = Path.GetFullPath(WorkingDirectory);
            return Path.GetRelativePath(workingDirFull, targetFull);
        }
        
        return path;
    }

    /// <summary>
    /// Resolve a tool path against this tool's working directory (disk or <c>embed:</c>).
    /// </summary>
    public string? ResolvePathAgainstWorkingDirectory(string path)
    {
        if (string.IsNullOrEmpty(WorkingDirectory))
            return path;

        if (EmbeddedFolderStore.IsEmbedPath(WorkingDirectory))
            return ResolveEmbedPathUnderWorkingDirectory(path);

        if (Path.IsPathRooted(path))
            return path;

        return Path.Combine(WorkingDirectory, path);
    }

    private string? ResolveEmbedPathUnderWorkingDirectory(string path)
    {
        if (string.IsNullOrEmpty(path) || path == ".")
            return WorkingDirectory.TrimEnd('/');

        if (EmbeddedFolderStore.IsEmbedPath(path))
        {
            if (!EmbeddedFolderStore.TryParsePath(WorkingDirectory, out var workAlias, out var workRel) ||
                !EmbeddedFolderStore.TryParsePath(path, out var pathAlias, out var pathRel))
            {
                return null;
            }

            if (!string.Equals(workAlias, pathAlias, StringComparison.OrdinalIgnoreCase))
                return null;

            if (string.IsNullOrEmpty(workRel))
                return EmbeddedFolderStore.MakeRoot(workAlias) + (string.IsNullOrEmpty(pathRel) ? "" : "/" + pathRel);

            if (string.IsNullOrEmpty(pathRel))
                return string.IsNullOrEmpty(workRel) ? EmbeddedFolderStore.MakeRoot(workAlias) : null;

            if (!pathRel.Equals(workRel, StringComparison.OrdinalIgnoreCase) &&
                !pathRel.StartsWith(workRel + "/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return EmbeddedFolderStore.Join(EmbeddedFolderStore.MakeRoot(workAlias), pathRel);
        }

        var cleaned = path.Replace('\\', '/').Trim('/');
        return EmbeddedFolderStore.TryJoin(WorkingDirectory, new[] { cleaned }, out var joined)
            ? joined
            : null;
    }
    
    private static bool IsUnderWorkingDirectory(string workingDirFull, string targetPath)
    {
        if (targetPath.Equals(workingDirFull, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = workingDirFull + Path.DirectorySeparatorChar;
        return targetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private string? GetFullPathUnderWorkingDirectory(string path)
    {
        if (string.IsNullOrEmpty(WorkingDirectory))
            return Path.GetFullPath(path);

        // Trim trailing separators so "." resolving to the workdir root still matches.
        // Child paths are checked with an explicit separator to prevent prefix bypass
        // (e.g. workdir "C:\proj\demo" must not allow "C:\proj\demo-evil\file").
        var workingDirFull = Path.GetFullPath(WorkingDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string targetPath;
        if (Path.IsPathRooted(path))
        {
            targetPath = Path.GetFullPath(path);
        }
        else
        {
            targetPath = Path.GetFullPath(Path.Combine(workingDirFull, path));
        }

        return IsUnderWorkingDirectory(workingDirFull, targetPath)
            ? targetPath
            : null;
    }
    
    public RuntimeValue GetSchema()
    {
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.String("function"));
        
        var func = new JsonObject();
        func.Set("name", RuntimeValue.String(Name));
        func.Set("description", RuntimeValue.String(Description));
        func.Set("parameters", _parameters);
        
        schema.Set("function", RuntimeValue.Object(func));
        return RuntimeValue.Object(schema);
    }
    
    /// <summary>
    /// Invokes a MALDA / transpiled tool handler with LLM-style object arguments.
    /// Single-parameter handlers receive the whole args object when the param name
    /// is not present as a property (idiom for <c>new Tool(..., handler)</c>).
    /// </summary>
    public static RuntimeValue InvokeMaldaToolFunction(
        FunctionValue function,
        Interpreter interpreter,
        RuntimeValue arguments)
    {
        try
        {
            if (arguments.Type != ValueType.Object)
                return RuntimeValue.String("Error: Tool arguments must be an object");

            // Transpiled function values often have no Declaration — pass the args bag.
            if (function.Declaration == null)
            {
                if (function.TranspiledDelegate == null)
                    return RuntimeValue.String("Error: Tool function has no declaration");
                return interpreter.CallFunctionAsync(
                    function,
                    new List<RuntimeValue> { arguments },
                    null).GetAwaiter().GetResult();
            }

            var argsObj = arguments.AsObject();
            var functionParams = function.Declaration.Parameters;
            var functionArgs = new List<RuntimeValue>();

            if (functionParams.Count == 1)
            {
                var only = functionParams[0];
                RuntimeValue? direct = null;
                try
                {
                    direct = argsObj.Get(only, null);
                }
                catch
                {
                    direct = null;
                }

                // new Tool handlers typically use function(args) { ... args.slug ... }
                if (direct == null || direct.Type == ValueType.Null)
                    functionArgs.Add(arguments);
                else
                    functionArgs.Add(direct);
            }
            else
            {
                foreach (var paramName in functionParams)
                {
                    try
                    {
                        var paramValue = argsObj.Get(paramName, null);
                        functionArgs.Add(paramValue ?? RuntimeValue.Null());
                    }
                    catch
                    {
                        functionArgs.Add(RuntimeValue.Null());
                    }
                }
            }

            return interpreter.CallFunctionAsync(function, functionArgs, null).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error executing tool function: {ex.Message}");
        }
    }

    public virtual RuntimeValue Execute(RuntimeValue arguments, Interpreter? interpreter = null)
    {
        try
        {
            // Extract file path from arguments and validate
            if (arguments.Type == ValueType.Object)
            {
                var argsObj = arguments.AsObject();
                string? filePath = null;
                string? dirPath = null;
                
                // Try to get filePath or dirPath
                try
                {
                    var filePathVal = argsObj.Get("filePath", null);
                    if (filePathVal != null && filePathVal.Type == ValueType.String)
                        filePath = filePathVal.AsString();
                }
                catch { }
                
                try
                {
                    var dirPathVal = argsObj.Get("dirPath", null);
                    if (dirPathVal != null && dirPathVal.Type == ValueType.String)
                        dirPath = dirPathVal.AsString();
                }
                catch { }
                
                // Validate path
                var pathToCheck = filePath ?? dirPath;
                if (pathToCheck != null && !IsPathAllowed(pathToCheck))
                {
                    return RuntimeValue.String($"Error: Path '{pathToCheck}' is outside the allowed working directory '{WorkingDirectory}'");
                }
            }

            var functionHandler = _functionHandler;
            var interp = _interpreter ?? interpreter;
            if (functionHandler != null && interp != null)
                return InvokeMaldaToolFunction(functionHandler, interp, arguments);

            if (_handler != null && _handler.Type == ValueType.Function && interpreter != null)
                return InvokeMaldaToolFunction(_handler.AsFunction(), interpreter, arguments);
            
            return RuntimeValue.String("Tool execution validated");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error executing tool: {ex.Message}");
        }
    }
    
    public RuntimeValue Describe()
    {
        return RuntimeValue.String($"{Name}: {Description}");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "getSchema":
                return GetSchema();
            
            case "execute":
                if (args.Count != 1)
                    throw new Exception("execute() expects 1 argument");
                return Execute(args[0]);
            
            case "describe":
                return Describe();
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
}
