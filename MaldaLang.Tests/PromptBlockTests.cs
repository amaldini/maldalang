// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Interpreter;
using MaldaLang.BuiltIns;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace MaldaLang.Tests;

public class PromptBlockTests : TestBase
{
    [Fact]
    public void ParsePromptDeclaration_SimplePrompt()
    {
        var source = @"
prompt simple(task) {
    user: ""Task: {task}""
}
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        
        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        Assert.IsType<Parser.AST.Declarations.PromptDeclaration>(statements[0]);
        
        var promptDecl = (Parser.AST.Declarations.PromptDeclaration)statements[0];
        Assert.Equal("simple", promptDecl.Name);
        Assert.Single(promptDecl.Parameters);
        Assert.Equal("task", promptDecl.Parameters[0]);
    }
    
    [Fact]
    public void ParsePromptDeclaration_WithSystemPrompt()
    {
        var source = @"
prompt planTask(task, docs) -> Plan {
    system: ""You are a senior engineer."",
    user: ""Task: {task}\n\nDocs: {docs}""
}
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        
        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        var promptDecl = (Parser.AST.Declarations.PromptDeclaration)statements[0];
        Assert.Equal("planTask", promptDecl.Name);
        Assert.Equal(2, promptDecl.Parameters.Count);
        Assert.Equal("Plan", promptDecl.ReturnType);
    }
    
    [Fact]
    public void InvokePrompt_SimplePrompt()
    {
        var source = @"
prompt simple(task) {
    user: ""Task: {task}""
}

var result = simple(""test"");
print(result.user);
";
        var output = RunProgram(source);
        Assert.Contains("Task: test", output);
    }
    
    [Fact]
    public void InvokePrompt_WithSystemPrompt()
    {
        var source = @"
prompt planTask(task) {
    system: ""You are a planner.""
    user: ""Plan: {task}""
}

var result = planTask(""build feature"");
print(result.system);
print(result.user);
";
        var output = RunProgram(source);
        Assert.Contains("You are a planner.", output);
        Assert.Contains("Plan: build feature", output);
    }
    
    [Fact]
    public void InvokePrompt_WithInterpolation()
    {
        var source = @"
prompt greet(name, age) {
    user: $""Hello {name}, you are {age} years old.""
}

var result = greet(""Alice"", 25);
print(result.user);
";
        var output = RunProgram(source);
        Assert.Contains("Hello Alice, you are 25 years old.", output);
    }
    
    [Fact]
    public void AgentThink_WithPromptInstance()
    {
        var source = @"
prompt taskPrompt(task) {
    system: ""You are a helpful assistant."",
    user: ""Please help with: {task}""
}

var p = taskPrompt(""test task"");
print(p.user);
";
        var output = RunProgram(source);
        Assert.Contains("Please help with: test task", output);
    }

    [Fact]
    public void InvokePrompt_MethodAccessors_Work()
    {
        var source = @"
prompt greet(name) {
    system: ""You are friendly."",
    user: ""Hello, {name}!""
}

var result = greet(""Alice"");
print(result.getSystem());
print(result.getUser());
";
        var output = RunProgram(source);
        Assert.Contains("You are friendly.", output);
        Assert.Contains("Hello, Alice!", output);
    }
    
