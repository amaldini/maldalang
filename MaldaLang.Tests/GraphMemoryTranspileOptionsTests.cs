// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Portable ASK (transpile) passes object literals as <see cref="DictionaryInstance"/>
/// and embedders as <see cref="FunctionValue.TranspiledDelegate"/>. These must work
/// the same as interpreter <see cref="JsonObject"/> / declared functions.
/// </summary>
[Collection("Sequential")]
public class GraphMemoryTranspileOptionsTests
{
    [Fact]
    public void Query_AcceptsDictionaryInstanceOptions_HybridLexicalBm25()
    {
        var interpreter = new Interpreter.Interpreter();
        var memory = new GraphMemoryInstance();
        memory.SetInterpreter(interpreter);

        memory.CallMethod("initialize", new List<RuntimeValue>
        {
            RuntimeValue.Integer(64),
            RuntimeValue.String("single")
        }, interpreter);

        var meta = new DictionaryInstance();
        meta.SetEntry("type", RuntimeValue.String("semantic"));
        meta.SetEntry("filePath", RuntimeValue.String("notes/zebra.md"));
        memory.CallMethod("remember", new List<RuntimeValue>
        {
            RuntimeValue.String("unique zebra fact token ZZGMOPT99"),
            RuntimeValue.String("animals"),
            RuntimeValue.Object(meta)
        }, interpreter);

        var options = new DictionaryInstance();
        options.SetEntry("hybridLexical", RuntimeValue.Boolean(true));
        options.SetEntry("lexicalMode", RuntimeValue.String("bm25"));
        options.SetEntry("lexicalMinScore", RuntimeValue.Float(0));
        options.SetEntry("minScore", RuntimeValue.Float(0));
        options.SetEntry("type", RuntimeValue.String("semantic"));

        var results = memory.CallMethod("query", new List<RuntimeValue>
        {
            RuntimeValue.String("ZZGMOPT99 zebra"),
            RuntimeValue.Integer(5),
            RuntimeValue.Object(options)
        }, interpreter);

        Assert.Equal(MaldaLang.Interpreter.ValueType.Array, results.Type);
        Assert.True(results.AsArray().Count >= 1, "DictionaryInstance query options should enable BM25 hybrid hits");

        var diag = memory.CallMethod("getLastQueryDiagnostics", new List<RuntimeValue>(), interpreter);
        Assert.Equal(MaldaLang.Interpreter.ValueType.Object, diag.Type);
        var diagObj = Assert.IsType<JsonObject>(diag.AsObject());
        Assert.True(diagObj.Get("hybridLexical").AsBoolean());
        Assert.Equal("bm25", diagObj.Get("lexicalMode").AsString());
        Assert.True(diagObj.Get("bm25Candidates").AsInteger() >= 1);
    }

    [Fact]
    public void Initialize_AcceptsTranspiledDelegateEmbedder_AndQueryUsesIt()
    {
        var interpreter = new Interpreter.Interpreter();
        var memory = new GraphMemoryInstance();
        memory.SetInterpreter(interpreter);

        var embedCalls = 0;
        var embedFn = new FunctionValue
        {
            TranspiledDelegate = async arg =>
            {
                embedCalls++;
                var text = arg?.ToString() ?? "";
                // Deterministic 8-dim pseudo-embedding from text hash.
                var vector = new List<object>();
                unchecked
                {
                    var h = (uint)StringComparer.Ordinal.GetHashCode(text);
                    for (var i = 0; i < 8; i++)
                    {
                        h = h * 1664525u + 1013904223u;
                        vector.Add((h % 1000) / 1000.0);
                    }
                }
                await Task.CompletedTask;
                return vector;
            }
        };

        memory.CallMethod("initialize", new List<RuntimeValue>
        {
            RuntimeValue.Integer(8),
            RuntimeValue.String("single"),
            RuntimeValue.Function(embedFn)
        }, interpreter);

        var beforeRemember = embedCalls;
        memory.CallMethod("remember", new List<RuntimeValue>
        {
            RuntimeValue.String("alpha beta gamma unique embed path"),
            RuntimeValue.Null()
        }, interpreter);
        Assert.True(embedCalls > beforeRemember, "remember should invoke TranspiledDelegate embedder");

        var beforeQuery = embedCalls;
        var opts = new DictionaryInstance();
        opts.SetEntry("minScore", RuntimeValue.Float(0));
        var results = memory.CallMethod("query", new List<RuntimeValue>
        {
            RuntimeValue.String("alpha beta gamma unique embed path"),
            RuntimeValue.Integer(3),
            RuntimeValue.Object(opts)
        }, interpreter);

        Assert.True(embedCalls > beforeQuery, "query searchSimilar should invoke TranspiledDelegate embedder");
        Assert.Equal(MaldaLang.Interpreter.ValueType.Array, results.Type);
        Assert.True(results.AsArray().Count >= 1);
    }
}
