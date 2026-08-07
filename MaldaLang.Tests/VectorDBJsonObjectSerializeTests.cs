// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// GraphMemory stores JsonObject payloads in VectorDB. Serialize must preserve nodeId
/// so searchSimilar hits can be mapped after load (otherwise ASK shows vec 0 · lex …).
/// </summary>
[Collection("Sequential")]
public class VectorDBJsonObjectSerializeTests
{
    [Fact]
    public void Serialize_PreservesJsonObjectNodeId_RoundTrip()
    {
        var interpreter = new Interpreter.Interpreter();
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_vdb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "idx.vectordb.bin");
        try
        {
            var vdb = new VectorDBInstance(4, "single");
            var embedFn = new FunctionValue
            {
                TranspiledDelegate = async arg =>
                {
                    await System.Threading.Tasks.Task.CompletedTask;
                    return new List<object> { 1.0, 0.0, 0.0, 0.0 };
                }
            };
            vdb.CallMethod("init", new List<RuntimeValue> { RuntimeValue.Function(embedFn) }, interpreter);

            var data = new JsonObject();
            data.Set("nodeId", RuntimeValue.String("node_42"));
            data.Set("description", RuntimeValue.String("zebra fact"));
            vdb.CallMethod("add", new List<RuntimeValue>
            {
                RuntimeValue.Array(new List<RuntimeValue>
                {
                    RuntimeValue.Float(1), RuntimeValue.Float(0), RuntimeValue.Float(0), RuntimeValue.Float(0)
                }),
                RuntimeValue.Object(data)
            }, interpreter);

            vdb.CallMethod("serialize", new List<RuntimeValue> { RuntimeValue.String(path) }, interpreter);

            var loadedRv = vdb.CallMethod("deserialize", new List<RuntimeValue> { RuntimeValue.String(path) }, interpreter);
            var loaded = Assert.IsType<VectorDBInstance>(loadedRv.AsObject());
            loaded.CallMethod("init", new List<RuntimeValue> { RuntimeValue.Function(embedFn) }, interpreter);

            Assert.Contains("node_42", loaded.CollectIndexedNodeIds());

            var hits = loaded.CallMethod("searchSimilar", new List<RuntimeValue>
            {
                RuntimeValue.String("zebra"),
                RuntimeValue.Integer(1)
            }, interpreter);
            Assert.Equal(MaldaLang.Interpreter.ValueType.Array, hits.Type);
            Assert.True(hits.AsArray().Count >= 1);

            var hit = hits.AsArray()[0].AsObject();
            Assert.True(hit is JsonObject or DictionaryInstance);
            RuntimeValue dataVal;
            if (hit is JsonObject jo)
                dataVal = jo.Get("data");
            else
                Assert.True(((DictionaryInstance)hit).TryGetEntry("data", out dataVal));

            Assert.Equal(MaldaLang.Interpreter.ValueType.Object, dataVal.Type);
            var payload = dataVal.AsObject();
            string? nodeId = null;
            if (payload is JsonObject pJo)
                nodeId = pJo.Get("nodeId").AsString();
            else if (payload is DictionaryInstance pDict && pDict.TryGetEntry("nodeId", out var idVal))
                nodeId = idVal.AsString();
            Assert.Equal("node_42", nodeId);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void GraphMemory_Load_RepairsUnmappedVectors_AndReportsVectorScore()
    {
        var interpreter = new Interpreter.Interpreter();
        var memory = new GraphMemoryInstance();
        memory.SetInterpreter(interpreter);
        memory.CallMethod("initialize", new List<RuntimeValue>
        {
            RuntimeValue.Integer(8),
            RuntimeValue.String("single")
        }, interpreter);

        var meta = new JsonObject();
        meta.Set("type", RuntimeValue.String("semantic"));
        meta.Set("filePath", RuntimeValue.String("notes/zebra.md"));
        memory.CallMethod("remember", new List<RuntimeValue>
        {
            RuntimeValue.String("unique zebra vector repair token ZZVDBFIX1"),
            RuntimeValue.String("animals"),
            RuntimeValue.Object(meta)
        }, interpreter);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_gm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "brain_memory");
        try
        {
            memory.CallMethod("save", new List<RuntimeValue> { RuntimeValue.String(basePath) }, interpreter);

            // Corrupt vectordb payloads the way old builds did: keep vectors, wipe data objects.
            CorruptVectorPayloadsToEmptyObjects(basePath + ".vectordb.bin");

            var reloaded = new GraphMemoryInstance();
            reloaded.SetInterpreter(interpreter);
            reloaded.CallMethod("initialize", new List<RuntimeValue>
            {
                RuntimeValue.Integer(8),
                RuntimeValue.String("single")
            }, interpreter);
            reloaded.CallMethod("load", new List<RuntimeValue> { RuntimeValue.String(basePath) }, interpreter);

            var options = new JsonObject();
            options.Set("hybridLexical", RuntimeValue.Boolean(true));
            options.Set("lexicalMode", RuntimeValue.String("bm25"));
            options.Set("lexicalMinScore", RuntimeValue.Float(0));
            options.Set("minScore", RuntimeValue.Float(0));
            options.Set("type", RuntimeValue.String("semantic"));
            options.Set("explain", RuntimeValue.Boolean(true));

            var results = reloaded.CallMethod("query", new List<RuntimeValue>
            {
                RuntimeValue.String("ZZVDBFIX1 zebra"),
                RuntimeValue.Integer(3),
                RuntimeValue.Object(options)
            }, interpreter);

            Assert.True(results.AsArray().Count >= 1);
            var first = Assert.IsType<JsonObject>(results.AsArray()[0].AsObject());
            var explain = Assert.IsType<JsonObject>(first.Get("explain").AsObject());
            Assert.True(explain.Get("vectorScore").AsFloat() > 0.0,
                "After repair, top hit must carry a non-zero vector score");

            var diag = Assert.IsType<JsonObject>(
                reloaded.CallMethod("getLastQueryDiagnostics", new List<RuntimeValue>(), interpreter).AsObject());
            Assert.True(diag.Get("vectorCandidates").AsInteger() >= 1);
            Assert.True(diag.Get("embedReady").AsBoolean());
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Rewrites each entry's data blob to "{}" while keeping vectors — simulates the pre-fix serializer.
    /// </summary>
    private static void CorruptVectorPayloadsToEmptyObjects(string vectordbPath)
    {
        var bytes = File.ReadAllBytes(vectordbPath);
        using var input = new MemoryStream(bytes);
        using var reader = new BinaryReader(input);
        var magic = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(4));
        Assert.Equal("VDB2", magic);
        var dimension = reader.ReadInt32();
        var precisionByte = reader.ReadByte();
        var entryCount = reader.ReadInt32();

        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(System.Text.Encoding.UTF8.GetBytes("VDB2"));
            writer.Write(dimension);
            writer.Write(precisionByte);
            writer.Write(entryCount);
            for (var i = 0; i < entryCount; i++)
            {
                if (precisionByte == 0)
                {
                    for (var j = 0; j < dimension; j++)
                        writer.Write(reader.ReadSingle());
                }
                else
                {
                    for (var j = 0; j < dimension; j++)
                        writer.Write(reader.ReadDouble());
                }

                var dataLength = reader.ReadInt32();
                reader.ReadBytes(dataLength);
                var empty = System.Text.Encoding.UTF8.GetBytes("{}");
                writer.Write(empty.Length);
                writer.Write(empty);
            }
        }

        File.WriteAllBytes(vectordbPath, output.ToArray());
    }
}
