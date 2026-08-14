// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DebugAdapter;

using System.Globalization;
using System.Text;

/// <summary>
/// LSP-style <c>Content-Length</c> framed JSON-RPC over a pair of streams.
/// </summary>
public sealed class DapTransport : IDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly byte[] _readBuffer = new byte[4096];
    private int _readOffset;
    private int _readCount;
    private bool _disposed;

    public DapTransport(Stream input, Stream output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Reads one framed JSON payload, or <c>null</c> on EOF.
    /// </summary>
    public async Task<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        var header = await ReadUntilAsync(new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' }, cancellationToken)
            .ConfigureAwait(false);
        if (header == null)
            return null;

        var length = ParseContentLength(Encoding.ASCII.GetString(header));
        if (length < 0)
            throw new InvalidDataException("DAP message is missing a Content-Length header.");

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await ReadAsync(body, read, length - read, cancellationToken).ConfigureAwait(false);
            if (n == 0)
                throw new EndOfStreamException("DAP stream ended inside a message body.");
            read += n;
        }

        return Encoding.UTF8.GetString(body);
    }

    public async Task WriteMessageAsync(string json, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n\r\n");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _writeLock.Dispose();
    }

    private static int ParseContentLength(string headers)
    {
        var lines = headers.Split(new[] { "\r\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var name = line[..colon].Trim();
            if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = line[(colon + 1)..].Trim();
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) && length >= 0)
                return length;
        }

        return -1;
    }

    private async Task<byte[]?> ReadUntilAsync(byte[] sentinel, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var match = 0;
        while (match < sentinel.Length)
        {
            var b = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            if (b < 0)
            {
                if (buffer.Length == 0 && match == 0)
                    return null;
                throw new EndOfStreamException("DAP stream ended inside a message header.");
            }

            buffer.WriteByte((byte)b);
            if (b == sentinel[match])
                match++;
            else
                match = b == sentinel[0] ? 1 : 0;
        }

        return buffer.ToArray();
    }

    private async Task<int> ReadByteAsync(CancellationToken cancellationToken)
    {
        if (_readOffset >= _readCount)
        {
            _readOffset = 0;
            _readCount = await _input.ReadAsync(_readBuffer.AsMemory(0, _readBuffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (_readCount == 0)
                return -1;
        }

        return _readBuffer[_readOffset++];
    }

    private async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var copied = 0;
        while (copied < count && _readOffset < _readCount)
            buffer[offset + copied++] = _readBuffer[_readOffset++];

        if (copied == count)
            return copied;

        var n = await _input.ReadAsync(buffer.AsMemory(offset + copied, count - copied), cancellationToken)
            .ConfigureAwait(false);
        return copied + n;
    }
}
