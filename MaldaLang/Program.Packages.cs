// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Interpreter;
using MaldaLang.PackageManager;
using System;
using System.Text;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using MaldaLang.Testing;
using MaldaLang.Scaffolding;
using MaldaLang.Deployment;
using MaldaLang.UIHost;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Cli;
using MaldaLang.Eval;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.BuiltIns;
using MaldaLang.DebugAdapter;
using SystemEnvironment = System.Environment;
using PackageManager = MaldaLang.PackageManager.PackageManager;

namespace MaldaLang;

partial class Program
{
    static async Task PackageManagerCommand(string[] args)
    {
        if (args.Length < 1)
        {
            ShowPackageManagerHelp();
            return;
        }
        
        var command = args[0].ToLower();
        var pm = new MaldaLang.PackageManager.PackageManager();
        
        switch (command)
        {
            case "install":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: malda install <local-path>");
                    Console.WriteLine("       malda install <package>[@<version>]   (needs MALDA_REGISTRY_URL)");
                    SystemEnvironment.Exit(1);
                    return;
                }
                
                var packageSpec = args[1];
                if (LooksLikeLocalPackagePath(packageSpec))
                {
                    var localSuccess = pm.InstallFromPath(packageSpec);
                    SystemEnvironment.Exit(localSuccess ? 0 : 1);
                    break;
                }

                string? packageName = null;
                string? version = null;
                
                if (packageSpec.Contains('@'))
                {
                    var parts = packageSpec.Split('@', 2);
                    packageName = parts[0];
                    version = parts[1];
                }
                else
                {
                    packageName = packageSpec;
                }
                
                var success = await pm.InstallAsync(packageName, version);
                SystemEnvironment.Exit(success ? 0 : 1);
                break;
                
            case "uninstall":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: malda uninstall <package>[@<version>]");
                    SystemEnvironment.Exit(1);
                    return;
                }
                
                var uninstallSpec = args[1];
                string? uninstallPackageName = null;
                string? uninstallVersion = null;
                
                if (uninstallSpec.Contains('@'))
                {
                    var parts = uninstallSpec.Split('@', 2);
                    uninstallPackageName = parts[0];
                    uninstallVersion = parts[1];
                }
                else
                {
                    uninstallPackageName = uninstallSpec;
                }
                
                var uninstallSuccess = pm.Uninstall(uninstallPackageName, uninstallVersion);
                SystemEnvironment.Exit(uninstallSuccess ? 0 : 1);
                break;
                
            case "list":
                if (args.Length > 1 && string.Equals(args[1], "--workspace", StringComparison.OrdinalIgnoreCase))
                    pm.ListWorkspace();
                else
                    pm.List();
                break;
                
            case "search":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: malda search <query>   (needs MALDA_REGISTRY_URL)");
                    SystemEnvironment.Exit(1);
                    return;
                }
                
                var query = args[1];
                try
                {
                    var results = await pm.SearchAsync(query);
                    if (results == null || results.Count == 0)
                    {
                        Console.WriteLine("No packages found");
                    }
                    else
                    {
                        var count = results.Count;
                        Console.WriteLine($"Found {count} package(s):");
                        foreach (var pkg in results)
                        {
                            Console.WriteLine($"  {pkg.Name}@{pkg.Version} - {pkg.Description ?? "No description"}");
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    SystemEnvironment.Exit(1);
                }
                break;
                
            case "init":
                var dir = args.Length > 1 ? args[1] : null;
                var initSuccess = pm.Init(dir);
                SystemEnvironment.Exit(initSuccess ? 0 : 1);
                break;
                
            default:
                ShowPackageManagerHelp();
                SystemEnvironment.Exit(1);
                break;
        }
    }

    static bool LooksLikeLocalPackagePath(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return false;

        // name@version is always remote-style
        if (spec.Contains('@') && !spec.Contains('/') && !spec.Contains('\\'))
            return false;

        if (spec.StartsWith('.') ||
            spec.Contains('/') ||
            spec.Contains('\\') ||
            spec.EndsWith(".malda", StringComparison.OrdinalIgnoreCase) ||
            spec.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var full = Path.GetFullPath(spec);
            return Directory.Exists(full) || File.Exists(full);
        }
        catch
        {
            return false;
        }
    }
    
    static void ShowPackageManagerHelp()
    {
        Console.WriteLine("Package Manager Commands:");
        Console.WriteLine("  Workspace packages (no registry; preferred for OSS):");
        Console.WriteLine("    Put libs under packages/<name>/ and import them, or:");
        Console.WriteLine("    malda list --workspace                 - List packages/ visible from cwd");
        Console.WriteLine("    malda install <local-path>             - Copy a local package into ~/.maldalang/packages");
        Console.WriteLine("    malda init [directory]                 - Write package.json");
        Console.WriteLine("    malda list                             - List installed packages");
        Console.WriteLine("    malda uninstall <package>[@<version>]  - Remove installed package");
        Console.WriteLine();
        Console.WriteLine("  Remote registry (optional; needs MALDA_REGISTRY_URL):");
        Console.WriteLine("    malda install <package>[@<version>]    - Install from registry");
        Console.WriteLine("    malda search <query>                   - Search registry");
        Console.WriteLine();
        Console.WriteLine("  Docs: docs/workspace-packages.md");
    }
}
