// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Services;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MaldaLang.BuiltIns;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST;

public class AIChatService
{
    private readonly MALDALanguageContextService _languageContextService;
    private AIChatSettings _settings;
    private LLMClientInstance? _llmClient;
    private readonly List<ChatMessage> _conversationHistory = new();
    
    public AIChatService(MALDALanguageContextService languageContextService)
    {
        _languageContextService = languageContextService;
        _settings = new AIChatSettings { UseOpenRouterClient = true };
    }
    
    public void UpdateSettings(AIChatSettings settings)
    {
        _settings = settings;
        _llmClient = null; // Reset client to use new settings
    }
    
    private LLMClientInstance GetLLMClient()
    {
        if (_llmClient != null)
            return _llmClient;
        
        if (_settings.UseOpenRouterClient)
        {
            // Use OpenRouterClient with optional model override
            _llmClient = new OpenRouterClientInstance(_settings.Model);
        }
        else
        {
            // Use custom LLMClient
            var apiUrl = _settings.ApiUrl ?? "https://openrouter.ai/api/v1/chat/completions";
            var apiKey = _settings.ApiKey ?? System.Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "";
            var model = _settings.Model ?? "deepseek/deepseek-v4-flash";
            
            _llmClient = new LLMClientInstance
            {
                ApiUrl = apiUrl,
                ApiKey = apiKey,
                Model = model
            };
        }
        
        return _llmClient;
    }
    
    public async Task<ChatResponse> SendMessageAsync(
        string userMessage,
        string currentCode,
        int cursorLine,
        int cursorColumn,
        List<Diagnostic> errors,
        string? selectedCode = null,
        ChatMode mode = ChatMode.Ask)
    {
        if (mode == ChatMode.AskMalda)
        {
            return await SendMessageViaAgentAsync(userMessage, currentCode, cursorLine, cursorColumn, errors, selectedCode);
        }

        try
        {
            // Build context-aware prompt (includes code, cursor position, errors, etc.)
            var prompt = BuildPrompt(userMessage, currentCode, cursorLine, cursorColumn, errors, selectedCode, mode);
            
            // Add the full prompt (with context) to history, not just the raw user message
            _conversationHistory.Add(new ChatMessage
            {
                Role = "user",
                Content = prompt
            });
            
            // Get LLM client
            var client = GetLLMClient();
            
            // Create messages for LLM
            var messages = new List<RuntimeValue>();
            
            // Add system message with compact language-pack boot context (syntax + gotchas)
            var systemPrompt = "You are an AI assistant helping developers write code in MALDA (Multi Agent Language with Development Automation).\n\n" +
                            _languageContextService.GetInlineBootContext() +
                            "\n\nCRITICAL RULES FOR CODE GENERATION:\n" +
                            "1. ALWAYS generate code in MALDA syntax ONLY. NEVER generate JavaScript, Python, C#, or any other language.\n" +
                            "2. For Agent operations, use agent.think(prompt) method, NOT agent.executeTask(). The correct syntax is:\n" +
                            "   var result = agent.think(\"your prompt here\");\n" +
                            "   print(result.content);\n" +
                            "3. Agent methods available: think(prompt), addTool(tool), getConversation(), reset()\n" +
                            "4. Example correct Agent usage:\n" +
                            "   var client = new OpenRouterClient(\"model-name\");\n" +
                            "   var agent = new Agent(\"Name\", \"role\", \"instructions\", client);\n" +
                            "   var response = agent.think(\"task description\");\n" +
                            "\n" +
                            "IMPORTANT: Only include code blocks (```malda ... ```) when you are suggesting actual code changes or modifications. " +
                            "When explaining what code does, answering questions, or showing examples, do NOT include code blocks - just explain in plain text or markdown. " +
                            "Code blocks should only be used when the user explicitly asks for code changes, improvements, or fixes.";
            messages.Add(CreateMessage("system", systemPrompt));
            
            // Add conversation history (which now includes the full context-aware prompt)
            foreach (var msg in _conversationHistory)
            {
                messages.Add(CreateMessage(msg.Role, msg.Content));
            }
            
            // Send to LLM
            var response = client.Chat(RuntimeValue.Array(messages), null);
            
            if (response.Type != ValueType.Object)
            {
                return new ChatResponse
                {
                    IsError = true,
                    ErrorMessage = "Invalid response from LLM"
                };
            }
            
            var responseObj = response.AsObject();
            if (responseObj is JsonObject jsonObj)
            {
                var content = jsonObj.Get("content", null);
                var contentStr = content?.AsString() ?? "";
                
                // Extract code block if present
                var codeBlock = ExtractCodeBlock(contentStr);
                
                // In Ask mode (non-agent), don't surface code blocks as suggestions; Edit mode uses the agent path and returns code from tools
                codeBlock = null;
                
                // Add assistant response to history
                _conversationHistory.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = contentStr
                });
                
                return new ChatResponse
                {
                    Content = contentStr,
                    CodeBlock = codeBlock
                };
            }
            
