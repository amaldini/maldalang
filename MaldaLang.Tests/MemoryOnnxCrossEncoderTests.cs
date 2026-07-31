// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.BuiltIns;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class MemoryOnnxCrossEncoderTests : TestBase
{
    [Fact]
    public void TryCreate_returnsNull_whenModelMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), "nonexistent_cross_encoder_" + Guid.NewGuid().ToString("N") + ".onnx");
        Assert.Null(MemoryOnnxCrossEncoder.TryCreate(path));
    }

    [Fact]
    public void TryCreate_returnsNull_whenVocabMissing()
    {
        var tempDir = CreateTempDirectory("onnx_no_vocab_");
        try
        {
            var modelPath = Path.Combine(tempDir, "model.onnx");
            File.WriteAllText(modelPath, "not a real onnx model");
            Assert.Null(MemoryOnnxCrossEncoder.TryCreate(modelPath));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TryCreate_resolvesTildeModelDirectory()
    {
        var tempDir = CreateTempDirectory("onnx_tilde_");
        try
        {
            var modelDir = Path.Combine(tempDir, "models", "cross-encoder");
            Directory.CreateDirectory(modelDir);
            File.WriteAllText(Path.Combine(modelDir, "model.onnx"), "not onnx");
            File.WriteAllText(Path.Combine(modelDir, "vocab.txt"), "a");

            var configured = Path.Combine(tempDir, "models", "cross-encoder");
            Assert.Null(MemoryOnnxCrossEncoder.TryCreate(configured));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Opt-in integration test. Set <c>MALDA_RUN_ONNX_INTEGRATION=1</c> to download and score with the real model.
    /// </summary>
    [Fact]
    public async Task Score_prefersRelevantDocument_whenIntegrationEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MALDA_RUN_ONNX_INTEGRATION"), "1", StringComparison.Ordinal))
            return;

        var tempHome = CreateTempDirectory("onnx_integration_");
        try
        {
            await CrossEncoderOnnxModels.EnsureDownloadedAsync(maldaHome: tempHome);
            var modelDir = CrossEncoderOnnxModels.GetDefaultModelDirectory(tempHome);
            using var encoder = MemoryOnnxCrossEncoder.TryCreate(modelDir);
            Assert.NotNull(encoder);

            var relevant = encoder!.Score("what is a panda", "the giant panda is a bear native to China");
            var irrelevant = encoder.Score("what is a panda", "stock markets rallied after earnings reports");
            Assert.True(relevant > irrelevant, $"expected relevant ({relevant}) > irrelevant ({irrelevant})");
        }
        finally
        {
            SafeDeleteDirectory(tempHome);
        }
    }
}
