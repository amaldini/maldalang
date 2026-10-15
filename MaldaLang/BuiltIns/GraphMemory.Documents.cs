// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using ValueType = MaldaLang.Interpreter.ValueType;
using System.Text.Json;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;

public partial class GraphMemoryInstance
{
    private static string ComputeContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
    
    private static string? NormalizeIndexedFilePath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;
        return sourcePath.Replace('\\', '/').Trim();
    }
    
    private bool HasIndexedFileWithHash(string? filePath, string fileHash, string scope)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;
        
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            
            if (!string.Equals(GetMetadataString(nodeObj, "source"), "file", StringComparison.OrdinalIgnoreCase))
                continue;
            
            if (!string.Equals(GetNodeScope(nodeObj), scope, StringComparison.OrdinalIgnoreCase))
                continue;
            
            var storedPath = GetMetadataString(nodeObj, "filePath");
            if (string.IsNullOrEmpty(storedPath))
            {
                var contextVal = nodeObj.Get("context", null);
                if (contextVal != null && contextVal.Type == ValueType.String)
                    storedPath = NormalizeIndexedFilePath(contextVal.AsString().Split('#')[0]);
            }
            
            if (!string.Equals(storedPath, filePath, StringComparison.OrdinalIgnoreCase))
                continue;
            
            var storedHash = GetMetadataString(nodeObj, "fileHash");
            if (string.Equals(storedHash, fileHash, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        return false;
    }
    
    private int RemoveIndexedNodesForFile(string filePath, string scope)
    {
        var toRemove = new List<string>();
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            
            if (!string.Equals(GetMetadataString(nodeObj, "source"), "file", StringComparison.OrdinalIgnoreCase))
                continue;
            
            if (!string.Equals(GetNodeScope(nodeObj), scope, StringComparison.OrdinalIgnoreCase))
                continue;
            
            var storedPath = GetMetadataString(nodeObj, "filePath");
            if (string.IsNullOrEmpty(storedPath))
            {
                var contextVal = nodeObj.Get("context", null);
                if (contextVal != null && contextVal.Type == ValueType.String)
                    storedPath = NormalizeIndexedFilePath(contextVal.AsString().Split('#')[0]);
            }
            
            if (string.Equals(storedPath, filePath, StringComparison.OrdinalIgnoreCase))
                toRemove.Add(kvp.Key);
        }
        
        var removed = 0;
        foreach (var nodeId in toRemove)
        {
            if (RemoveNodeById(nodeId))
                removed++;
        }
        
        return removed;
    }

    private RuntimeValue CallIndexDocuments(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("indexDocuments() expects (pattern, dirPath?, options?)");
        
        EnsureInitialized();
        
        var result = IndexDocumentsInternal(args, forceChangedOnly: null);
        return RuntimeValue.Integer(result.Indexed);
    }
    
    private RuntimeValue CallReindexDocuments(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("reindexDocuments() expects (pattern, dirPath?, options?)");
        
        EnsureInitialized();
        
        var result = IndexDocumentsInternal(args, forceChangedOnly: true);
        var obj = new JsonObject();
        obj.Set("indexed", RuntimeValue.Integer(result.Indexed));
        obj.Set("skipped", RuntimeValue.Integer(result.Skipped));
        obj.Set("removed", RuntimeValue.Integer(result.Removed));
        return RuntimeValue.Object(obj);
    }
    
    private (int Indexed, int Skipped, int Removed) IndexDocumentsInternal(List<RuntimeValue> args, bool? forceChangedOnly)
    {
        ParseIndexDocumentsArgs(args, out var loadArgs, out var chunkSize, out var overlap, out var scope, out var changedOnly);
        if (forceChangedOnly != null)
            changedOnly = forceChangedOnly.Value;
        var documents = AiPipelineHelpers.LoadDocuments(loadArgs);
        if (documents.Type != ValueType.Array)
            return (0, 0, 0);
        
        var indexed = 0;
        var skipped = 0;
        var removed = 0;
        foreach (var document in documents.AsArray())
        {
            if (!TryExtractDocumentContent(document, out var content, out var sourcePath))
                continue;
            
            if (string.IsNullOrWhiteSpace(content))
                continue;
            
            var normalizedPath = NormalizeIndexedFilePath(sourcePath);
            var fileHash = ComputeContentHash(content);
            if (changedOnly && HasIndexedFileWithHash(normalizedPath, fileHash, scope))
            {
                skipped++;
                continue;
            }
            
            if (!string.IsNullOrEmpty(normalizedPath))
                removed += RemoveIndexedNodesForFile(normalizedPath, scope);
            
            var chunks = BuildDocumentChunks(document, content, chunkSize, overlap);
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunkText = chunks[i];
                if (chunkText.Length > MaxIndexedDocumentChars)
                    chunkText = chunkText.Substring(0, MaxIndexedDocumentChars);
                
                var contextLabel = chunks.Count == 1
                    ? normalizedPath ?? ""
                    : $"{normalizedPath ?? "document"}#chunk-{i + 1}";
                
                var metadata = new JsonObject();
                metadata.Set("type", RuntimeValue.String("semantic"));
                metadata.Set("source", RuntimeValue.String("file"));
                metadata.Set("scope", RuntimeValue.String(scope));
                if (!string.IsNullOrEmpty(normalizedPath))
                    metadata.Set("filePath", RuntimeValue.String(normalizedPath));
                metadata.Set("fileHash", RuntimeValue.String(fileHash));
                
                CallRemember(new List<RuntimeValue>
                {
                    RuntimeValue.String(chunkText),
                    RuntimeValue.String(contextLabel),
                    RuntimeValue.Object(metadata)
                });
                indexed++;
            }
        }
        
        if (skipped > 0)
            System.Diagnostics.Debug.WriteLine($"indexDocuments skipped {skipped} unchanged file(s)");
        
        return (indexed, skipped, removed);
    }
    
    private List<string> BuildDocumentChunks(RuntimeValue document, string content, int chunkSize, int overlap)
    {
        if (chunkSize <= 0 || content.Length <= chunkSize)
            return new List<string> { content };
        
        var splitResult = AiPipelineHelpers.SplitDocuments(new List<RuntimeValue>
        {
            RuntimeValue.Array(new List<RuntimeValue> { document }),
            RuntimeValue.Integer(chunkSize),
            RuntimeValue.Integer(overlap)
        });
        
        if (splitResult.Type != ValueType.Array)
            return new List<string> { content };
        
        var chunks = new List<string>();
        foreach (var chunk in splitResult.AsArray())
        {
            if (TryExtractDocumentContent(chunk, out var chunkContent, out _)
                && !string.IsNullOrWhiteSpace(chunkContent))
                chunks.Add(chunkContent);
        }
        
        return chunks.Count > 0 ? chunks : new List<string> { content };
    }
    
    private static void ParseIndexDocumentsArgs(List<RuntimeValue> args, out List<RuntimeValue> loadArgs, out int chunkSize, out int overlap, out string scope, out bool changedOnly)
    {
        loadArgs = new List<RuntimeValue> { args[0] };
        chunkSize = DefaultIndexChunkSize;
        overlap = DefaultIndexChunkOverlap;
        scope = "global";
        changedOnly = false;
        
        JsonObject? options = null;
        if (args.Count >= 2)
        {
            if (args[1].Type == ValueType.String)
            {
                loadArgs.Add(args[1]);
                if (args.Count >= 3 && args[2].Type == ValueType.Object)
                    options = RequireJsonObject(args[2], "indexDocuments() options must be an object");
            }
            else if (args[1].Type == ValueType.Object)
            {
                loadArgs.Add(RuntimeValue.String("."));
                options = RequireJsonObject(args[1], "indexDocuments() options must be an object");
            }
        }
        
        if (options != null)
        {
            chunkSize = GetIntOption(options, "chunkSize", chunkSize);
            overlap = GetIntOption(options, "overlap", overlap);
            var scopeOpt = GetStringOption(options, "scope");
            if (scopeOpt != null)
                scope = scopeOpt;
            changedOnly = GetBoolOption(options, "changedOnly", false);
        }
    }
    
    private static bool TryExtractDocumentContent(RuntimeValue document, out string content, out string? sourcePath)
    {
        content = "";
        sourcePath = null;
        
        if (document.AsObject() is DocumentInstance doc)
        {
            content = doc.Content;
            sourcePath = doc.GetMetadataString("source");
            return true;
        }
        
        if (document.Type == ValueType.Object && document.AsObject() is JsonObject docObj)
        {
            var contentVal = docObj.Get("content");
            if (contentVal.Type == ValueType.String)
                content = contentVal.AsString();
            
            var metadataVal = docObj.Get("metadata");
            if (metadataVal.Type == ValueType.Object && metadataVal.AsObject() is JsonObject metaObj)
            {
                var sourceVal = metaObj.Get("source");
                if (sourceVal.Type == ValueType.String)
                    sourcePath = sourceVal.AsString();
            }
            
            return content.Length > 0;
        }
        
        return false;
    }

    private RuntimeValue CallStartKbWatch(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("startKbWatch() expects (dir, pattern?, options?)");
        EnsureInitialized();
        _kbWatchService?.Dispose();
        var dir = args[0].AsString();
        var pattern = "**/*.md";
        JsonObject? options = null;
        if (args.Count >= 2)
        {
            if (args[1].Type == ValueType.String)
                pattern = args[1].AsString();
            else if (args[1].Type == ValueType.Object)
                options = RequireJsonObject(args[1], "startKbWatch() options must be an object");
        }
        if (args.Count >= 3 && args[2].Type == ValueType.Object)
            options = RequireJsonObject(args[2], "startKbWatch() options must be an object");
        var scope = GetStringOption(options, "scope") ?? "global";
        _kbWatchSavePath = GetStringOption(options, "savePath");
        var debounceMs = GetIntOption(options, "debounceMs", 2000);
        var patternFromOpts = GetStringOption(options, "pattern");
        if (!string.IsNullOrWhiteSpace(patternFromOpts))
            pattern = patternFromOpts!;
        _kbWatchService = new KbWatchService(dir, pattern, () =>
        {
            lock (_lock)
            {
                try
                {
                    var reindexOpts = new JsonObject();
                    reindexOpts.Set("changedOnly", RuntimeValue.Boolean(true));
                    reindexOpts.Set("scope", RuntimeValue.String(scope));
                    CallReindexDocuments(new List<RuntimeValue>
                    {
                        RuntimeValue.String(pattern),
                        RuntimeValue.String(dir),
                        RuntimeValue.Object(reindexOpts)
                    });
                    if (!string.IsNullOrWhiteSpace(_kbWatchSavePath))
                        CallSave(new List<RuntimeValue> { RuntimeValue.String(_kbWatchSavePath!) });
                }
                catch
                {
                }
            }
        }, debounceMs);
        _kbWatchService.Start();
        return RuntimeValue.Boolean(true);
    }

    private RuntimeValue CallStopKbWatch(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new RuntimeException("stopKbWatch() expects no arguments");
        _kbWatchService?.Dispose();
        _kbWatchService = null;
        _kbWatchSavePath = null;
        return RuntimeValue.Boolean(true);
    }
}
