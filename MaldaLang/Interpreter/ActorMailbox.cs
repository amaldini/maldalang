// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System.Threading.Channels;

public class ActorMailbox
{
    private readonly Channel<Message> _channel;
    private readonly ChannelWriter<Message> _writer;
    private readonly ChannelReader<Message> _reader;
    private bool _closed = false;
    
    public ActorMailbox()
    {
        var options = new UnboundedChannelOptions
        {
            SingleReader = true, // Each actor processes messages sequentially
            SingleWriter = false // Multiple actors can send to this mailbox
        };
        
        _channel = Channel.CreateUnbounded<Message>(options);
        _writer = _channel.Writer;
        _reader = _channel.Reader;
    }
    
    public void Send(Message message)
    {
        if (_closed)
            return;
        
        if (!_writer.TryWrite(message))
        {
            // Match transpiled actor semantics: messages sent after stop are ignored.
            return;
        }
    }
    
    public async Task<Message> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (_closed && !await _reader.WaitToReadAsync(cancellationToken))
        {
            throw new RuntimeException("Mailbox is closed and no messages available.");
        }
        
        if (await _reader.WaitToReadAsync(cancellationToken))
        {
            if (_reader.TryRead(out var message))
            {
                return message;
            }
        }
        
        throw new RuntimeException("Failed to receive message from mailbox.");
    }
    
    public void Close()
    {
        _closed = true;
        _writer.Complete();
    }
    
    public bool IsClosed => _closed;
}
