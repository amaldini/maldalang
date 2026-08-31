// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Compiler;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class PromptAttachmentTests
{
    private static readonly byte[] OneByOnePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void LLMClient_BuildRequestBody_SerializesImageUrlAndFileParts()
    {
        var pngPath = Path.Combine(Path.GetTempPath(), "malda_attach_" + Guid.NewGuid().ToString("N") + ".png");
        var pdfPath = Path.Combine(Path.GetTempPath(), "malda_attach_" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(pngPath, OneByOnePng);
        File.WriteAllText(pdfPath, "%PDF-1.1\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

        try
        {
            var attachments = new List<PromptAttachment>
            {
                new(PromptAttachment.KindImage, pngPath, null, Path.GetFileName(pngPath)),
                new(PromptAttachment.KindImage, null, "https://example.com/remote.png", "remote.png"),
                new(PromptAttachment.KindPdf, pdfPath, null, Path.GetFileName(pdfPath))
            };
            var content = PromptAttachmentCodec.BuildContentParts("What is the total?", attachments);

            var msg = new JsonObject();
            msg.Set("role", RuntimeValue.String("user"));
            msg.Set("content", content);
            var messages = RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Object(msg) });

            var client = new LLMClientInstance
            {
                Model = "gpt-4o-mini",
                ApiUrl = "https://example.test/v1/chat/completions"
            };
            var body = client.BuildRequestBody(messages, tools: null, responseFormat: null);
            var json = JsonSerializer.Serialize(body["messages"]);

            Assert.Contains("image_url", json, StringComparison.Ordinal);
            Assert.Contains("data:image/png;base64,", json, StringComparison.Ordinal);
            Assert.Contains("https://example.com/remote.png", json, StringComparison.Ordinal);
            Assert.Contains("\"file\"", json, StringComparison.Ordinal);
            Assert.Contains("application/pdf", json, StringComparison.Ordinal);
            Assert.DoesNotContain("shots/", json, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(pngPath);
            TryDelete(pdfPath);
        }
    }

    [Fact]
    public void LlamaCpp_EnsureLocalBackendAllows_ThrowsOnImageParts()
    {
        var attachments = new List<PromptAttachment>
        {
            new(PromptAttachment.KindImage, null, "https://example.com/a.png", "a.png")
        };
        var content = PromptAttachmentCodec.BuildContentParts("hi", attachments);
        var msg = new JsonObject();
        msg.Set("role", RuntimeValue.String("user"));
        msg.Set("content", content);

        var ex = Assert.Throws<RuntimeException>(() =>
            PromptAttachmentCodec.EnsureLocalBackendAllows(new List<RuntimeValue> { RuntimeValue.Object(msg) }));
        Assert.Contains("HTTP vision-capable", ex.Message, StringComparison.Ordinal);
        Assert.Contains("GGUF", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LlamaCpp_EnsureLocalBackendAllows_AllowsStringContent()
    {
        var msg = new JsonObject();
        msg.Set("role", RuntimeValue.String("user"));
        msg.Set("content", RuntimeValue.String("hi"));
        PromptAttachmentCodec.EnsureLocalBackendAllows(new List<RuntimeValue> { RuntimeValue.Object(msg) });
    }

    [Fact]
    public void Codec_ReadBytes_MissingFile_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "malda_missing_" + Guid.NewGuid().ToString("N") + ".png");
        var ex = Assert.Throws<RuntimeException>(() => PromptAttachmentCodec.ReadBytes(missing));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Codec_ReadBytes_OverSize_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), "malda_big_" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            using (var stream = File.Create(path))
                stream.SetLength(PromptAttachmentCodec.MaxBytes + 1);

            var ex = Assert.Throws<RuntimeException>(() => PromptAttachmentCodec.ReadBytes(path));
            Assert.Contains("10 MB", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ParseList_MoreThanMaxCount_Throws()
    {
        var items = new List<RuntimeValue>();
        for (var i = 0; i < PromptAttachment.MaxCount + 1; i++)
        {
            var obj = new JsonObject();
            obj.Set("kind", RuntimeValue.String("image"));
            obj.Set("url", RuntimeValue.String("https://example.com/" + i + ".png"));
            items.Add(RuntimeValue.Object(obj));
        }

        var ex = Assert.Throws<RuntimeException>(() =>
            PromptAttachment.ParseListOrNull(RuntimeValue.Array(items)));
        Assert.Contains("at most", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TraceSummary_DoesNotIncludeDataUrl()
    {
        var attachments = new List<PromptAttachment>
        {
            new(PromptAttachment.KindImage, "photo.png", null, "photo.png")
        };
        var summary = JsonSerializer.Serialize(PromptAttachmentCodec.TraceSummary(attachments));
        Assert.DoesNotContain("base64", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("photo.png", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Conversation_AddUserMessage_WithAttachments_UsesContentArray()
    {
        var conversation = new ConversationInstance();
        conversation.AddUserMessage(
            "see",
            new List<PromptAttachment> { new(PromptAttachment.KindImage, null, "https://example.com/a.png", "a.png") });

        var messages = conversation.GetMessages().AsArray();
        Assert.NotEmpty(messages);
        var last = messages[^1].AsObject();
        var content = last.Get("content");
        Assert.Equal(ValueType.Array, content.Type);
        Assert.True(PromptAttachmentCodec.HasNonTextContentParts(content));
    }

    [Fact]
    public void Transpiler_EmitsAttachmentParseAndPromptInstanceArg()
    {
        var source = """
            prompt look(photo) {
                user: "see"
                attachments: [{ kind: "image", path: photo }]
            }

            var p = look("a.png");
            print(p.attachments[0].path);
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_prompt_attach_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "attach.malda");
        var generatedPath = Path.Combine(tempDir, "GeneratedProgram.cs");
        File.WriteAllText(sourcePath, source);

        try
        {
            var compiler = new Compiler.Compiler();
            var csharpResult = compiler.CompileToCSharp(sourcePath, generatedPath);
            Assert.True(csharpResult.Success, csharpResult.ErrorMessage ?? "Transpile failed.");
            var generated = File.ReadAllText(generatedPath);
            Assert.Contains("PromptAttachment.ParseListOrNull", generated);
            Assert.Contains("ApplyParameterInterpolation(attachments", generated);
            Assert.Contains("__promptInstance.Attachments", generated);
            Assert.Contains(", attachments));", generated);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
