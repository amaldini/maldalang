using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class InterpreterResumeCharacterizationTests : TestBase
{
    private sealed class OnDemandInputProvider : IInputProvider
    {
        private readonly Queue<string> _inputs = new();

        public int GetInputCalls { get; private set; }
        public int GetQueuedInputCalls { get; private set; }

        public void AddInput(string value)
        {
            _inputs.Enqueue(value);
        }

        public bool HasQueuedInput()
        {
            return false;
        }

        public string GetQueuedInput()
        {
            GetQueuedInputCalls++;
            return _inputs.Count > 0 ? _inputs.Dequeue() : "";
        }

        public Task<string> GetInputAsync(string prompt)
        {
            GetInputCalls++;
            return Task.FromResult(_inputs.Count > 0 ? _inputs.Dequeue() : "");
        }

        public void QueueInput(string input)
        {
            _inputs.Enqueue(input);
        }
    }

    private static List<Parser.AST.Statements.Statement> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }

    [Fact]
    public async Task InterpretAsync_OnDemandInputProvider_CompletesWithoutInputRequiredException()
    {
        var inputProvider = new OnDemandInputProvider();
        inputProvider.AddInput("typed value");
        var interpreter = new Interpreter.Interpreter(null, null, inputProvider);
        var statements = Parse(
            """
            var response = input("Prompt: ");
            print(response);
            """);

        RedirectConsole();
        try
        {
            var exception = await Record.ExceptionAsync(() => interpreter.InterpretAsync(statements));

            Assert.Null(exception);
            Assert.Equal(1, inputProvider.GetInputCalls);
            Assert.Equal(0, inputProvider.GetQueuedInputCalls);
            Assert.Equal("typed value", GetOutput());
        }
        finally
        {
            RestoreConsole();
        }
    }

    [Fact]
    public async Task InterpretAsync_ReinvocationRestartsTopLevelExecution()
    {
        var interpreter = new Interpreter.Interpreter();
        var statements = Parse(
            """
            var counter = 0;
            counter = counter + 1;
            print(counter);
            """);

        RedirectConsole();
        try
        {
            await interpreter.InterpretAsync(statements);
            await interpreter.InterpretAsync(statements);

            var lines = GetOutput().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(new[] { "1", "1" }, lines);
        }
        finally
        {
            RestoreConsole();
        }
    }
}
