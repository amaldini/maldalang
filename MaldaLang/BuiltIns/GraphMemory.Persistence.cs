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
    /// <summary>
    /// Resolves the base path for memory artifact files ({base}.graph.json, etc.).
    /// Dot-prefixed paths (e.g. .ralph-memory) must not use Path.ChangeExtension on Windows,
    /// where it incorrectly strips to the parent directory.
    /// </summary>
    private static string ResolveMemoryBasePath(string filePath)
    {
        if (EmbeddedFolderStore.IsEmbedPath(filePath))
        {
            // Avoid System.IO.Path APIs that mis-handle the embed: scheme on Windows.
            var normalized = filePath.Replace('\\', '/');
            ReadOnlySpan<string> suffixes =
            [
                ".graph.json",
                ".metadata.json",
                ".vectordb.bin",
                ".bundle.json",
                ".mem"
            ];
            foreach (var suffix in suffixes)
            {
                if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return normalized.Substring(0, normalized.Length - suffix.Length);
            }

            return normalized;
        }

        var fileName = Path.GetFileName(filePath);
        if (!string.IsNullOrEmpty(fileName) && fileName.StartsWith('.'))
            return filePath;

        var withoutExtension = Path.ChangeExtension(filePath, null);
        if (string.IsNullOrWhiteSpace(withoutExtension))
            return filePath;

        var trimmed = withoutExtension.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0 || trimmed.EndsWith('.'))
            return filePath;

        return withoutExtension;
    }

    private static string GetMemoryArtifactDirectory(string basePath)
    {
        var dir = Path.GetDirectoryName(basePath);
        return string.IsNullOrEmpty(dir) ? "." : dir;
    }

    private static bool MemoryPathExists(string path)
    {
        if (EmbeddedFolderStore.IsEmbedPath(path))
            return EmbeddedFolderStore.HasFile(path);
        return File.Exists(path);
    }

    private static string ReadMemoryText(string path)
    {
        if (EmbeddedFolderStore.IsEmbedPath(path))
        {
            var text = EmbeddedFolderStore.ReadText(path);
            if (text == null)
                throw new RuntimeException($"Embedded memory artifact not found: {path}");
            return text;
        }

        return File.ReadAllText(path);
    }

    private static void RejectEmbedWrite(string path, string operation)
    {
        if (EmbeddedFolderStore.IsEmbedPath(path))
        {
            throw new RuntimeException(
                $"{operation} cannot write to embedded path '{path}' (embed: folders are read-only).");
        }
    }

    private static void ResolveMemoryArtifactPaths(
        string canonicalBasePath,
        out string graphPath,
        out string metadataPath,
        out string vectordbPath)
    {
        graphPath = $"{canonicalBasePath}.graph.json";
        metadataPath = $"{canonicalBasePath}.metadata.json";
        vectordbPath = $"{canonicalBasePath}.vectordb.bin";

        // embed: paths must not use Path.Combine legacy fallback (breaks the scheme).
        if (EmbeddedFolderStore.IsEmbedPath(canonicalBasePath))
            return;

        if (File.Exists(graphPath))
            return;

        var dir = GetMemoryArtifactDirectory(canonicalBasePath);
        var legacyGraph = Path.Combine(dir, ".graph.json");
        if (!File.Exists(legacyGraph))
            return;

        graphPath = legacyGraph;
        metadataPath = Path.Combine(dir, ".metadata.json");
        vectordbPath = Path.Combine(dir, ".vectordb.bin");
    }

    private RuntimeValue CallExportBundle(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("exportBundle() expects 1 string argument (filePath)");
        
        EnsureInitialized();
        
        var filePath = args[0].AsString();
        var basePath = ResolveMemoryBasePath(filePath);
        RejectEmbedWrite(basePath, "exportBundle()");
        WriteMemoryArtifacts(basePath);
        
        ResolveMemoryArtifactPaths(basePath, out var graphPath, out var metadataPath, out var vectordbPath);
        var manifestPath = $"{basePath}.bundle.json";
        var manifest = new Dictionary<string, object>
        {
            ["version"] = BundleManifestVersion,
            ["exportedAt"] = DateTime.UtcNow.ToString("O"),
            ["nodes"] = _nodeMetadata.Count,
            ["artifacts"] = new Dictionary<string, string>
            {
                ["graph"] = graphPath,
                ["metadata"] = metadataPath,
                ["vectordb"] = vectordbPath
            }
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        
        return RuntimeValue.String(manifestPath);
    }
    
    private RuntimeValue CallImportBundle(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("importBundle() expects 1 string argument (filePath)");
        
        var filePath = args[0].AsString();
        var basePath = ResolveMemoryBasePath(filePath);
        var manifestPath = $"{basePath}.bundle.json";
        
        if (!MemoryPathExists(manifestPath))
            throw new RuntimeException($"importBundle() manifest not found: {manifestPath}");
        
        ResolveMemoryArtifactPaths(basePath, out var graphPath, out var metadataPath, out var vectordbPath);
        if (!MemoryPathExists(graphPath) || !MemoryPathExists(metadataPath) || !MemoryPathExists(vectordbPath))
            throw new RuntimeException("importBundle() requires graph, metadata, and vectordb artifacts for the bundle base path");
        
        return CallLoad(args);
    }
    
    private static bool ResolveBackupEnabled(JsonObject? options)
    {
        if (options != null)
        {
            var backupVal = options.Get("backup", null);
            if (backupVal != null && backupVal.Type == ValueType.Boolean)
                return backupVal.AsBoolean();
        }

        var env = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_BACKUP");
        return string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveMaxBackups(JsonObject? options)
    {
        if (options != null)
        {
            var maxVal = options.Get("maxBackups", null);
            if (maxVal != null && maxVal.Type == ValueType.Integer && maxVal.AsInteger() > 0)
                return maxVal.AsInteger();
        }

        var env = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_MAX_BACKUPS");
        if (int.TryParse(env, out var parsed) && parsed > 0)
            return parsed;
        return 5;
    }

    private static (bool Enabled, int MaxBackups) ResolveBackupOptions(JsonObject? options) =>
        (ResolveBackupEnabled(options), ResolveMaxBackups(options));

    private static void MaybeRotateBackups(string basePath, (bool Enabled, int MaxBackups) backupOptions)
    {
        if (!backupOptions.Enabled)
            return;

        ResolveMemoryArtifactPaths(basePath, out var graphPath, out var metadataPath, out var vectordbPath);
        if (!File.Exists(graphPath) && !File.Exists(metadataPath) && !File.Exists(vectordbPath))
            return;

        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss");
        CopyIfExists(graphPath, $"{basePath}.backup.{stamp}.graph.json");
        CopyIfExists(metadataPath, $"{basePath}.backup.{stamp}.metadata.json");
        CopyIfExists(vectordbPath, $"{basePath}.backup.{stamp}.vectordb.bin");
        PruneRotatingBackups(basePath, backupOptions.MaxBackups);
    }

    private static void CopyIfExists(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
            return;
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static void PruneRotatingBackups(string basePath, int maxBackups)
    {
        if (maxBackups <= 0)
            return;

        var dir = GetMemoryArtifactDirectory(basePath);
        var fileName = Path.GetFileName(basePath);
        if (string.IsNullOrEmpty(fileName))
            fileName = basePath;

        var stamps = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(dir, $"{fileName}.backup.*.graph.json"))
        {
            var name = Path.GetFileName(path);
            var prefix = $"{fileName}.backup.";
            var suffix = ".graph.json";
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
                continue;
            var stamp = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            if (!string.IsNullOrWhiteSpace(stamp))
                stamps.Add(stamp);
        }

        while (stamps.Count > maxBackups)
        {
            var oldest = stamps.Min!;
            stamps.Remove(oldest);
            DeleteIfExists($"{basePath}.backup.{oldest}.graph.json");
            DeleteIfExists($"{basePath}.backup.{oldest}.metadata.json");
            DeleteIfExists($"{basePath}.backup.{oldest}.vectordb.bin");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private void WriteMemoryArtifacts(string basePath)
    {
        RejectEmbedWrite(basePath, "save()");
        var graphJson = _knowledgeGraph!.CallMethod("serialize", new List<RuntimeValue>(), _interpreter!).AsString();
        File.WriteAllText($"{basePath}.graph.json", graphJson);
        
        _nodeIndex!.CallMethod("serialize", new List<RuntimeValue> { RuntimeValue.String($"{basePath}.vectordb.bin") }, _interpreter!);
        
        var metadataDict = new Dictionary<string, object>();
        foreach (var kvp in _nodeMetadata)
            metadataDict[kvp.Key] = ConvertRuntimeValueToJson(kvp.Value);
        
        var metadataJson = JsonSerializer.Serialize(metadataDict, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText($"{basePath}.metadata.json", metadataJson);
    }
    
    private RuntimeValue CallSave(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("save() expects (filePath, options?)");
        
        EnsureInitialized();
        
        JsonObject? saveOptions = null;
        if (args.Count >= 2)
        {
            saveOptions = RequireJsonObject(args[1], "save() optional second argument must be options object");
        }

        var filePath = args[0].AsString();
        var basePath = ResolveMemoryBasePath(filePath);
        RejectEmbedWrite(basePath, "save()");
        MaybeRotateBackups(basePath, ResolveBackupOptions(saveOptions));
        WriteMemoryArtifacts(basePath);
        
        return RuntimeValue.Null();
    }
    
    private RuntimeValue CallLoad(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("load() expects 1 string argument (filePath, options?)");
        JsonObject? loadOptions = null;
        if (args.Count >= 2)
        {
            loadOptions = RequireJsonObject(args[1], "load() optional second argument must be options object");
        }
        var migrateDualIndex = GetBoolOption(loadOptions, "migrateDualIndex", true);
        var filePath = args[0].AsString();
        var basePath = ResolveMemoryBasePath(filePath);
        ResolveMemoryArtifactPaths(basePath, out var graphPath, out var metadataPath, out var vectordbPath);
        
        var initialized = false;
        
        // Load graph
        if (MemoryPathExists(graphPath))
        {
            var graphJson = ReadMemoryText(graphPath);
            InitializeWithPreservedEmbedding(new List<RuntimeValue>());
            initialized = true;
            var graphResult = _knowledgeGraph!.CallMethod("deserialize", new List<RuntimeValue> { RuntimeValue.String(graphJson) }, _interpreter!);
            if (graphResult.Type == ValueType.Object && graphResult.AsObject() is GraphInstance loadedGraph)
                _knowledgeGraph = loadedGraph;
        }
        
        // Load VectorDB
        if (MemoryPathExists(vectordbPath))
        {
            if (!initialized)
            {
                InitializeWithPreservedEmbedding(new List<RuntimeValue>());
                initialized = true;
            }

            var vdbResult = _nodeIndex!.CallMethod("deserialize", new List<RuntimeValue> { RuntimeValue.String(vectordbPath) }, _interpreter!);
            if (vdbResult.Type == ValueType.Object && vdbResult.AsObject() is VectorDBInstance loadedVdb)
            {
                _nodeIndex = loadedVdb;
                if (loadedVdb.Dimension > 0)
                    _currentDimension = loadedVdb.Dimension;
                var embedFunc = CreateEmbeddingWrapper(_currentDimension, _interpreter!, _customEmbeddingFunction);
                if (embedFunc != null)
                {
                    try
                    {
                        _nodeIndex.CallMethod("init", new List<RuntimeValue> { RuntimeValue.Function(embedFunc) }, _interpreter!);
                    }
                    catch
                    {
                        // Vector search still works with stored vectors if init fails
                    }
                }
            }
        }
        
        if (!initialized && MemoryPathExists(metadataPath))
            InitializeWithPreservedEmbedding(new List<RuntimeValue>());
        
        // Load metadata
        if (MemoryPathExists(metadataPath))
        {
            var metadataJson = ReadMemoryText(metadataPath);
            var doc = System.Text.Json.JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    var key = prop.Name;
                    var valueJson = prop.Value;
                    // Convert JSON back to RuntimeValue
                    var runtimeValue = JsonToRuntimeValue(valueJson);
                    if (runtimeValue.Type == ValueType.Object && runtimeValue.AsObject() is JsonObject nodeObj)
                    {
                        var nodeIdVal = nodeObj.Get("nodeId", null);
                        if (nodeIdVal == null || nodeIdVal.Type != ValueType.String)
                            nodeObj.Set("nodeId", RuntimeValue.String(key));
                        runtimeValue = RuntimeValue.Object(nodeObj);
                    }
                    _nodeMetadata[key] = runtimeValue;
                }
            }
        }
        
        RestoreNodeIdCounter();
        if (migrateDualIndex)
            MigrateDualIndexEntries();
        RepairUnmappedVectorIndex();
        RebuildBm25Index();

        return RuntimeValue.Null();
    }

    /// <summary>
    /// Older VectorDB saves wrote JsonObject payloads as "{}". Those entries still have
    /// vectors but no nodeId mapping, so hybrid ASK falls back to BM25-only (vec 0).
    /// Rebuild the vector index from metadata when nothing is mapped.
    /// </summary>
    private void RepairUnmappedVectorIndex()
    {
        if (_nodeIndex == null || _interpreter == null || _nodeMetadata.Count == 0)
            return;

        var mapped = _nodeIndex.CollectIndexedNodeIds();
        if (mapped.Count > 0)
            return;

        // Orphan vectors cannot be matched; drop them before re-embedding from metadata.
        _nodeIndex.ClearEntries();
        _currentDimension = _nodeIndex.Dimension > 0 ? _nodeIndex.Dimension : _currentDimension;

        foreach (var kvp in _nodeMetadata.ToList())
        {
            var nodeId = kvp.Key;
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            var factVal = nodeObj.Get("fact", null);
            if (factVal == null || factVal.Type == ValueType.Null)
                continue;
            var description = GetStoredDescription(nodeId);
            IndexNodeVector(nodeId, factVal, description);
        }
    }

    private void MigrateDualIndexEntries()
    {
        foreach (var kvp in _nodeMetadata.ToList())
        {
            var nodeId = kvp.Key;
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            var migratedVal = nodeObj.Get("dualIndexMigrated", null);
            if (migratedVal != null && migratedVal.Type == ValueType.Boolean && migratedVal.AsBoolean())
                continue;
            var factVal = nodeObj.Get("fact", null);
            if (factVal == null || factVal.Type == ValueType.Null)
                continue;
            _nodeIndex!.RemoveEntriesForNodeId(nodeId);
            var description = GetStoredDescription(nodeId);
            IndexNodeVector(nodeId, factVal, description);
            nodeObj.Set("dualIndexMigrated", RuntimeValue.Boolean(true));
            _nodeMetadata[nodeId] = RuntimeValue.Object(nodeObj);
            if (_knowledgeGraph != null && _interpreter != null)
            {
                _knowledgeGraph.CallMethod("setNodeData", new List<RuntimeValue>
                {
                    RuntimeValue.String(nodeId),
                    RuntimeValue.Object(nodeObj)
                }, _interpreter);
            }
        }
    }
}
