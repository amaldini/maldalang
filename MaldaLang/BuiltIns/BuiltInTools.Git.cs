// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

public static partial class BuiltInTools
{
    private const string GitRepoPathParamDescription =
        "Leave unset. Defaults to the agent working directory (same folder as read_file/write_file). Do not pass the process launch directory or a different checkout/worktree.";

    public static RuntimeValue CreateGitStatusTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_status",
            "Gets git status: modified, staged, and untracked files. Use this after creating new files — untracked files appear here, not in git_diff.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitAddTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var filesProp = new JsonObject();
        filesProp.Set("type", RuntimeValue.String("string"));
        filesProp.Set("description", RuntimeValue.String("Files to stage: use '.' for all changes in the working directory, or basename paths relative to the agent workdir (e.g. snake.html PRD.md). Do not use repo-root paths."));
        properties.Set("files", RuntimeValue.Object(filesProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("files") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_add",
            "Adds files to the git staging area. Use '.' to add all files, or specify specific file paths.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitCommitTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var messageProp = new JsonObject();
        messageProp.Set("type", RuntimeValue.String("string"));
        messageProp.Set("description", RuntimeValue.String("Commit message describing the changes"));
        properties.Set("message", RuntimeValue.Object(messageProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("message") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_commit",
            "Creates a commit with the staged changes. Requires a commit message.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitLogTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var countProp = new JsonObject();
        countProp.Set("type", RuntimeValue.String("integer"));
        countProp.Set("description", RuntimeValue.String("Optional: Number of commits to show (default: 10)"));
        countProp.Set("default", RuntimeValue.Integer(10));
        properties.Set("count", RuntimeValue.Object(countProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_log",
            "Shows the commit history. Returns commit messages, authors, dates, and commit hashes.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitDiffTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Optional: Specific file path to show diff for (if not provided, shows all changes)"));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        var stagedProp = new JsonObject();
        stagedProp.Set("type", RuntimeValue.String("boolean"));
        stagedProp.Set("description", RuntimeValue.String("Optional: Show staged changes instead of unstaged (default: false)"));
        stagedProp.Set("default", RuntimeValue.Boolean(false));
        properties.Set("staged", RuntimeValue.Object(stagedProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_diff",
            "Shows diffs for tracked files only (working tree vs index, or index vs last commit). New untracked files are not included — use git_status instead. Empty output does not mean no new files exist.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitBranchTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var actionProp = new JsonObject();
        actionProp.Set("type", RuntimeValue.String("string"));
        actionProp.Set("description", RuntimeValue.String("Action to perform: 'list' to show all branches, 'create' to create a new branch"));
        actionProp.Set("enum", RuntimeValue.Array(new List<RuntimeValue> 
        { 
            RuntimeValue.String("list"), 
            RuntimeValue.String("create")
        }));
        properties.Set("action", RuntimeValue.Object(actionProp));
        
        var branchNameProp = new JsonObject();
        branchNameProp.Set("type", RuntimeValue.String("string"));
        branchNameProp.Set("description", RuntimeValue.String("Optional: Branch name (required for 'create' action)"));
        properties.Set("branchName", RuntimeValue.Object(branchNameProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("action") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_branch",
            "Manages git branches. Can list all branches or create a new branch. Branch deletion is disabled for safety.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitCheckoutTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var branchNameProp = new JsonObject();
        branchNameProp.Set("type", RuntimeValue.String("string"));
        branchNameProp.Set("description", RuntimeValue.String("Branch name to checkout"));
        properties.Set("branchName", RuntimeValue.Object(branchNameProp));
        
        var createProp = new JsonObject();
        createProp.Set("type", RuntimeValue.String("boolean"));
        createProp.Set("description", RuntimeValue.String("Optional: Create the branch if it doesn't exist (default: false)"));
        createProp.Set("default", RuntimeValue.Boolean(false));
        properties.Set("create", RuntimeValue.Object(createProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("branchName") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_checkout",
            "Switches to a different branch. Can optionally create the branch if it doesn't exist.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitPushTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var remoteProp = new JsonObject();
        remoteProp.Set("type", RuntimeValue.String("string"));
        remoteProp.Set("description", RuntimeValue.String("Optional: Remote name (default: 'origin')"));
        remoteProp.Set("default", RuntimeValue.String("origin"));
        properties.Set("remote", RuntimeValue.Object(remoteProp));
        
        var branchProp = new JsonObject();
        branchProp.Set("type", RuntimeValue.String("string"));
        branchProp.Set("description", RuntimeValue.String("Optional: Branch name to push (defaults to current branch)"));
        properties.Set("branch", RuntimeValue.Object(branchProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_push",
            "Pushes commits to a remote repository. Pushes the current branch to origin by default.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitPullTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var remoteProp = new JsonObject();
        remoteProp.Set("type", RuntimeValue.String("string"));
        remoteProp.Set("description", RuntimeValue.String("Optional: Remote name (default: 'origin')"));
        remoteProp.Set("default", RuntimeValue.String("origin"));
        properties.Set("remote", RuntimeValue.Object(remoteProp));
        
        var branchProp = new JsonObject();
        branchProp.Set("type", RuntimeValue.String("string"));
        branchProp.Set("description", RuntimeValue.String("Optional: Branch name to pull (defaults to current branch)"));
        properties.Set("branch", RuntimeValue.Object(branchProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_pull",
            "Pulls changes from a remote repository. Pulls from origin for the current branch by default.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
}
