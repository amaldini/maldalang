// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.DebugAdapter;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class InterpretDebugAdapterTests : TestBase
{
    [Fact]
    public async Task Launch_Breakpoint_StackTrace_Continue_Exits()
    {
        var dir = CreateTempDirectory("dap_");
        var program = Path.Combine(dir, "sample.malda");
        await File.WriteAllTextAsync(program, "var x = 1\nprint(x)\n");
        program = Path.GetFullPath(program);

        await using var harness = await DapHarness.StartAsync();
        try
        {
            var init = await harness.RequestAsync("initialize", new { adapterID = "malda" });
            Assert.True(init.Success);
            Assert.True(DapProtocol.ReadBoolean(init.Body, "supportsConfigurationDoneRequest"));
            Assert.True(DapProtocol.ReadBoolean(init.Body, "supportsConditionalBreakpoints"));
            Assert.True(DapProtocol.ReadBoolean(init.Body, "supportsEvaluateForHovers"));
            Assert.False(DapProtocol.ReadBoolean(init.Body, "supportsSetVariable"));

            var initialized = await harness.WaitForEventAsync("initialized");
            Assert.Equal("initialized", initialized.Event);

            var launch = await harness.RequestAsync("launch", new { program, stopOnEntry = false });
            Assert.True(launch.Success, launch.Message);

            var setBp = await harness.RequestAsync("setBreakpoints", new
            {
                source = new { path = program },
                breakpoints = new[] { new { line = 2 } }
            });
            Assert.True(setBp.Success, setBp.Message);
            var bp0 = setBp.Body.GetProperty("breakpoints")[0];
            Assert.True(bp0.GetProperty("verified").GetBoolean());
            Assert.Equal(2, bp0.GetProperty("line").GetInt32());

            var configDone = await harness.RequestAsync("configurationDone", null);
            Assert.True(configDone.Success, configDone.Message);

            var stopped = await harness.WaitForEventAsync("stopped");
            Assert.Equal("breakpoint", DapProtocol.ReadString(stopped.Body, "reason"));
            Assert.Equal(1, DapProtocol.ReadInt32(stopped.Body, "threadId"));

            var stack = await harness.RequestAsync("stackTrace", new { threadId = 1 });
            Assert.True(stack.Success, stack.Message);
            var frames = stack.Body.GetProperty("stackFrames");
            Assert.True(frames.GetArrayLength() > 0);
            var top = frames[0];
            var sourcePath = top.GetProperty("source").GetProperty("path").GetString() ?? "";
            Assert.Contains(".malda", sourcePath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("GeneratedProgram.cs", sourcePath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(program), Path.GetFullPath(sourcePath));
            var line = top.GetProperty("line").GetInt32();
            Assert.Equal(2, line);
            Assert.NotEqual(0, line);

            var evalOk = await harness.RequestAsync("evaluate", new { expression = "x", frameId = 1, context = "watch" });
            Assert.True(evalOk.Success, evalOk.Message);
            Assert.Equal("1", DapProtocol.ReadString(evalOk.Body, "result"));

            var evalErr = await harness.RequestAsync("evaluate", new { expression = "x = 1", frameId = 1, context = "repl" });
            Assert.False(evalErr.Success);
            Assert.False(string.IsNullOrEmpty(evalErr.Message));

            var cont = await harness.RequestAsync("continue", new { threadId = 1 });
            Assert.True(cont.Success, cont.Message);
            Assert.True(DapProtocol.ReadBoolean(cont.Body, "allThreadsContinued"));

            var end = await harness.WaitUntilAsync(m =>
                m.Type == "event" && (m.Event == "exited" || m.Event == "terminated"));
            Assert.Contains(end.Event, new[] { "exited", "terminated" });
            await harness.WaitUntilAsync(m =>
                m.Type == "event" && m.Event != end.Event && (m.Event == "exited" || m.Event == "terminated"));

            Assert.DoesNotContain("MALDA CLI", harness.RawOutput, StringComparison.Ordinal);
        }
        finally
        {
            await harness.ShutdownAsync();
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task SetBreakpoints_MapsFunctionLine_ToNextStoppable()
    {
        var dir = CreateTempDirectory("dap_map_");
        var program = Path.Combine(dir, "map.malda");
        await File.WriteAllTextAsync(program, "function f() {\nvar x = 1\nprint(x)\n}\nf()\n");
        program = Path.GetFullPath(program);

        await using var harness = await DapHarness.StartAsync();
        try
        {
            await harness.RequestAsync("initialize", new { adapterID = "malda" });
            await harness.WaitForEventAsync("initialized");
            var launch = await harness.RequestAsync("launch", new { program, stopOnEntry = false });
            Assert.True(launch.Success, launch.Message);

            var setBp = await harness.RequestAsync("setBreakpoints", new
            {
                source = new { path = program },
                breakpoints = new[] { new { line = 1 } }
            });
            Assert.True(setBp.Success, setBp.Message);
            var bp0 = setBp.Body.GetProperty("breakpoints")[0];
            Assert.True(bp0.GetProperty("verified").GetBoolean());
            Assert.Equal(2, bp0.GetProperty("line").GetInt32());

            await harness.RequestAsync("disconnect", new { });
            Assert.DoesNotContain("MALDA CLI", harness.RawOutput, StringComparison.Ordinal);
        }
        finally
        {
            await harness.ShutdownAsync();
            SafeDeleteDirectory(dir);
        }
    }

    private sealed class DapHarness : IAsyncDisposable
    {
        private readonly AnonymousPipeServerStream _toAdapter;
        private readonly AnonymousPipeServerStream _fromAdapter;
        private readonly AnonymousPipeClientStream _adapterInput;
        private readonly AnonymousPipeClientStream _adapterOutput;
        private readonly TeeReadStream _recorded;
        private readonly DapTransport _client;
        private readonly Task _adapterTask;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<DapIncoming> _inbox = new();
        private int _seq;

        public string RawOutput => _recorded.Text;

        private DapHarness(
            AnonymousPipeServerStream toAdapter,
            AnonymousPipeServerStream fromAdapter,
            AnonymousPipeClientStream adapterInput,
            AnonymousPipeClientStream adapterOutput,
            TeeReadStream recorded,
            DapTransport client,
            Task adapterTask)
        {
            _toAdapter = toAdapter;
            _fromAdapter = fromAdapter;
            _adapterInput = adapterInput;
            _adapterOutput = adapterOutput;
            _recorded = recorded;
            _client = client;
            _adapterTask = adapterTask;
        }

        public static Task<DapHarness> StartAsync()
        {
            var toAdapter = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
            var fromAdapter = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
            var adapterInput = new AnonymousPipeClientStream(PipeDirection.In, toAdapter.GetClientHandleAsString());
            var adapterOutput = new AnonymousPipeClientStream(PipeDirection.Out, fromAdapter.GetClientHandleAsString());
            toAdapter.DisposeLocalCopyOfClientHandle();
            fromAdapter.DisposeLocalCopyOfClientHandle();

            var recorded = new TeeReadStream(fromAdapter);
            var client = new DapTransport(recorded, toAdapter);
            var adapterTask = DebugAdapterSession.RunAsync(adapterInput, adapterOutput);
            return Task.FromResult(new DapHarness(
                toAdapter, fromAdapter, adapterInput, adapterOutput, recorded, client, adapterTask));
        }

        public async Task<DapIncoming> RequestAsync(string command, object? arguments)
        {
            var seq = ++_seq;
            string json;
            if (arguments == null)
            {
                json = $"{{\"seq\":{seq},\"type\":\"request\",\"command\":\"{command}\"}}";
            }
            else
            {
                var argsJson = JsonSerializer.Serialize(arguments, DapProtocol.JsonOptions);
                json = $"{{\"seq\":{seq},\"type\":\"request\",\"command\":\"{command}\",\"arguments\":{argsJson}}}";
            }

            await _client.WriteMessageAsync(json);
            return await WaitUntilAsync(m =>
                m.Type == "response" && m.Command == command && m.RequestSeq == seq);
        }

        public Task<DapIncoming> WaitForEventAsync(string eventName)
        {
            return WaitUntilAsync(m => m.Type == "event" && m.Event == eventName);
        }

        public async Task<DapIncoming> WaitUntilAsync(Func<DapIncoming, bool> predicate)
        {
            foreach (var existing in _inbox)
            {
                if (predicate(existing))
                    return existing;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                var remaining = deadline - DateTime.UtcNow;
                using var timeout = new CancellationTokenSource(remaining);
                var json = await _client.ReadMessageAsync(timeout.Token);
                if (json == null)
                    throw new EndOfStreamException("Debug adapter closed the pipe.");
                var msg = DapProtocol.Parse(json);
                _inbox.Add(msg);
                if (predicate(msg))
                    return msg;
            }

            throw new TimeoutException("Timed out waiting for a DAP message.");
        }

        public async Task ShutdownAsync()
        {
            try
            {
                _cts.Cancel();
                await _toAdapter.DisposeAsync();
                await _adapterTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await ShutdownAsync();
            _client.Dispose();
            await _recorded.DisposeAsync();
            await _fromAdapter.DisposeAsync();
            await _adapterInput.DisposeAsync();
            await _adapterOutput.DisposeAsync();
            _cts.Dispose();
        }
    }

    private sealed class TeeReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly MemoryStream _copy = new();

        public TeeReadStream(Stream inner) => _inner = inner;

        public string Text => Encoding.UTF8.GetString(_copy.ToArray());

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _inner.Read(buffer, offset, count);
            if (n > 0)
                _copy.Write(buffer, offset, n);
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (n > 0)
                _copy.Write(buffer.Span[..n]);
            return n;
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _copy.Dispose();
            base.Dispose(disposing);
        }
    }
}
