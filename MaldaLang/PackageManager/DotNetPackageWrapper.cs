// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MaldaLang.Interpreter;
using MaldaLang.PackageManager.Models;
using Environment = MaldaLang.Interpreter.Environment;

public class DotNetPackageWrapper
{
    private readonly Dictionary<string, Assembly> _loadedAssemblies = new();
    
    public Environment WrapAssembly(Assembly assembly, string? namespaceFilter = null)
    {
        var environment = new Environment();
        
        // Get all public types from the assembly
        var types = assembly.GetExportedTypes();
        
        if (namespaceFilter != null)
        {
            types = types.Where(t => t.Namespace == namespaceFilter || 
                                    (t.Namespace != null && t.Namespace.StartsWith(namespaceFilter + "."))).ToArray();
        }
        
        // Group types by namespace
        var namespaceGroups = types.GroupBy(t => t.Namespace ?? "");
        
        foreach (var nsGroup in namespaceGroups)
        {
            var nsName = nsGroup.Key;
            
            if (string.IsNullOrEmpty(nsName))
            {
                // Top-level types - import directly
                foreach (var type in nsGroup)
                {
                    var typeName = type.Name;
                    // Remove generic type parameters from name for lookup
                    if (typeName.Contains('`'))
                    {
                        typeName = typeName.Substring(0, typeName.IndexOf('`'));
                    }
                    
                    // Create a wrapper that allows instantiation via dotnetNew
                    var typeWrapper = RuntimeValue.String($"{type.Namespace}.{type.Name}");
                    environment.Define(typeName, typeWrapper);
                }
            }
            else
            {
                // Namespaced types - create namespace object
                var nsObj = new ObjectInstance(null);
                foreach (var type in nsGroup)
                {
                    var typeName = type.Name;
                    if (typeName.Contains('`'))
                    {
                        typeName = typeName.Substring(0, typeName.IndexOf('`'));
                    }
                    
                    var typeWrapper = RuntimeValue.String($"{type.Namespace}.{type.Name}");
                    nsObj.Set(typeName, typeWrapper);
                }
                
                // Import namespace as object
                var nsParts = nsName.Split('.');
                var currentEnv = environment;
                
                // Create nested namespace structure
                for (int i = 0; i < nsParts.Length - 1; i++)
                {
                    var part = nsParts[i];
                    if (!currentEnv.Contains(part))
                    {
                        var nsPartObj = new ObjectInstance(null);
                        currentEnv.Define(part, RuntimeValue.Object(nsPartObj));
                        var nsPartValue = currentEnv.Get(part);
                        currentEnv = new Environment(); // Create new env for nested access
                    }
                    else
                    {
                        var nsPartValue = currentEnv.Get(part);
                        if (nsPartValue.Type == MaldaLang.Interpreter.ValueType.Object)
                        {
                            // Continue with nested namespace
                            // Note: This is simplified - full implementation would need proper namespace object traversal
                        }
                    }
                }
                
                // Set the final namespace object
                var finalNsName = nsParts[nsParts.Length - 1];
                currentEnv.Define(finalNsName, RuntimeValue.Object(nsObj));
            }
        }
        
        return environment;
    }
    
    public Environment LoadDotNetNamespace(string namespaceName)
    {
        // Try to find namespace in already loaded assemblies
        foreach (var assembly in _loadedAssemblies.Values)
        {
            var types = assembly.GetExportedTypes()
                .Where(t => t.Namespace == namespaceName || 
                           (t.Namespace != null && t.Namespace.StartsWith(namespaceName + ".")));
            
            if (types.Any())
            {
                return WrapAssembly(assembly, namespaceName);
            }
        }
        
        // Try to load from framework assemblies
        var frameworkAssemblies = new[]
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "System.IO",
            "System.Text"
        };
        
        foreach (var frameworkNs in frameworkAssemblies)
        {
            if (namespaceName.StartsWith(frameworkNs))
            {
                try
                {
                    var assemblyName = frameworkNs.Split('.')[0];
                    var assembly = Assembly.Load(assemblyName);
                    _loadedAssemblies[assemblyName] = assembly;
                    return WrapAssembly(assembly, namespaceName);
                }
                catch
                {
                    // Assembly not found, continue
                }
            }
        }
        
        throw new InvalidOperationException($"Could not find .NET namespace: {namespaceName}");
    }
    
    public void RegisterAssembly(Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name;
        if (assemblyName != null)
        {
            _loadedAssemblies[assemblyName] = assembly;
        }
    }
}
