// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

public static partial class BuiltInTools
{
    public static RuntimeValue CreateReadFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to read. Use a relative path (e.g. \"current.malda\", \"MALDA_SPEC.md\") when the tool has a working directory."));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        var startLineProp = new JsonObject();
        startLineProp.Set("type", RuntimeValue.String("integer"));
        startLineProp.Set("description", RuntimeValue.String("Optional: Starting line number (1-indexed). If not provided, reads entire file."));
        properties.Set("startLine", RuntimeValue.Object(startLineProp));
        
        var endLineProp = new JsonObject();
        endLineProp.Set("type", RuntimeValue.String("integer"));
        endLineProp.Set("description", RuntimeValue.String("Optional: Ending line number (1-indexed, inclusive). If not provided and startLine is provided, reads to end of file."));
        properties.Set("endLine", RuntimeValue.Object(endLineProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("filePath") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "read_file",
            "Reads the contents of a file as a string. If startLine is provided, reads only the specified line range (1-indexed). If both startLine and endLine are provided, reads lines from startLine to endLine (inclusive).",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateWriteFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to write"));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        var contentProp = new JsonObject();
        contentProp.Set("type", RuntimeValue.String("string"));
        contentProp.Set("description", RuntimeValue.String("Content to write to the file. Maximum length: 6,000 characters. Use ONLY for new small files (under ~200 lines). For existing or large files use edit_file or replace_in_file instead — large write_file JSON is truncated by the LLM and will fail."));
        contentProp.Set("maxLength", RuntimeValue.Integer(6000));
        properties.Set("content", RuntimeValue.Object(contentProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> 
        { 
            RuntimeValue.String("filePath"), 
            RuntimeValue.String("content") 
        };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "write_file",
            "Writes content to a file (create or overwrite). For NEW small files only (under ~200 lines). For existing/large files use edit_file or replace_in_file — full write_file payloads are truncated by the LLM and rejected.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateReplaceInFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to edit"));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        var oldTextProp = new JsonObject();
        oldTextProp.Set("type", RuntimeValue.String("string"));
        oldTextProp.Set("description", RuntimeValue.String("The exact text to find and replace (do NOT include context lines - just the substring to replace). Matching is robust to whitespace differences. Maximum length: 50,000 characters. IMPORTANT: If oldText or newText exceeds ~2000 characters, split the replacement into multiple smaller chunks to avoid API truncation. CRITICAL: oldText must contain actual code/text content, NOT just whitespace or newlines. Do not use excessive consecutive newlines (max 10)."));
        oldTextProp.Set("maxLength", RuntimeValue.Integer(50000));
        properties.Set("oldText", RuntimeValue.Object(oldTextProp));
        
        var newTextProp = new JsonObject();
        newTextProp.Set("type", RuntimeValue.String("string"));
        newTextProp.Set("description", RuntimeValue.String("The replacement text. Maximum length: 50,000 characters. IMPORTANT: If oldText or newText exceeds ~2000 characters, split the replacement into multiple smaller chunks to avoid API truncation. For large replacements, make multiple replace_in_file calls with smaller chunks."));
        newTextProp.Set("maxLength", RuntimeValue.Integer(50000));
        properties.Set("newText", RuntimeValue.Object(newTextProp));
        
        var contextLinesProp = new JsonObject();
        contextLinesProp.Set("type", RuntimeValue.String("integer"));
        contextLinesProp.Set("description", RuntimeValue.String("Number of context lines before and after the oldText match to use for disambiguation (default: 3)"));
        contextLinesProp.Set("default", RuntimeValue.Integer(3));
        properties.Set("contextLines", RuntimeValue.Object(contextLinesProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> 
        { 
            RuntimeValue.String("filePath"), 
            RuntimeValue.String("oldText"),
            RuntimeValue.String("newText")
        };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "replace_in_file",
            "Replaces a SINGLE substring in a file. Tries an exact match first; if oldText appears more than once, the call fails unless fuzzy matching finds exactly one unambiguous occurrence (contextLines can disambiguate when only one match has unique surrounding lines). Whitespace-only differences (spaces, tabs, indentation) may match in fuzzy mode. For multiple replacements, use edit_file instead. IMPORTANT: For large replacements (oldText or newText > 2000 characters), split into multiple smaller replace_in_file calls to prevent API response truncation. Process chunks sequentially, working from top to bottom of the file. CRITICAL: oldText must contain actual code/text content, NOT just whitespace or excessive newlines (max 10 consecutive newlines). If a replace fails, use a longer, unique oldText snippet.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateListDirectoryTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var dirPathProp = new JsonObject();
        dirPathProp.Set("type", RuntimeValue.String("string"));
        dirPathProp.Set("description", RuntimeValue.String("Path to the directory (use '.' for current directory)"));
        properties.Set("dirPath", RuntimeValue.Object(dirPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("dirPath") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "list_directory",
            "Lists files and directories. Use this instead of powershell Get-ChildItem, cmd dir, or findstr. Returns an array of objects with name, type (file/directory), and path.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateDeleteFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();

        var properties = new JsonObject();
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to delete. Use a relative path when the tool has a working directory."));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("filePath") }));

        tool.Initialize(
            "delete_file",
            "Deletes a file. Paths stay inside the working directory. Returns { success: boolean, error?: string }. Succeeds when the file is already missing.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );

        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateCopyFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();

        var properties = new JsonObject();

        var srcPathProp = new JsonObject();
        srcPathProp.Set("type", RuntimeValue.String("string"));
        srcPathProp.Set("description", RuntimeValue.String("Path of the file to copy. Use a relative path when the tool has a working directory."));
        properties.Set("srcPath", RuntimeValue.Object(srcPathProp));

        var destPathProp = new JsonObject();
        destPathProp.Set("type", RuntimeValue.String("string"));
        destPathProp.Set("description", RuntimeValue.String("Destination path. Use a relative path when the tool has a working directory. Both srcPath and destPath must stay inside the working directory."));
        properties.Set("destPath", RuntimeValue.Object(destPathProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("srcPath"),
            RuntimeValue.String("destPath")
        }));

        tool.Initialize(
            "copy_file",
            "Copies a file. Both srcPath and destPath must stay inside the working directory. Returns { success: boolean, error?: string }.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );

        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateEnsureDirTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();

        var properties = new JsonObject();
        var dirPathProp = new JsonObject();
        dirPathProp.Set("type", RuntimeValue.String("string"));
        dirPathProp.Set("description", RuntimeValue.String("Directory to create (parents are created as needed). Use a relative path when the tool has a working directory."));
        properties.Set("dirPath", RuntimeValue.Object(dirPathProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("dirPath") }));

        tool.Initialize(
            "ensure_dir",
            "Creates a directory and any missing parents. Paths stay inside the working directory. Returns { success: boolean, path?: string, error?: string }.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );

        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateInsertAtLineTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to edit"));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        var lineNumberProp = new JsonObject();
        lineNumberProp.Set("type", RuntimeValue.String("integer"));
        lineNumberProp.Set("description", RuntimeValue.String("Line number where to insert (1-indexed). Line 0 inserts at start, line > file length appends at end"));
        properties.Set("lineNumber", RuntimeValue.Object(lineNumberProp));
        
        var contentProp = new JsonObject();
        contentProp.Set("type", RuntimeValue.String("string"));
        contentProp.Set("description", RuntimeValue.String("Content to insert (can be multi-line). Maximum length: 50,000 characters."));
        contentProp.Set("maxLength", RuntimeValue.Integer(50000));
        properties.Set("content", RuntimeValue.Object(contentProp));
        
        var insertAfterProp = new JsonObject();
        insertAfterProp.Set("type", RuntimeValue.String("boolean"));
        insertAfterProp.Set("description", RuntimeValue.String("If true, inserts after the specified line; if false, inserts before (default: false)"));
        insertAfterProp.Set("default", RuntimeValue.Boolean(false));
        properties.Set("insertAfter", RuntimeValue.Object(insertAfterProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> 
        { 
            RuntimeValue.String("filePath"),
            RuntimeValue.String("lineNumber"),
            RuntimeValue.String("content")
        };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "insertAtLine",
            "Inserts content at a specific line number in a file. Supports inserting before or after a line, with lenient edge case handling (line 0 = start, line > file length = append).",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateEditFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to edit"));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        // Define the edit object schema
        var editItemProperties = new JsonObject();
        
        var oldTextProp = new JsonObject();
        oldTextProp.Set("type", RuntimeValue.String("string"));
        oldTextProp.Set("description", RuntimeValue.String("The exact text to find and replace (do NOT include context lines - just the substring to replace). Matching is robust to whitespace differences. Maximum length: 50,000 characters. IMPORTANT: If oldText or newText exceeds ~2000 characters, split the edits into multiple smaller edit_file calls to avoid API truncation. CRITICAL: oldText must contain actual code/text content, NOT just whitespace or newlines. Do not use excessive consecutive newlines (max 10)."));
        oldTextProp.Set("maxLength", RuntimeValue.Integer(50000));
        editItemProperties.Set("oldText", RuntimeValue.Object(oldTextProp));
        
        var newTextProp = new JsonObject();
        newTextProp.Set("type", RuntimeValue.String("string"));
        newTextProp.Set("description", RuntimeValue.String("The replacement text. Maximum length: 50,000 characters. IMPORTANT: If oldText or newText exceeds ~2000 characters, split the edits into multiple smaller edit_file calls to avoid API truncation."));
        newTextProp.Set("maxLength", RuntimeValue.Integer(50000));
        editItemProperties.Set("newText", RuntimeValue.Object(newTextProp));
        
        var contextLinesProp = new JsonObject();
        contextLinesProp.Set("type", RuntimeValue.String("integer"));
        contextLinesProp.Set("description", RuntimeValue.String("Number of context lines before and after the oldText match to use for disambiguation (default: 3)"));
        contextLinesProp.Set("default", RuntimeValue.Integer(3));
        editItemProperties.Set("contextLines", RuntimeValue.Object(contextLinesProp));
        
        var editItemRequired = new List<RuntimeValue> 
        { 
            RuntimeValue.String("oldText"),
            RuntimeValue.String("newText")
        };
        
        var editItemSchema = new JsonObject();
        editItemSchema.Set("type", RuntimeValue.String("object"));
        editItemSchema.Set("properties", RuntimeValue.Object(editItemProperties));
        editItemSchema.Set("required", RuntimeValue.Array(editItemRequired));
        
        var editsProp = new JsonObject();
        editsProp.Set("type", RuntimeValue.String("array"));
        editsProp.Set("description", RuntimeValue.String("Array of edits to apply. Each edit object should have 'oldText' (string, required), 'newText' (string, required), and optionally 'contextLines' (integer, default: 3). Edits are applied sequentially in order."));
        editsProp.Set("items", RuntimeValue.Object(editItemSchema));
        properties.Set("edits", RuntimeValue.Object(editsProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> 
        { 
            RuntimeValue.String("filePath"), 
            RuntimeValue.String("edits")
        };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "edit_file",
            "Applies MULTIPLE text replacements to a file in a single operation. Use this tool when you need to make TWO OR MORE edits to the same file. For a single replacement, use replace_in_file instead (simpler API). Each edit uses the same strict matching rules as replace_in_file (exact match first; fails on ambiguous multiple matches). Returns {success: boolean, applied: integer, totalEdits: integer, error?: string, failedEdit?: integer}; success is true only when every edit was applied — on failure no changes are written (transactional). Edits are applied sequentially in memory, then written once. IMPORTANT: If any oldText or newText exceeds ~2000 characters, consider splitting into multiple edit_file calls to avoid API truncation. CRITICAL: oldText must contain actual code/text content, NOT just whitespace or excessive newlines (max 10 consecutive newlines). Always check success in the tool result before assuming edits were applied.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
}