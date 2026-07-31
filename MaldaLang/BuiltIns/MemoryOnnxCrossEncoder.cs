// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

/// <summary>
/// Optional ONNX cross-encoder for GraphMemory rerank (<c>rerankMode: onnx</c>).
/// Expects a BERT-style cross-encoder ONNX model and <c>vocab.txt</c> in the same directory.
/// </summary>
public sealed class MemoryOnnxCrossEncoder : IDisposable
{
    private const int DefaultMaxSequenceLength = 512;
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly int _maxSequenceLength;
    private readonly string _inputIdsName;
    private readonly string _attentionMaskName;
    private readonly string? _tokenTypeIdsName;

    private MemoryOnnxCrossEncoder(
        InferenceSession session,
        BertTokenizer tokenizer,
        int maxSequenceLength,
        string inputIdsName,
        string attentionMaskName,
        string? tokenTypeIdsName)
    {
        _session = session;
        _tokenizer = tokenizer;
        _maxSequenceLength = maxSequenceLength;
        _inputIdsName = inputIdsName;
        _attentionMaskName = attentionMaskName;
        _tokenTypeIdsName = tokenTypeIdsName;
    }

    public static MemoryOnnxCrossEncoder? TryCreate(string modelPath, int maxSequenceLength = DefaultMaxSequenceLength)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            return null;

        try
        {
            var resolvedModel = ResolveModelPath(modelPath);
            if (resolvedModel == null || !File.Exists(resolvedModel))
                return null;

            var vocabPath = ResolveVocabPath(resolvedModel);
            if (vocabPath == null || !File.Exists(vocabPath))
                return null;

            var options = new BertOptions
            {
                LowerCaseBeforeTokenization = true,
                UnknownToken = "[UNK]",
                ClassificationToken = "[CLS]",
                SeparatorToken = "[SEP]",
                PaddingToken = ""
            };
            using var vocabStream = File.OpenRead(vocabPath);
            var tokenizer = BertTokenizer.Create(vocabStream, options);

            var session = new InferenceSession(resolvedModel);
            var inputNames = session.InputMetadata.Keys.ToList();
            var inputIdsName = FindInputName(inputNames, "input_ids") ?? inputNames[0];
            var attentionMaskName = FindInputName(inputNames, "attention_mask") ?? inputNames.FirstOrDefault(n => !string.Equals(n, inputIdsName, StringComparison.OrdinalIgnoreCase)) ?? inputIdsName;
            var tokenTypeIdsName = FindInputName(inputNames, "token_type_ids");

            return new MemoryOnnxCrossEncoder(
                session,
                tokenizer,
                Math.Max(32, maxSequenceLength),
                inputIdsName,
                attentionMaskName,
                tokenTypeIdsName);
        }
        catch
        {
            return null;
        }
    }

    public double Score(string query, string document)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(document))
            return 0.0;

        var queryIds = _tokenizer.EncodeToIds(query);
        var documentIds = _tokenizer.EncodeToIds(document);
        var (inputIdList, tokenTypeIdList) = BuildPairInputs(queryIds, documentIds);
        var inputIds = PadAndTruncate(inputIdList, _maxSequenceLength);
        var attentionMask = inputIds.Select(id => id == 0 ? 0L : 1L).ToArray();
        var tokenTypeIds = PadAndTruncate(tokenTypeIdList, _maxSequenceLength);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputIdsName, ToTensor(inputIds)),
            NamedOnnxValue.CreateFromTensor(_attentionMaskName, ToTensor(attentionMask))
        };
        if (_tokenTypeIdsName != null)
            inputs.Add(NamedOnnxValue.CreateFromTensor(_tokenTypeIdsName, ToTensor(tokenTypeIds)));

        using var results = _session.Run(inputs);
        var output = results.First().AsEnumerable<float>().FirstOrDefault();
        return Sigmoid(output);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    static string? ResolveModelPath(string modelPath)
    {
        var trimmed = CrossEncoderOnnxModels.ExpandMaldaPath(modelPath);
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;
        if (File.Exists(trimmed) && trimmed.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        if (Directory.Exists(trimmed))
        {
            var candidate = Path.Combine(trimmed, "model.onnx");
            if (File.Exists(candidate))
                return candidate;
        }
        return File.Exists(trimmed) ? trimmed : null;
    }

    static string? ResolveVocabPath(string modelPath)
    {
        var dir = Path.GetDirectoryName(modelPath);
        if (string.IsNullOrEmpty(dir))
            return null;
        var vocab = Path.Combine(dir, "vocab.txt");
        return File.Exists(vocab) ? vocab : null;
    }

    static string? FindInputName(IEnumerable<string> names, string expected) =>
        names.FirstOrDefault(n => string.Equals(n, expected, StringComparison.OrdinalIgnoreCase));

    static DenseTensor<long> ToTensor(long[] values) =>
        new(values, new[] { 1, values.Length });

    static double Sigmoid(float value) =>
        1.0 / (1.0 + Math.Exp(-value));

    (List<int> InputIds, List<int> TokenTypeIds) BuildPairInputs(
        IReadOnlyList<int> sequenceA,
        IReadOnlyList<int> sequenceB)
    {
        var ids = new List<int>();
        var typeIds = new List<int>();
        ids.Add(_tokenizer.ClassificationTokenId);
        typeIds.Add(0);
        foreach (var id in sequenceA)
        {
            ids.Add(id);
            typeIds.Add(0);
        }
        ids.Add(_tokenizer.SeparatorTokenId);
        typeIds.Add(0);
        foreach (var id in sequenceB)
        {
            ids.Add(id);
            typeIds.Add(1);
        }
        ids.Add(_tokenizer.SeparatorTokenId);
        typeIds.Add(1);
        return (ids, typeIds);
    }

    static long[] PadAndTruncate(List<int> ids, int maxLength)
    {
        var length = Math.Min(ids.Count, maxLength);
        var result = new long[maxLength];
        for (var i = 0; i < length; i++)
            result[i] = ids[i];
        return result;
    }
}
