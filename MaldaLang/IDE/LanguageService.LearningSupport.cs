// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.PackageManager;
using System.Threading;

namespace MaldaLang.IDE.Services;

public partial class LanguageService
{
    private static void ApplyLearningSupport(Diagnostic diagnostic)
    {
        var message = diagnostic.Message.ToLowerInvariant();

        if (diagnostic.Source == "decorator" || message.Contains("decorator") || message.Contains("@get") || message.Contains("@post"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Decorators are part of MALDA's API and UI features. They work best after you are comfortable with plain functions.",
                suggestedFix: "Check the decorator format and compare it with a runnable REST or UI example.",
                relatedExamplePath: "Web/rest_api_server.malda",
                relatedExampleTitle: "REST API Server",
                relatedDocumentationPath: "/?lesson=build-ai-apps#lesson-build-ai-apps",
                relatedDocumentationTitle: "Build AI Apps");
            return;
        }

        if (message.Contains("prompt") || message.Contains("agent"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Prompts and agents build on the same function-style thinking used in the beginner lessons.",
                suggestedFix: "Open the First Prompt or First Agent starter and compare the structure with your code.",
                relatedExamplePath: "Prompts/basic_prompt.malda",
                relatedExampleTitle: "Basic Prompt",
                relatedDocumentationPath: "/?lesson=first-prompt#lesson-first-prompt",
                relatedDocumentationTitle: "First Prompt");
            return;
        }

        if (message.Contains("if") || message.Contains("else"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Conditionals need a complete condition and a block for each branch you want to run.",
                suggestedFix: "Recheck the condition, parentheses, and braces around the if/else blocks.",
                relatedExamplePath: "Basics/conditionals.malda",
                relatedExampleTitle: "Conditionals",
                relatedDocumentationPath: "/?lesson=conditionals#lesson-conditionals",
                relatedDocumentationTitle: "Conditionals");
            return;
        }

        if (message.Contains("while") || message.Contains("for") || message.Contains("loop"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Loops repeat code, so they usually need a valid condition and a block surrounded by braces.",
                suggestedFix: "Compare your loop header and braces with the While Loop or For Loop examples.",
                relatedExamplePath: "Basics/while_loop.malda",
                relatedExampleTitle: "While Loop",
                relatedDocumentationPath: "/?lesson=loops#lesson-loops",
                relatedDocumentationTitle: "Loops");
            return;
        }

        if (message.Contains("function") || message.Contains("return") || message.Contains("parameter"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Functions let you name reusable steps. In MALDA they need parentheses after the name and braces around the body.",
                suggestedFix: "Compare your declaration with the Functions example and verify each parameter is separated correctly.",
                relatedExamplePath: "Basics/functions.malda",
                relatedExampleTitle: "Functions",
                relatedDocumentationPath: "/?lesson=functions#lesson-functions",
                relatedDocumentationTitle: "Functions");
            return;
        }

        if (message.Contains("input"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Input is read as text first, then often converted to numbers before calculations.",
                suggestedFix: "Check the Input Example to confirm where conversion happens and how values are printed.",
                relatedExamplePath: "Basics/input_example.malda",
                relatedExampleTitle: "Input Example",
                relatedDocumentationPath: "/?lesson=input-output#lesson-input-output",
                relatedDocumentationTitle: "Input and Output");
            return;
        }

        if (message.Contains("';'") || message.Contains("semicolon") || message.Contains("expected ';'"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "MALDA statements usually end with a semicolon so the parser knows where one statement stops and the next begins.",
                suggestedFix: "Add the missing semicolon and rerun, or compare your line with Hello World or Variables and Arithmetic.",
                relatedExamplePath: "Basics/variables_arithmetic.malda",
                relatedExampleTitle: "Variables and Arithmetic",
                relatedDocumentationPath: "/?lesson=variables#lesson-variables",
                relatedDocumentationTitle: "Variables and Arithmetic");
            return;
        }

        if (message.Contains("}") || message.Contains("{") || message.Contains("brace") || message.Contains("block"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Blocks group code together. Every opening brace should eventually have a matching closing brace.",
                suggestedFix: "Look for the nearest missing or extra brace and compare the shape of your code with a basic function or loop example.",
                relatedExamplePath: "Basics/functions.malda",
                relatedExampleTitle: "Functions",
                relatedDocumentationPath: "/?lesson=functions#lesson-functions",
                relatedDocumentationTitle: "Functions");
            return;
        }

        SetLearningSupport(
            diagnostic,
            hint: "When a parser error feels abstract, the fastest way forward is to compare your code with a very small working example.",
            suggestedFix: diagnostic.AutoFix != null
                ? diagnostic.AutoFix.Description
                : "Start from Hello World or Variables and Arithmetic, then reapply your changes one step at a time.",
            relatedExamplePath: "Basics/hello_world.malda",
            relatedExampleTitle: "Hello World",
            relatedDocumentationPath: "/?lesson=hello-world#lesson-hello-world",
            relatedDocumentationTitle: "Hello World");
    }

    private static void SetLearningSupport(
        Diagnostic diagnostic,
        string hint,
        string suggestedFix,
        string relatedExamplePath,
        string relatedExampleTitle,
        string relatedDocumentationPath,
        string relatedDocumentationTitle)
    {
        diagnostic.LearningHint = hint;
        diagnostic.SuggestedFix = diagnostic.AutoFix != null
            ? $"{suggestedFix} Suggested autofix: {diagnostic.AutoFix.Description}."
            : suggestedFix;
        diagnostic.RelatedExamplePath = relatedExamplePath;
        diagnostic.RelatedExampleTitle = relatedExampleTitle;
        diagnostic.RelatedDocumentationPath = relatedDocumentationPath;
        diagnostic.RelatedDocumentationTitle = relatedDocumentationTitle;
    }
}