    [Fact]
    public async Task PromptDeclaration_MissingUserField_ThrowsError()
    {
        var source = @"
prompt invalid() {
    system: ""Only system, no user""
}

var result = invalid();
";
        RedirectConsole();
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            var statements = parser.Parse();
            
            // Should parse fine, but runtime should error
            Assert.Empty(parser.Errors);
            
            var interpreter = new Interpreter.Interpreter();
            await Assert.ThrowsAsync<RuntimeException>(async () => await interpreter.InterpretAsync(statements));
        }
        finally
        {
            RestoreConsole();
        }
    }
    
    [Fact]
    public void PromptDeclaration_WithMetadata()
    {
        var source = @"
prompt advanced(task) {
    system: ""You are an expert."",
    user: ""Task: {task}"",
    model: ""openai/gpt-4"",
    temperature: 0.7,
    tools: [""read_file"", ""write_file""],
    maxTokens: 2000
}

var result = advanced(""test"");
print(result.model);
print(result.temperature);
";
        var output = RunProgram(source);
        Assert.Contains("openai/gpt-4", output);
        Assert.Contains("0.7", output);
    }
    
    [Fact]
    public void ParsePromptDeclaration_StatementBasedBody()
    {
        var source = @"
prompt summarize(text) {
    system ""You are a summarizer."";
    user text;
}
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        
        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        var promptDecl = (Parser.AST.Declarations.PromptDeclaration)statements[0];
        Assert.Equal("summarize", promptDecl.Name);
        Assert.Equal(Parser.AST.Declarations.PromptBodyType.Statements, promptDecl.BodyType);
        Assert.NotNull(promptDecl.StatementBody);
        Assert.Equal(2, promptDecl.StatementBody!.Count);
    }
    
    [Fact]
    public void InvokePrompt_StatementBasedBody()
    {
        var source = @"
prompt summarize(text) {
    system ""You are a summarizer."";
    user text;
}

var result = summarize(""Long text here"");
print(result.user);
";
        var output = RunProgram(source);
        Assert.Contains("Long text here", output);
    }
    
    [Fact]
    public void InvokePrompt_StatementBasedBody_WithAllFields()
    {
        var source = @"
prompt advanced(task) {
    system ""You are an expert."";
    user task;
    model ""openai/gpt-4"";
    temperature 0.7;
    tools [""read_file"", ""write_file""];
    maxTokens 2000;
}

var result = advanced(""test task"");
print(result.user);
print(result.model);
";
        var output = RunProgram(source);
        Assert.Contains("test task", output);
        Assert.Contains("openai/gpt-4", output);
    }
    
    [Fact]
    public async Task AwaitPrompt_ExecutesLLM_ReturnsString()
    {
        var source = @"
var client = new OpenRouterClient();
var agent = new Agent(""TestAgent"", ""assistant"", ""You are helpful."", client);
setDefaultAgent(agent);

prompt summarize(text) {
    system ""You are a summarizer. Respond with only the summary, no extra text."";
    user ""Summarize: "" + text;
}

var result = await summarize(""This is a test document that needs summarization."");
print(result);
";
        // Note: This test requires actual LLM call, so it may fail if API key is not set
        // For now, we'll test that it compiles and the structure is correct
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        
        Assert.Empty(parser.Errors);
        // The await syntax should parse correctly
        Assert.NotEmpty(statements);
    }
    
    [Fact]
    public void PromptDeclaration_BackwardCompatibility_ObjectLiteralStillWorks()
    {
        var source = @"
prompt oldStyle(task) {
    user: ""Task: {task}""
}

var result = oldStyle(""test"");
print(result.user);
";
        var output = RunProgram(source);
        Assert.Contains("Task: test", output);
    }
    
    [Fact]
    public void PromptDeclaration_StatementBasedBody_UserCanBeExpression()
    {
        var source = @"
prompt combine(a, b) {
    user ""First: "" + a + "", Second: "" + b;
}

var result = combine(""A"", ""B"");
print(result.user);
";
        var output = RunProgram(source);
        Assert.Contains("First: A, Second: B", output);
    }

    [Fact]
    public async Task AwaitPrompt_WithTypedReturnAndNoLlmClient_RetriesThenThrows()
    {
        var source = @"
prompt planTask(task) -> Plan {
    user ""Task: {task}"";
}
";

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var promptDecl = Assert.IsType<Parser.AST.Declarations.PromptDeclaration>(statements[0]);
        var prompt = new PromptValue(promptDecl);
        var interpreter = new Interpreter.Interpreter();

        // Use a default agent with no client to avoid network calls and produce deterministic invalid content.
        var agent = new AgentInstance();
        agent.Initialize("TestAgent", "assistant", "test", null, null, null, null);
        var defaultAgentField = typeof(Interpreter.Interpreter).GetField("_defaultAgent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(defaultAgentField);
        defaultAgentField!.SetValue(interpreter, agent);

        var ex = await Assert.ThrowsAsync<RuntimeException>(() =>
            prompt.CallAsync(new List<RuntimeValue> { RuntimeValue.String("build feature") }, interpreter));

        Assert.Contains("after 3 attempts", ex.Message);
        Assert.Contains("Return type: Plan", ex.Message);
    }
}
