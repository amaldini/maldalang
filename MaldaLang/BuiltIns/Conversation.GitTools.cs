// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using MaldaLang.Runtime.Tracing;
using Spectre.Console;
using ValueType = MaldaLang.Interpreter.ValueType;

public partial class ConversationInstance
{
    private RuntimeValue? ExecuteGitTool(string canonicalName, ToolInstance tool, RuntimeValue arguments, ObjectInstance argsObj, string? resolvedFilePath, string? resolvedDirPath)
    {
        switch (canonicalName)
        {
            case "git_status":
                try
                {
                    var repoPathVal = argsObj.Get("repoPath", null);
                    var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                        ? repoPathVal.AsString() 
                        : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                    
                    return BuiltInFunctions.CallBuiltIn("gitStatus", new List<RuntimeValue> { RuntimeValue.String(repoPath) }, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing git_status tool: {ex.Message}");
                }

            case "git_add":
                try
                {
                    var filesVal = argsObj.Get("files", null);
                    if (filesVal == null || filesVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: files parameter required");
                    
                    var repoPathVal = argsObj.Get("repoPath", null);
                    var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                        ? repoPathVal.AsString() 
                        : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                    
                    return BuiltInFunctions.CallBuiltIn("gitAdd", new List<RuntimeValue> 
                    { 
                        RuntimeValue.String(repoPath),
                        filesVal
                    }, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing git_add tool: {ex.Message}");
                }

            case "git_commit":
                try
                {
                    var messageVal = argsObj.Get("message", null);
                    if (messageVal == null || messageVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: message parameter required");
                    
                    var repoPathVal = argsObj.Get("repoPath", null);
                    var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                        ? repoPathVal.AsString() 
                        : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                    
                    return BuiltInFunctions.CallBuiltIn("gitCommit", new List<RuntimeValue> 
                    { 
                        RuntimeValue.String(repoPath),
                        messageVal
                    }, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing git_commit tool: {ex.Message}");
                }

            case "git_log":
                try
                {
                    var repoPathVal = argsObj.Get("repoPath", null);
                    var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                        ? repoPathVal.AsString() 
                        : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                    
                    var countVal = argsObj.Get("count", null);
                    var count = countVal != null && countVal.Type == ValueType.Integer 
                        ? countVal.AsInteger() 
                        : 10;
                    
                    return BuiltInFunctions.CallBuiltIn("gitLog", new List<RuntimeValue> 
                    { 
                        RuntimeValue.String(repoPath),
                        RuntimeValue.Integer(count)
                    }, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing git_log tool: {ex.Message}");
                }

            case "git_diff":
                try
                {
                    var repoPathVal = argsObj.Get("repoPath", null);
                    var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                        ? repoPathVal.AsString() 
                        : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                    
                    var filePathVal = argsObj.Get("filePath", null);
                    var stagedVal = argsObj.Get("staged", null);
                    var staged = stagedVal != null && stagedVal.Type == ValueType.Boolean 
                        ? stagedVal.AsBoolean() 
                        : false;
                    
                    var args = new List<RuntimeValue> { RuntimeValue.String(repoPath) };
                    if (filePathVal != null && filePathVal.Type == ValueType.String)
                        args.Add(filePathVal);
                    else
                        args.Add(RuntimeValue.Null());
                    args.Add(RuntimeValue.Boolean(staged));
                    
                    return BuiltInFunctions.CallBuiltIn("gitDiff", args, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing git_diff tool: {ex.Message}");
                }

            case "git_branch":
                try
                {
                    var actionVal = argsObj.Get("action", null);
                    if (actionVal == null || actionVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: action parameter required");
                    
                    var repoPathVal = argsObj.Get("repoPath", null);
                    var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                        ? repoPathVal.AsString() 
                        : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                    
                    var args = new List<RuntimeValue> { RuntimeValue.String(repoPath), actionVal };
                    
                    var branchNameVal = argsObj.Get("branchName", null);
                    if (branchNameVal != null && branchNameVal.Type == ValueType.String)
                        args.Add(branchNameVal);
                    
                    return BuiltInFunctions.CallBuiltIn("gitBranch", args, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing git_branch tool: {ex.Message}");
                }

            case "git_checkout":
                try
                {
                    var branchNameVal = argsObj.Get("branchName", null);
                    if (branchNameVal == null || branchNameVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: branchName parameter required");
                    
                    var repoPathVal = argsObj.Get("repoPath", null);
                    var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                        ? repoPathVal.AsString() 
                        : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                    
                    var createVal = argsObj.Get("create", null);
                    var create = createVal != null && createVal.Type == ValueType.Boolean 
                        ? createVal.AsBoolean() 
                        : false;
                    
                    return BuiltInFunctions.CallBuiltIn("gitCheckout", new List<RuntimeValue> 
                    { 
                        RuntimeValue.String(repoPath),
                        branchNameVal,
                        RuntimeValue.Boolean(create)
                    }, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing git_checkout tool: {ex.Message}");
                }

            case "git_push":
                try
                {
                    var repoPathVal = argsObj.Get("repoPath", null);
                    var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                        ? repoPathVal.AsString() 
                        : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                    
                    var remoteVal = argsObj.Get("remote", null);
                    var remote = remoteVal != null && remoteVal.Type == ValueType.String 
                        ? remoteVal.AsString() 
                        : "origin";
                    
                    var branchVal = argsObj.Get("branch", null);
                    
                    var args = new List<RuntimeValue> { RuntimeValue.String(repoPath), RuntimeValue.String(remote) };
                    if (branchVal != null && branchVal.Type == ValueType.String)
                        args.Add(branchVal);
                    
                    return BuiltInFunctions.CallBuiltIn("gitPush", args, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing git_push tool: {ex.Message}");
                }

            case "git_pull":
                try
                {
                    var repoPathVal = argsObj.Get("repoPath", null);
                    var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                        ? repoPathVal.AsString() 
                        : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                    
                    var remoteVal = argsObj.Get("remote", null);
                    var remote = remoteVal != null && remoteVal.Type == ValueType.String 
                        ? remoteVal.AsString() 
                        : "origin";
                    
                    var branchVal = argsObj.Get("branch", null);
                    
                    var args = new List<RuntimeValue> { RuntimeValue.String(repoPath), RuntimeValue.String(remote) };
                    if (branchVal != null && branchVal.Type == ValueType.String)
                        args.Add(branchVal);
                    
                    return BuiltInFunctions.CallBuiltIn("gitPull", args, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing git_pull tool: {ex.Message}");
                }

            default:
                return null;
        }
    }
}
