// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.BuiltIns;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class CrossEncoderOnnxModelsTests : TestBase
{
    [Fact]
    public void IsInstalled_false_whenDirectoryEmpty()
    {
        var tempDir = CreateTempDirectory("cross_encoder_empty_");
        try
        {
            var modelDir = Path.Combine(tempDir, "models", "cross-encoder");
            Directory.CreateDirectory(modelDir);
            Assert.False(CrossEncoderOnnxModels.IsInstalled(modelDir));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void IsInstalled_true_whenModelAndVocabPresent()
    {
        var tempDir = CreateTempDirectory("cross_encoder_ready_");
        try
        {
            var modelDir = CrossEncoderOnnxModels.GetDefaultModelDirectory(tempDir);
            File.WriteAllText(Path.Combine(modelDir, CrossEncoderOnnxModels.LocalOnnxFileName), "fake");
            File.WriteAllText(Path.Combine(modelDir, CrossEncoderOnnxModels.LocalVocabFileName), "fake");
            Assert.True(CrossEncoderOnnxModels.IsInstalled(modelDir));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ExpandMaldaPath_expandsTildeMaldaModels()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expanded = CrossEncoderOnnxModels.ExpandMaldaPath("~/.malda/models/cross-encoder");
        Assert.Equal(
            Path.GetFullPath(Path.Combine(home, ".malda", "models", "cross-encoder")),
            expanded);
    }

    [Fact]
    public void ResolveRerankModelPath_usesInstalledDefaultWhenConfigEmpty()
    {
        var tempDir = CreateTempDirectory("cross_encoder_resolve_");
        try
        {
            var modelDir = CrossEncoderOnnxModels.GetDefaultModelDirectory(tempDir);
            File.WriteAllText(Path.Combine(modelDir, CrossEncoderOnnxModels.LocalOnnxFileName), "fake");
            File.WriteAllText(Path.Combine(modelDir, CrossEncoderOnnxModels.LocalVocabFileName), "fake");

            var resolved = CrossEncoderOnnxModels.ResolveRerankModelPath(null, tempDir);
            Assert.Equal(modelDir, resolved);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ResolveRerankModelPath_prefersConfiguredPath()
    {
        var tempDir = CreateTempDirectory("cross_encoder_cfg_");
        try
        {
            var customDir = Path.Combine(tempDir, "custom");
            Directory.CreateDirectory(customDir);
            var resolved = CrossEncoderOnnxModels.ResolveRerankModelPath(customDir, tempDir);
            Assert.Equal(Path.GetFullPath(customDir), resolved);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task EnsureDownloadedAsync_skipsWhenFilesAlreadyPresent()
    {
        var tempDir = CreateTempDirectory("cross_encoder_skip_");
        try
        {
            var modelDir = CrossEncoderOnnxModels.GetDefaultModelDirectory(tempDir);
            var onnxPath = Path.Combine(modelDir, CrossEncoderOnnxModels.LocalOnnxFileName);
            var vocabPath = Path.Combine(modelDir, CrossEncoderOnnxModels.LocalVocabFileName);
            File.WriteAllText(onnxPath, "cached");
            File.WriteAllText(vocabPath, "cached");

            await CrossEncoderOnnxModels.EnsureDownloadedAsync(maldaHome: tempDir);

            Assert.Equal("cached", File.ReadAllText(onnxPath));
            Assert.Equal("cached", File.ReadAllText(vocabPath));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