            return new ChatResponse
            {
                IsError = true,
                ErrorMessage = "Failed to parse LLM response"
            };
        }
        catch (Exception ex)
        {
            return new ChatResponse
            {
                IsError = true,
                ErrorMessage = $"Error: {ex.Message}"
            };
        }
    }
    
    private string BuildPrompt(
        string userMessage,
        string currentCode,
        int cursorLine,
        int cursorColumn,
        List<Diagnostic> errors,
        string? selectedCode,
        ChatMode mode)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("Current Code:");
        sb.AppendLine("```malda");
        sb.AppendLine(currentCode);
        sb.AppendLine("```");
        sb.AppendLine();
        
        sb.AppendLine($"Cursor Position: Line {cursorLine + 1}, Column {cursorColumn + 1}");
        sb.AppendLine();
        
        if (errors != null && errors.Count > 0)
        {
            sb.AppendLine("Current Errors:");
            foreach (var error in errors)
            {
                sb.AppendLine($"- Line {error.Line + 1}: {error.Message}");
            }
            sb.AppendLine();
        }
        
        if (!string.IsNullOrEmpty(selectedCode))
        {
            sb.AppendLine("Selected Code:");
            sb.AppendLine("```malda");
            sb.AppendLine(selectedCode);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        
        sb.AppendLine($"User Question: {userMessage}");
        sb.AppendLine();

        sb.AppendLine("User intent: ASK MODE – the user is asking a question or wants an explanation. "
                    + "Focus on explanations and guidance; avoid proposing full-file code rewrites unless explicitly requested.");
        sb.AppendLine();
        sb.AppendLine("Please provide helpful answers about MALDA syntax, explain code, or suggest improvements.");
        sb.AppendLine("IMPORTANT: Only include code blocks (```malda ... ```) when suggesting actual code changes or modifications. " +
                     "When explaining what code does or answering questions, use plain text or markdown without code blocks.");
        
        return sb.ToString();
    }
    
    private bool IsCodeSuggestion(string extractedCode, string currentCode, string userMessage)
    {
        // Normalize both code strings for comparison
        var normalizedExtracted = NormalizeCode(extractedCode);
        var normalizedCurrent = NormalizeCode(currentCode);
        
        // Check if user is explicitly asking for code changes FIRST (before similarity checks)
        var lowerMessage = userMessage.ToLowerInvariant();
        var changeKeywords = new[] { "change", "modify", "update", "fix", "improve", "refactor", "rewrite", "suggest", "show me", "give me", "create", "write", "add", "remove", "delete", "insert", "implement", "new function", "new method" };
        var explanationKeywords = new[] { "what does", "what is", "explain", "how does", "describe", "tell me about", "meaning", "purpose", "what will" };
        
        // If user is explicitly asking for changes, ALWAYS treat code block as suggestion
        // This takes priority over similarity checks
        bool isChangeRequest = changeKeywords.Any(keyword => lowerMessage.Contains(keyword));
        if (isChangeRequest)
        {
            // Even if code is identical, if user asked for changes, show it (might be a formatting change or the AI misunderstood)
            // But if code is truly identical, don't show (no point)
            if (normalizedExtracted == normalizedCurrent)
            {
                return false;
            }
            return true;
        }
        
        // If the extracted code is identical to current code, it's likely just an explanation
        if (normalizedExtracted == normalizedCurrent)
        {
            return false;
        }
        
        // If user is asking for explanation, be more strict
        bool isExplanationRequest = explanationKeywords.Any(keyword => lowerMessage.Contains(keyword));
        if (isExplanationRequest)
        {
            // For explanation requests, only treat as suggestion if code is significantly different
            // Calculate similarity - if more than 80% similar, it's likely just showing the code being explained
            double similarity = CalculateSimilarity(normalizedExtracted, normalizedCurrent);
            if (similarity > 0.8)
            {
                return false;
            }
        }
        
        // Default: if code is significantly different, treat as suggestion
        double defaultSimilarity = CalculateSimilarity(normalizedExtracted, normalizedCurrent);
        return defaultSimilarity < 0.75; // Less than 75% similar = likely a suggestion (slightly more lenient)
    }
    
    private string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "";
        
        // Remove whitespace differences for comparison
        return string.Join("\n", 
            code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line)));
    }
    
    private double CalculateSimilarity(string str1, string str2)
    {
        if (string.IsNullOrEmpty(str1) && string.IsNullOrEmpty(str2))
            return 1.0;
        
        if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
            return 0.0;
        
        if (str1 == str2)
            return 1.0;
        
        // Simple similarity based on common lines
        var lines1 = new HashSet<string>(str1.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var lines2 = new HashSet<string>(str2.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        
        if (lines1.Count == 0 && lines2.Count == 0)
            return 1.0;
        
        var intersection = lines1.Intersect(lines2).Count();
        var union = lines1.Union(lines2).Count();
        
        return union > 0 ? (double)intersection / union : 0.0;
    }
    
    private string? ExtractCodeBlock(string content)
    {
        // Look for code blocks marked with ```malda or ```
        var pattern = @"```(?:malda)?\s*\n(.*?)```";
        var matches = Regex.Matches(content, pattern, RegexOptions.Singleline);
        
        if (matches.Count > 0)
        {
            // Return the first code block found
            var code = matches[0].Groups[1].Value.Trim();
            return code;
        }
        
        return null;
    }
    
    private RuntimeValue CreateMessage(string role, string content)
    {
        var msg = new JsonObject();
        msg.Set("role", RuntimeValue.String(role));
        msg.Set("content", RuntimeValue.String(content));
        return RuntimeValue.Object(msg);
    }
    
    public void ClearHistory()
    {
        _conversationHistory.Clear();
    }

    private async Task<ChatResponse> SendMessageViaAgentAsync(
        string userMessage,
        string currentCode,
        int cursorLine,
        int cursorColumn,
        List<Diagnostic> errors,
        string? selectedCode)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "AskMalda_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var currentFilePath = Path.Combine(tempDir, "current.malda");
            await File.WriteAllTextAsync(currentFilePath, currentCode ?? "");

            // Materialize docs/llm (+ live DECORATORS.md) so the agent can read/grep without bloating the system prompt
            _languageContextService.MaterializeLanguagePack(tempDir);

            var client = GetLLMClient();
            var inputProvider = new AskMaldaInputProvider();
            var instructions =
                "You are a MALDA code assistant. Working directory contains current.malda (the user's file) and llm/ (the MALDA language pack). "
                + "Start with llm/INDEX.md for load order. Prefer llm/malda-syntax.md and llm/malda-gotchas.md first; "
                + "then matching llm/few-shot/ samples; llm/malda-grammar.md for unfamiliar constructs; "
                + "llm/malda-builtins-min.md / grep llm/malda-builtins.tsv for builtins; llm/DECORATORS.md for @decorators. "
                + "Prefer grep + partial read_file over reading large files whole. "
                + "Use getSymbols, getParseErrors, read_file, replace_in_file, edit_file, grep as needed. Prefer suggesting edits on current.malda. "
                + "ALWAYS generate code in MALDA only; never JavaScript, Python, C#, or other languages.";
            var agent = new DevAgentInstance("AskMalda", "MALDA code assistant", instructions, client, tempDir, includeSymbols: true, inputProvider);

            var prompt = BuildAskMaldaPrompt(userMessage, currentCode, cursorLine, cursorColumn, errors, selectedCode);

            _conversationHistory.Add(new ChatMessage { Role = "user", Content = prompt });

            RuntimeValue response;
            try
            {
                response = await Task.Run(() => agent.Think(RuntimeValue.String(prompt)));
            }
            catch (Exception ex)
            {
                return new ChatResponse { IsError = true, ErrorMessage = $"Error from agent: {ex.Message}" };
            }

            if (response.Type != ValueType.Object)
            {
                return new ChatResponse { IsError = true, ErrorMessage = "Invalid response from agent" };
            }

            var responseObj = response.AsObject();
            if (responseObj is not JsonObject jsonObj)
            {
                return new ChatResponse { IsError = true, ErrorMessage = "Failed to parse agent response" };
            }

            var contentVal = jsonObj.Get("content", null);
            var contentStr = contentVal?.AsString() ?? "";

            _conversationHistory.Add(new ChatMessage { Role = "assistant", Content = contentStr });

            string? codeBlock = null;
            if (File.Exists(currentFilePath))
            {
                var newContent = await File.ReadAllTextAsync(currentFilePath);
                if (newContent != currentCode)
                {
                    codeBlock = newContent;
                }
            }

            return new ChatResponse { Content = contentStr, CodeBlock = codeBlock };
        }
        catch (Exception ex)
        {
            return new ChatResponse { IsError = true, ErrorMessage = $"Error: {ex.Message}" };
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { /* ignore cleanup errors */ }
        }
    }

    private static string BuildAskMaldaPrompt(
        string userMessage,
        string currentCode,
        int cursorLine,
        int cursorColumn,
        List<Diagnostic> errors,
        string? selectedCode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Current file: current.malda");
        sb.AppendLine();
        sb.AppendLine("Current Code:");
        sb.AppendLine("```malda");
        sb.AppendLine(currentCode);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine($"Cursor Position: Line {cursorLine + 1}, Column {cursorColumn + 1}");
        sb.AppendLine();
        if (errors != null && errors.Count > 0)
        {
            sb.AppendLine("Current Errors:");
            foreach (var error in errors)
            {
                sb.AppendLine($"- Line {error.Line + 1}: {error.Message}");
            }
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(selectedCode))
        {
            sb.AppendLine("Selected Code:");
            sb.AppendLine("```malda");
            sb.AppendLine(selectedCode);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        sb.AppendLine($"User Question: {userMessage}");
        return sb.ToString();
    }
    
    private class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }
}