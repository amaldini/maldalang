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
    // Git functions
    private static RuntimeValue BuiltInGitStatus(List<RuntimeValue> args)
    {
        BuiltInArity.Require("gitStatus", args, 0, 1, "repoPath?");
        var repoPath = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : Directory.GetCurrentDirectory();
        
        try
        {
            var result = ExecuteGitCommand(repoPath, "status", "--porcelain");
            if (result == null)
                return RuntimeValue.String("Error: Not a git repository or git command failed");
            
            // Parse porcelain output
            var statusObj = new JsonObject();
            var modified = new List<RuntimeValue>();
            var staged = new List<RuntimeValue>();
            var untracked = new List<RuntimeValue>();
            
            foreach (var line in result.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var status = line.Substring(0, 2);
                var file = line.Substring(3).Trim();
                
                if (status[0] == '?' || status[1] == '?')
                    untracked.Add(RuntimeValue.String(file));
                else if (status[0] != ' ' && status[0] != '?')
                    staged.Add(RuntimeValue.String(file));
                else if (status[1] != ' ' && status[1] != '?')
                    modified.Add(RuntimeValue.String(file));
            }
            
            statusObj.Set("modified", RuntimeValue.Array(modified));
            statusObj.Set("staged", RuntimeValue.Array(staged));
            statusObj.Set("untracked", RuntimeValue.Array(untracked));
            
            return RuntimeValue.Object(statusObj);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitAdd(List<RuntimeValue> args)
    {
        if (args.Count < 2) throw new Exception("gitAdd() expects 2 arguments: (repoPath, files)");
        var repoPath = Path.GetFullPath(args[0].AsString());
        var files = args[1].AsString();
        
        try
        {
            var (gitCwd, normalizedFiles) = NormalizeGitAddPaths(repoPath, files);
            var (success, _, error) = TryExecuteGitCommand(gitCwd, "add", normalizedFiles);
            if (!success)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? "" : ": " + error.Trim();
                return RuntimeValue.String($"Error: Failed to add files{detail}");
            }
            return RuntimeValue.String("Success: Files added to staging");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitCommit(List<RuntimeValue> args)
    {
        if (args.Count < 2) throw new Exception("gitCommit() expects 2 arguments: (repoPath, message)");
        var repoPath = args[0].AsString();
        var message = args[1].AsString();
        
        try
        {
            var result = ExecuteGitCommand(repoPath, "commit", "-m", message);
            if (result == null)
                return RuntimeValue.String("Error: Failed to create commit");
            return RuntimeValue.String("Success: Commit created");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitLog(List<RuntimeValue> args)
    {
        BuiltInArity.Require("gitLog", args, 0, 2, "repoPath?, count?");
        var repoPath = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : Directory.GetCurrentDirectory();
        var count = args.Count > 1 && args[1].Type == MaldaLang.Interpreter.ValueType.Integer 
            ? args[1].AsInteger() 
            : 10;
        
        try
        {
            var result = ExecuteGitCommand(repoPath, "log", $"-{count}", "--pretty=format:%H|%an|%ae|%ad|%s", "--date=iso");
            if (result == null)
                return RuntimeValue.String("Error: Failed to get git log");
            
            var commits = new List<RuntimeValue>();
            foreach (var line in result.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var parts = line.Split('|');
                if (parts.Length >= 5)
                {
                    var commitObj = new JsonObject();
                    commitObj.Set("hash", RuntimeValue.String(parts[0]));
                    commitObj.Set("author", RuntimeValue.String(parts[1]));
                    commitObj.Set("email", RuntimeValue.String(parts[2]));
                    commitObj.Set("date", RuntimeValue.String(parts[3]));
                    commitObj.Set("message", RuntimeValue.String(parts[4]));
                    commits.Add(RuntimeValue.Object(commitObj));
                }
            }
            
            return RuntimeValue.Array(commits);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitDiff(List<RuntimeValue> args)
    {
        BuiltInArity.Require("gitDiff", args, 0, 3, "repoPath?, filePath?, staged?");
        var repoPath = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : Directory.GetCurrentDirectory();
        var filePath = args.Count > 1 && args[1].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[1].AsString() 
            : null;
        var staged = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.Boolean 
            ? args[2].AsBoolean() 
            : false;
        
        try
        {
            var gitArgs = new List<string> { staged ? "--cached" : "" };
            if (filePath != null)
                gitArgs.Add(filePath);
            
            var result = ExecuteGitCommand(repoPath, "diff", gitArgs.Where(a => !string.IsNullOrEmpty(a)).ToArray());
            if (result == null)
                return RuntimeValue.String("No changes");
            return RuntimeValue.String(result);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitBranch(List<RuntimeValue> args)
    {
        if (args.Count < 2) throw new Exception("gitBranch() expects at least 2 arguments: (repoPath, action, ...)");
        var repoPath = args[0].AsString();
        var action = args[1].AsString();
        
        try
        {
            if (action == "list")
            {
                var result = ExecuteGitCommand(repoPath, "branch", "-a");
                if (result == null)
                    return RuntimeValue.String("Error: Failed to list branches");
                
                var branches = new List<RuntimeValue>();
                foreach (var line in result.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var branchName = line.Trim().TrimStart('*', ' ');
                    branches.Add(RuntimeValue.String(branchName));
                }
                return RuntimeValue.Array(branches);
            }
            else if (action == "create")
            {
                if (args.Count < 3) throw new Exception("gitBranch() 'create' action requires branchName");
                var branchName = args[2].AsString();
                var result = ExecuteGitCommand(repoPath, "branch", branchName);
                if (result == null)
                    return RuntimeValue.String("Error: Failed to create branch");
                return RuntimeValue.String($"Success: Branch '{branchName}' created");
            }
            else
            {
                return RuntimeValue.String($"Error: Unknown action '{action}'. Use 'list' or 'create'");
            }
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitCheckout(List<RuntimeValue> args)
    {
        if (args.Count < 2) throw new Exception("gitCheckout() expects at least 2 arguments: (repoPath, branchName, ...)");
        var repoPath = args[0].AsString();
        var branchName = args[1].AsString();
        var create = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.Boolean 
            ? args[2].AsBoolean() 
            : false;
        
        try
        {
            var gitArgs = create ? new[] { "-b", branchName } : new[] { branchName };
            var result = ExecuteGitCommand(repoPath, "checkout", gitArgs);
            if (result == null)
                return RuntimeValue.String("Error: Failed to checkout branch");
            return RuntimeValue.String($"Success: Switched to branch '{branchName}'");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitPush(List<RuntimeValue> args)
    {
        BuiltInArity.Require("gitPush", args, 0, 3, "repoPath?, remote?, branch?");
        var repoPath = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : Directory.GetCurrentDirectory();
        var remote = args.Count > 1 && args[1].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[1].AsString() 
            : "origin";
        var branch = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[2].AsString() 
            : null;
        
        try
        {
            var gitArgs = branch != null ? new[] { remote, branch } : new[] { remote };
            var result = ExecuteGitCommand(repoPath, "push", gitArgs);
            if (result == null)
                return RuntimeValue.String("Error: Failed to push");
            return RuntimeValue.String("Success: Pushed to remote");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitPull(List<RuntimeValue> args)
    {
        BuiltInArity.Require("gitPull", args, 0, 3, "repoPath?, remote?, branch?");
        var repoPath = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : Directory.GetCurrentDirectory();
        var remote = args.Count > 1 && args[1].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[1].AsString() 
            : "origin";
        var branch = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[2].AsString() 
            : null;
        
        try
        {
            var gitArgs = branch != null ? new[] { remote, branch } : new[] { remote };
            var result = ExecuteGitCommand(repoPath, "pull", gitArgs);
            if (result == null)
                return RuntimeValue.String("Error: Failed to pull");
            return RuntimeValue.String("Success: Pulled from remote");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static string? ExecuteGitCommand(string repoPath, string command, params string[] args)
    {
        var (success, output, _) = TryExecuteGitCommand(repoPath, command, args);
        return success ? output : null;
    }

    private static (bool Success, string Output, string Error) TryExecuteGitCommand(string repoPath, string command, params string[] args)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"{command} {string.Join(" ", args.Select(a => $"\"{a}\""))}",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
                return (false, "", "Failed to start git process");
            
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            
            if (process.ExitCode != 0)
                return (false, output, error);
            
            return (true, output, error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private static string? GetGitRoot(string startPath)
    {
        var (success, output, _) = TryExecuteGitCommand(startPath, "rev-parse", "--show-toplevel");
        return success ? output.Trim() : null;
    }

    private static (string GitCwd, string Files) NormalizeGitAddPaths(string repoPath, string files)
    {
        repoPath = Path.GetFullPath(repoPath);

        if (string.IsNullOrWhiteSpace(files) || files.Trim() == ".")
            return (repoPath, ".");

        var gitRoot = GetGitRoot(repoPath);
        gitRoot = gitRoot != null ? Path.GetFullPath(gitRoot) : null;

        var resolved = new List<string>();
        foreach (var part in files.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            resolved.Add(ResolveGitAddPathForRepoPath(repoPath, gitRoot, part));
        }

        if (resolved.Count > 0 && resolved.All(static p => p == "."))
            return (repoPath, ".");

        // Always run git add from repoPath with paths relative to that directory.
        // Running from git root with repo-root pathspecs breaks when repoPath is nested
        // (e.g. Ralph worktree project dir); git would look for doubled paths like
        // Examples/RalphWiggum/snake-demo/Examples/RalphWiggum/...
        return (repoPath, string.Join(" ", resolved));
    }

    private static string ResolveGitAddPathForRepoPath(string repoPath, string? gitRoot, string path)
    {
        path = path.Replace('\\', '/').Trim().Trim('"', '\'');

        if (path == "." || path == "")
            return ".";

        var directFull = Path.GetFullPath(Path.Combine(repoPath, path.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(directFull) || Directory.Exists(directFull))
            return ToGitForwardSlashPath(Path.GetRelativePath(repoPath, directFull));

        if (Path.IsPathRooted(path))
        {
            var absFull = Path.GetFullPath(path);
            if (File.Exists(absFull) || Directory.Exists(absFull))
                return ToGitForwardSlashPath(Path.GetRelativePath(repoPath, absFull));
        }

        if (gitRoot != null)
        {
            var fromGitRoot = Path.GetFullPath(Path.Combine(gitRoot, path.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(fromGitRoot) || Directory.Exists(fromGitRoot))
                return ToGitForwardSlashPath(Path.GetRelativePath(repoPath, fromGitRoot));

            var repoRelFromRoot = ToGitForwardSlashPath(Path.GetRelativePath(gitRoot, repoPath));
            if (path == repoRelFromRoot || path == repoRelFromRoot + "/"
                || path.StartsWith(repoRelFromRoot + "/", StringComparison.Ordinal))
            {
                var suffix = path.Length > repoRelFromRoot.Length
                    ? path[(repoRelFromRoot.Length + 1)..]
                    : "";
                if (string.IsNullOrEmpty(suffix))
                    return ".";
                var suffixFull = Path.GetFullPath(Path.Combine(repoPath, suffix.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(suffixFull) || Directory.Exists(suffixFull))
                    return ToGitForwardSlashPath(Path.GetRelativePath(repoPath, suffixFull));
            }
        }

        var basename = Path.GetFileName(path.TrimEnd('/'));
        if (!string.IsNullOrEmpty(basename))
        {
            var inRepoPath = Path.Combine(repoPath, basename);
            if (File.Exists(inRepoPath) || Directory.Exists(inRepoPath))
                return ToGitForwardSlashPath(basename);
        }

        return path;
    }

    private static string ToGitForwardSlashPath(string path) => path.Replace('\\', '/');
    
    // Git tool creation functions
    private static RuntimeValue BuiltInCreateGitStatusTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitStatusTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitStatusTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitAddTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitAddTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitAddTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitCommitTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitCommitTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitCommitTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitLogTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitLogTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitLogTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitDiffTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitDiffTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitDiffTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitBranchTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitBranchTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitBranchTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitCheckoutTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitCheckoutTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitCheckoutTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitPushTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitPushTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitPushTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitPullTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitPullTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitPullTool(workingDir);
    }
}
