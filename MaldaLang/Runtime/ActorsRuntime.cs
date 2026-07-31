// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.Actors;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/// <summary>
/// Opaque reference to an actor instance managed by <see cref="ActorsRuntime"/>.
/// </summary>
public readonly struct ActorRef
{
    internal Guid Id { get; }

    internal ActorRef(Guid id)
    {
        Id = id;
    }

    public override string ToString()
    {
        return $"<ActorRef {Id}>";
    }
}

/// <summary>
/// Marker interface for actor classes generated from MALDA <c>actor</c> declarations.
/// </summary>
public interface IActor
{
}

/// <summary>
/// Optional metadata exposed by generated actor classes so the runtime can
/// preserve interpreter-style actor message declarations in transpiled code.
/// </summary>
public interface IActorMessageMetadata
{
    bool IsDeclaredMessage(string name);
}

internal sealed class ActorCell
{
    public object Instance { get; }
    public Channel<Action> Mailbox { get; }
    public Channel<ReceiveEnvelope> ReceiveMailbox { get; }
    public CancellationTokenSource Cancellation { get; }
    public Task LoopTask { get; }

    public ActorCell(object instance)
    {
        Instance = instance;
        Cancellation = new CancellationTokenSource();

        var options = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        };

        Mailbox = Channel.CreateUnbounded<Action>(options);
        ReceiveMailbox = Channel.CreateUnbounded<ReceiveEnvelope>(options);
        LoopTask = RunLoopAsync();
    }

    private async Task RunLoopAsync()
    {
        var reader = Mailbox.Reader;

        try
        {
            while (await reader.WaitToReadAsync(Cancellation.Token).ConfigureAwait(false))
            {
                // Process messages one at a time, checking cancellation after each
                // This allows self.stop() to stop the actor after the current message
                while (reader.TryRead(out var action))
                {
                    action();
                    
                    // Check for cancellation after processing each message
                    // This allows self.stop() to stop after the current message handler
                    if (Cancellation.Token.IsCancellationRequested)
                    {
                        break;
                    }
                }
                
                // If cancellation was requested, exit the loop
                if (Cancellation.Token.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown - cancellation token was cancelled
        }
    }
}

internal sealed class ReceiveEnvelope
{
    public object? Payload { get; }
    public ActorRef? Sender { get; }
    public Guid? CorrelationId { get; }

    public ReceiveEnvelope(object? payload, ActorRef? sender, Guid? correlationId)
    {
        Payload = payload;
        Sender = sender;
        CorrelationId = correlationId;
    }
}

/// <summary>
/// Simple actors runtime used by transpiled executables.
/// This is intentionally small and reflection-based so it can be replaced later.
/// </summary>
public static class ActorsRuntime
{
    private static readonly ConcurrentDictionary<Guid, ActorCell> _actors = new();
    private static readonly AsyncLocal<ActorRef?> _currentActor = new();
    private static readonly AsyncLocal<ActorRef?> _currentSender = new();
    private static readonly AsyncLocal<Guid?> _currentCorrelationId = new();
    private static readonly AsyncLocal<ReceiveEnvelope?> _currentReceiveEnvelope = new();

    private sealed class CallbackRegistration
    {
        public Func<object?, Task> Callback { get; }
        public ActorRef Sender { get; }
        public CancellationTokenSource? TimeoutCancellation { get; }
        public Func<object?, Task>? TimeoutErrorHandler { get; }
        public ActorRef Target { get; }
        public string HandlerName { get; }

        public CallbackRegistration(
            Func<object?, Task> callback, 
            ActorRef sender,
            CancellationTokenSource? timeoutCancellation = null,
            Func<object?, Task>? timeoutErrorHandler = null,
            ActorRef target = default,
            string handlerName = "")
        {
            Callback = callback;
            Sender = sender;
            TimeoutCancellation = timeoutCancellation;
            TimeoutErrorHandler = timeoutErrorHandler;
            Target = target;
            HandlerName = handlerName;
        }
    }

    private static readonly ConcurrentDictionary<Guid, CallbackRegistration> _callbacks = new();

    /// <summary>
    /// Spawn a new actor instance and start its message-processing loop.
    /// </summary>
    /// <param name="actorInstance">The actor object created from a MALDA <c>actor</c> declaration.</param>
    /// <returns>An opaque <see cref="ActorRef"/> that can be used with <see cref="Send"/>.</returns>
    public static ActorRef Spawn(object actorInstance)
    {
        if (actorInstance is null)
        {
            throw new ArgumentNullException(nameof(actorInstance));
        }

        var id = Guid.NewGuid();
        var cell = new ActorCell(actorInstance);

        if (!_actors.TryAdd(id, cell))
        {
            throw new InvalidOperationException("Failed to register actor in runtime.");
        }

        return new ActorRef(id);
    }

    /// <summary>
    /// Send a message to an actor using call-style syntax:
    /// <c>send target.handlerName(arg1, arg2, ...);</c>
    /// or without handler name:
    /// <c>send target(arg1, arg2, ...);</c>
    /// </summary>
    public static void Send(ActorRef target, string? handlerName, params object?[] arguments)
    {
        if (!_actors.TryGetValue(target.Id, out var cell))
        {
            throw new InvalidOperationException("Target actor not found.");
        }

        EnqueueInvocation(target, cell, handlerName, arguments ?? Array.Empty<object?>(), sender: null, correlationId: null);
    }

    /// <summary>
    /// Asynchronously receive the next message payload for the current actor.
    /// Matches interpreter semantics for receive() in actor handlers.
    /// </summary>
    public static async Task<object?> ReceiveAsync()
    {
        if (_currentActor.Value is not ActorRef current)
        {
            throw new InvalidOperationException("receive() can only be used within an actor message handler.");
        }

        if (!_actors.TryGetValue(current.Id, out var cell))
        {
            throw new InvalidOperationException("Current actor not found in runtime.");
        }

        try
        {
            if (_currentReceiveEnvelope.Value is ReceiveEnvelope currentEnvelope)
            {
                _currentReceiveEnvelope.Value = null;
                _currentSender.Value = currentEnvelope.Sender;
                _currentCorrelationId.Value = currentEnvelope.CorrelationId;
                return currentEnvelope.Payload;
            }

            var reader = cell.ReceiveMailbox.Reader;
            while (await reader.WaitToReadAsync(cell.Cancellation.Token).ConfigureAwait(false))
            {
                if (reader.TryRead(out var envelope))
                {
                    _currentSender.Value = envelope.Sender;
                    _currentCorrelationId.Value = envelope.CorrelationId;
                    return envelope.Payload;
                }
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            // Actor is stopping; treat as no further messages
            return null;
        }
    }

    /// <summary>
    /// Call-style send with callback:
    /// <c>send target.handler(args) then (result) { ... };</c>
    /// or without handler name:
    /// <c>send target(args) then (result) { ... };</c>
    /// </summary>
    public static void SendWithCallback(
        ActorRef sender, 
        ActorRef target, 
        string? handlerName, 
        Func<object?, Task> __callback, 
        int? timeoutMs = null,
        Func<object?, Task>? timeoutErrorHandler = null,
        params object?[] arguments)
    {
        if (__callback == null) throw new ArgumentNullException(nameof(__callback));

        if (!_actors.TryGetValue(target.Id, out var cell))
        {
            throw new InvalidOperationException("Target actor not found.");
        }

        var correlationId = Guid.NewGuid();
        CancellationTokenSource? timeoutCts = null;

        // Set up timeout if specified
        if (timeoutMs.HasValue && timeoutMs.Value > 0)
        {
            timeoutCts = new CancellationTokenSource();

            // Start timeout task
            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait for the timeout duration
                    // If cancelled (reply arrived), this will throw OperationCanceledException
                    await Task.Delay(timeoutMs.Value, timeoutCts.Token).ConfigureAwait(false);
                    
                    // Timeout occurred - Task.Delay completed normally (not cancelled)
                    // Try to remove callback and invoke error handler
                    if (_callbacks.TryRemove(correlationId, out var registration))
                    {
                        if (registration.TimeoutErrorHandler != null && _actors.TryGetValue(registration.Sender.Id, out var senderCell))
                        {
                            var errorMessage = $"Request to {registration.Target}.{registration.HandlerName} timed out after {timeoutMs.Value}ms";
                            
                            void TimeoutErrorAction()
                            {
                                var previousActor = _currentActor.Value;
                                var previousSender = _currentSender.Value;
                                var previousCorrelation = _currentCorrelationId.Value;

                                _currentActor.Value = registration.Sender;
                                _currentSender.Value = null;
                                _currentCorrelationId.Value = null;

                                try
                                {
                                    // Run the timeout error handler synchronously in sender's actor loop
                                    registration.TimeoutErrorHandler(errorMessage).GetAwaiter().GetResult();
                                }
                                finally
                                {
                                    _currentActor.Value = previousActor;
                                    _currentSender.Value = previousSender;
                                    _currentCorrelationId.Value = previousCorrelation;
                                }
                            }

                            if (!senderCell.Mailbox.Writer.TryWrite(TimeoutErrorAction))
                            {
                                // Sender actor may have stopped, ignore
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Timeout was cancelled (reply arrived), ignore
                }
            });
        }

        var registration = new CallbackRegistration(
            __callback, 
            sender, 
            timeoutCts,
            timeoutErrorHandler,
            target,
            handlerName ?? "handle");

        if (!_callbacks.TryAdd(correlationId, registration))
        {
            timeoutCts?.Dispose();
            throw new InvalidOperationException("Failed to register actor callback.");
        }

        EnqueueInvocation(target, cell, handlerName, arguments ?? Array.Empty<object?>(), sender, correlationId);
    }

    private static void EnqueueInvocation(ActorRef actor, ActorCell cell, string? handlerName, object?[] arguments, ActorRef? sender, Guid? correlationId)
    {
        if (string.IsNullOrEmpty(handlerName))
        {
            var payload = arguments != null && arguments.Length > 0 ? arguments[0] : null;
            var envelope = new ReceiveEnvelope(payload, sender, correlationId);
            if (cell.ReceiveMailbox.Writer == null || !cell.ReceiveMailbox.Writer.TryWrite(envelope))
            {
                return;
            }

            return;
        }

        if (cell.Instance is IActorMessageMetadata metadata && metadata.IsDeclaredMessage(handlerName))
        {
            var declaredMessage = new ReceiveEnvelope(
                CreateDeclaredMessagePayload(handlerName, arguments),
                sender,
                correlationId);

            if (cell.ReceiveMailbox.Writer == null || !cell.ReceiveMailbox.Writer.TryWrite(declaredMessage))
            {
                // Actor stopped, mailbox is completed - silently ignore the message
                return;
            }

            return;
        }

        var currentReceiveEnvelope = arguments != null && arguments.Length > 0
            ? new ReceiveEnvelope(arguments[0], sender, correlationId)
            : null;

        void Action()
        {
            var previousActor = _currentActor.Value;
            var previousSender = _currentSender.Value;
            var previousCorrelation = _currentCorrelationId.Value;
            var previousReceiveEnvelope = _currentReceiveEnvelope.Value;

            _currentActor.Value = actor;
            _currentSender.Value = sender;
            _currentCorrelationId.Value = correlationId;
            _currentReceiveEnvelope.Value = currentReceiveEnvelope;

            try
            {
                var instance = cell.Instance;
                var type = instance.GetType();

                var method = type.GetMethod(
                    handlerName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );

                if (method == null)
                {
                    throw new InvalidOperationException(
                        $"No message handler '{handlerName}' found on actor type '{type.Name}'.");
                }

                var parameters = method.GetParameters();
                object?[] invokeArgs;

                if (parameters.Length == 0)
                {
                    invokeArgs = Array.Empty<object?>();
                }
                else
                {
                    invokeArgs = arguments.Length == parameters.Length
                        ? arguments
                        : ResizeArguments(arguments, parameters.Length);
                }

                method.Invoke(instance, invokeArgs);
            }
            finally
            {
                _currentActor.Value = previousActor;
                _currentSender.Value = previousSender;
                _currentCorrelationId.Value = previousCorrelation;
                _currentReceiveEnvelope.Value = previousReceiveEnvelope;
            }
        }

        // Try to enqueue the action - if actor is stopped (mailbox completed), silently ignore
        if (cell.Mailbox.Writer == null || !cell.Mailbox.Writer.TryWrite(Action))
        {
            // Actor stopped, mailbox is completed - silently ignore the message
            return;
        }
    }

    private static MaldaLang.Interpreter.RuntimeValue CreateDeclaredMessagePayload(string handlerName, object?[] arguments)
    {
        var payload = new List<MaldaLang.Interpreter.RuntimeValue>(arguments.Length);
        foreach (var argument in arguments)
        {
            payload.Add(ToRuntimeValue(argument));
        }

        return MaldaLang.Interpreter.RuntimeValue.Variant(handlerName, payload);
    }

    private static MaldaLang.Interpreter.RuntimeValue ToRuntimeValue(object? value)
    {
        return value switch
        {
            null => MaldaLang.Interpreter.RuntimeValue.Null(),
            MaldaLang.Interpreter.RuntimeValue runtimeValue => runtimeValue,
            int intValue => MaldaLang.Interpreter.RuntimeValue.Integer(intValue),
            long longValue => MaldaLang.Interpreter.RuntimeValue.Integer((int)longValue),
            double doubleValue => MaldaLang.Interpreter.RuntimeValue.Float(doubleValue),
            float floatValue => MaldaLang.Interpreter.RuntimeValue.Float(floatValue),
            bool boolValue => MaldaLang.Interpreter.RuntimeValue.Boolean(boolValue),
            string stringValue => MaldaLang.Interpreter.RuntimeValue.String(stringValue),
            _ => MaldaLang.Interpreter.RuntimeValue.String(value.ToString() ?? "")
        };
    }

    private static object?[] ResizeArguments(object?[] original, int targetLength)
    {
        if (original.Length == targetLength)
        {
            return original;
        }

        var resized = new object?[targetLength];
        Array.Copy(original, resized, Math.Min(original.Length, targetLength));
        return resized;
    }

    /// <summary>
    /// Get the current actor reference for the running message handler.
    /// Used to implement the <c>self</c> expression in transpiled code.
    /// </summary>
    public static ActorRef GetSelf()
    {
        if (_currentActor.Value is ActorRef current)
            return current;

        throw new InvalidOperationException("'self' can only be used within an actor message handler.");
    }

    /// <summary>
    /// Reply to a message that was sent with a callback.
    /// Executes the callback in the sender actor's context.
    /// </summary>
    public static void Reply(object? value)
    {
        if (_currentCorrelationId.Value is not Guid correlationId)
            throw new InvalidOperationException("reply() can only be used when handling a message sent with a callback.");

        if (!_callbacks.TryRemove(correlationId, out var registration))
            throw new InvalidOperationException("No callback found for this reply.");

        // Cancel timeout if it exists (reply arrived before timeout)
        registration.TimeoutCancellation?.Cancel();
        registration.TimeoutCancellation?.Dispose();

        var sender = registration.Sender;
        if (!_actors.TryGetValue(sender.Id, out var senderCell))
            throw new InvalidOperationException("Sender actor not found for reply.");

        void CallbackAction()
        {
            var previousActor = _currentActor.Value;
            var previousSender = _currentSender.Value;
            var previousCorrelation = _currentCorrelationId.Value;

            _currentActor.Value = sender;
            _currentSender.Value = null;
            _currentCorrelationId.Value = null;

            try
            {
                // Run the callback synchronously in this actor's loop
                registration.Callback(value).GetAwaiter().GetResult();
            }
            finally
            {
                _currentActor.Value = previousActor;
                _currentSender.Value = previousSender;
                _currentCorrelationId.Value = previousCorrelation;
            }
        }

        if (!senderCell.Mailbox.Writer.TryWrite(CallbackAction))
        {
            throw new InvalidOperationException("Failed to enqueue callback execution on sender actor.");
        }
    }

    /// <summary>
    /// Stop a specific actor.
    /// </summary>
    public static void Stop(ActorRef actor)
    {
        if (_actors.TryGetValue(actor.Id, out var cell))
        {
            cell.Cancellation.Cancel();
            // Complete the mailboxes to prevent new messages from being enqueued
            // Check if writer is not null (channel not already completed) before completing
            // Wrap in try-catch to handle case where channel is already completed
            var mailboxWriter = cell.Mailbox.Writer;
            if (mailboxWriter != null)
            {
                try
                {
                    mailboxWriter.Complete();
                }
                catch (InvalidOperationException)
                {
                    // Channel already completed, ignore
                }
            }
            
            var receiveMailboxWriter = cell.ReceiveMailbox.Writer;
            if (receiveMailboxWriter != null)
            {
                try
                {
                    receiveMailboxWriter.Complete();
                }
                catch (InvalidOperationException)
                {
                    // Channel already completed, ignore
                }
            }
        }
    }

    /// <summary>
    /// Request shutdown of all actors and wait for their loops to complete.
    /// Called from transpiled <c>Main</c> before exit to allow graceful shutdown.
    /// </summary>
    public static async Task ShutdownAsync()
    {
        var cells = _actors.ToArray();

        foreach (var kvp in cells)
        {
            kvp.Value.Cancellation.Cancel();
        }

        var tasks = new List<Task>(cells.Length);
        foreach (var kvp in cells)
        {
            tasks.Add(kvp.Value.LoopTask);
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resets the runtime state for testing purposes.
    /// Stops all running actors, clears callbacks, and resets async local state.
    /// </summary>
    public static void ResetForTesting()
    {
        // Stop all actors
        var cells = _actors.ToArray();
        foreach (var kvp in cells)
        {
            kvp.Value.Cancellation.Cancel();
        }

        // Wait for tasks to complete (with timeout)
        var tasks = new List<Task>(cells.Length);
        foreach (var kvp in cells)
        {
            tasks.Add(kvp.Value.LoopTask);
        }

        if (tasks.Count > 0)
        {
            Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(5));
        }

        // Clear collections
        _actors.Clear();
        _callbacks.Clear();
        
        // Reset async local state (these are per-thread, but we clear them anyway)
        _currentActor.Value = null;
        _currentSender.Value = null;
        _currentCorrelationId.Value = null;
    }
}

