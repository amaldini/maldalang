// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.UI;

using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

public sealed class UiSession
{
    private readonly ConcurrentQueue<UiEvent> _events = new();
    private readonly object _lock = new();
    private UiNode? _currentTree;
    private int _nextOutSequence = 1;
    private int _expectedInSequence = 1;
    private readonly Dictionary<string, HashSet<string>> _lifecycleHooks = new(StringComparer.Ordinal);
    private int _maxEventQueueDepth = 2048;

    public string SessionId { get; }
    public DateTime LastAccessUtc { get; private set; } = DateTime.UtcNow;
    public int MaxPatchCountPerEnvelope { get; set; } = 4096;
    public int MaxPayloadSizeBytes { get; set; } = 256 * 1024;
    public string ProtocolVersion => UiProtocol.Version;

    public UiSession(string sessionId)
    {
        SessionId = sessionId;
    }

    public RuntimeValue Mount(UiNode root)
    {
        lock (_lock)
        {
            Touch();
            _currentTree = root;
            var mountedComponents = OrderedComponentIds(CollectComponentIds(root));
            foreach (var componentId in mountedComponents)
            {
                EnqueueLifecycleEvent("onInit", componentId, RuntimeValue.Null());
                EnqueueLifecycleEvent("onMount", componentId, RuntimeValue.Null());
            }

            var patches = new List<UiPatch> { new(UiPatchOperation.ReplaceNode, "/", value: root.ToRuntimeValue()) };
            return BuildPatchEnvelope("mount", patches);
        }
    }

    public RuntimeValue Render(UiNode nextTree)
    {
        lock (_lock)
        {
            Touch();
            var oldComponentIds = CollectComponentIds(_currentTree);
            var newComponentIds = CollectComponentIds(nextTree);
            foreach (var componentId in OrderedComponentIds(newComponentIds))
            {
                EnqueueLifecycleEvent("onPreRender", componentId, RuntimeValue.Null());
            }

            var patches = UiDiffEngine.Diff(_currentTree, nextTree);
            if (patches.Count > MaxPatchCountPerEnvelope)
            {
                // Fallback: force full snapshot replacement to avoid giant patch storms.
                patches = new List<UiPatch> { new(UiPatchOperation.ReplaceNode, "/", value: nextTree.ToRuntimeValue()) };
            }

            _currentTree = nextTree;

            foreach (var componentId in OrderedComponentIds(newComponentIds))
            {
                if (!oldComponentIds.Contains(componentId))
                {
                    EnqueueLifecycleEvent("onMount", componentId, RuntimeValue.Null());
                    EnqueueLifecycleEvent("onLoad", componentId, RuntimeValue.Null());
                }
                else
                {
                    EnqueueLifecycleEvent("onUpdate", componentId, RuntimeValue.Null());
                }
            }

            foreach (var componentId in OrderedComponentIds(oldComponentIds))
            {
                if (!newComponentIds.Contains(componentId))
                {
                    EnqueueLifecycleEvent("onUnmount", componentId, RuntimeValue.Null());
                    EnqueueLifecycleEvent("onDispose", componentId, RuntimeValue.Null());
                }
            }

            return BuildPatchEnvelope("patch", patches);
        }
    }

    public void EnqueueEvent(UiEvent uiEvent)
    {
        Touch();
        if (_events.Count >= _maxEventQueueDepth)
        {
            // Backpressure: drop oldest event to keep memory bounded.
            _events.TryDequeue(out _);
        }

        _events.Enqueue(uiEvent);
    }

    public bool TryDequeueEvent(out UiEvent? uiEvent)
    {
        Touch();
        if (_events.TryDequeue(out var ev))
        {
            uiEvent = ev;
            return true;
        }

        uiEvent = null;
        return false;
    }

    public void ConfigureQueueDepth(int maxQueueDepth)
    {
        if (maxQueueDepth < 32)
        {
            throw new Exception("maxQueueDepth must be >= 32");
        }

        _maxEventQueueDepth = maxQueueDepth;
    }

    public RuntimeValue BuildAck(int sequence, string? envelopeId = null)
    {
        return BuildControlEnvelope("ack", sequence, envelopeId, null);
    }

    public RuntimeValue BuildNack(int sequence, string code, string message, string? envelopeId = null)
    {
        var error = new JsonObject();
        error.Set("code", RuntimeValue.String(code));
        error.Set("message", RuntimeValue.String(message));
        return BuildControlEnvelope("nack", sequence, envelopeId, RuntimeValue.Object(error));
    }

    public RuntimeValue BuildError(string code, string message)
    {
        var err = new JsonObject();
        err.Set("code", RuntimeValue.String(code));
        err.Set("message", RuntimeValue.String(message));
        err.Set("sessionId", RuntimeValue.String(SessionId));
        err.Set("version", RuntimeValue.String(UiProtocol.Version));
        err.Set("serverTimeUtc", RuntimeValue.String(DateTime.UtcNow.ToString("O")));
        err.Set("sequence", RuntimeValue.Integer(_nextOutSequence++));
        err.Set("envelopeId", RuntimeValue.String(Guid.NewGuid().ToString("N")));
        var envelope = new JsonObject();
        envelope.Set("type", RuntimeValue.String("error"));
        envelope.Set("error", RuntimeValue.Object(err));
        return RuntimeValue.Object(envelope);
    }

    public RuntimeValue BuildResyncEnvelope()
    {
        var patches = new List<UiPatch>();
        if (_currentTree != null)
        {
            patches.Add(new UiPatch(UiPatchOperation.ReplaceNode, "/", value: _currentTree.ToRuntimeValue()));
        }

        return BuildPatchEnvelope("resync", patches);
    }

    public bool TryAcceptInboundSequence(int sequence, out string? reason)
    {
        Touch();
        if (sequence < _expectedInSequence)
        {
            reason = $"duplicate_or_stale_expected_{_expectedInSequence}";
            return false;
        }

        if (sequence > _expectedInSequence)
        {
            reason = $"gap_expected_{_expectedInSequence}";
            return false;
        }

        _expectedInSequence++;
        reason = null;
        return true;
    }

    public void RegisterLifecycleHook(string eventName, string componentId)
    {
        Touch();
        if (!_lifecycleHooks.TryGetValue(eventName, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            _lifecycleHooks[eventName] = set;
        }

        set.Add(componentId);
    }

    public void EmitLifecycleHook(string eventName, string componentId, RuntimeValue payload)
    {
        lock (_lock)
        {
            Touch();
            EnqueueLifecycleEvent(eventName, componentId, payload);
        }
    }

    public RuntimeValue SnapshotAsRuntimeValue()
    {
        var snapshot = new JsonObject();
        snapshot.Set("sessionId", RuntimeValue.String(SessionId));
        snapshot.Set("version", RuntimeValue.String(UiProtocol.Version));
        snapshot.Set("nextSequence", RuntimeValue.Integer(_nextOutSequence));
        snapshot.Set("expectedInboundSequence", RuntimeValue.Integer(_expectedInSequence));
        snapshot.Set("hasTree", RuntimeValue.Boolean(_currentTree != null));
        snapshot.Set("tree", _currentTree?.ToRuntimeValue() ?? RuntimeValue.Null());
        snapshot.Set("lastAccessUtc", RuntimeValue.String(LastAccessUtc.ToString("O")));
        return RuntimeValue.Object(snapshot);
    }

    public void DisposeTrackedComponents()
    {
        lock (_lock)
        {
            Touch();
            var trackedComponents = OrderedComponentIds(CollectComponentIds(_currentTree));
            foreach (var componentId in trackedComponents)
            {
                EnqueueLifecycleEvent("onDispose", componentId, RuntimeValue.Null());
            }

            _currentTree = null;
        }
    }

    private RuntimeValue BuildPatchEnvelope(string messageType, List<UiPatch> patches)
    {
        var patchValues = new List<RuntimeValue>(patches.Count);
        foreach (var patch in patches)
        {
            patchValues.Add(patch.ToRuntimeValue());
        }

        return BuildPatchEnvelope(messageType, patchValues);
    }

    private RuntimeValue BuildPatchEnvelope(string messageType, List<RuntimeValue> patchValues)
    {
        var envelope = new JsonObject();
        envelope.Set("type", RuntimeValue.String(messageType));
        envelope.Set("version", RuntimeValue.String(UiProtocol.Version));
        envelope.Set("sessionId", RuntimeValue.String(SessionId));
        envelope.Set("sequence", RuntimeValue.Integer(_nextOutSequence++));
        envelope.Set("envelopeId", RuntimeValue.String(Guid.NewGuid().ToString("N")));
        envelope.Set("serverTimeUtc", RuntimeValue.String(DateTime.UtcNow.ToString("O")));
        envelope.Set("patches", RuntimeValue.Array(patchValues));
        envelope.Set("treeHash", RuntimeValue.String(ComputeTreeHash(_currentTree)));
        var runtimeEnvelope = RuntimeValue.Object(envelope);
        EnforcePayloadSizeLimit(runtimeEnvelope, messageType);
        return runtimeEnvelope;
    }

    private RuntimeValue BuildControlEnvelope(string messageType, int sequence, string? envelopeId, RuntimeValue? error)
    {
        var envelope = new JsonObject();
        envelope.Set("type", RuntimeValue.String(messageType));
        envelope.Set("version", RuntimeValue.String(UiProtocol.Version));
        envelope.Set("sessionId", RuntimeValue.String(SessionId));
        envelope.Set("sequence", RuntimeValue.Integer(_nextOutSequence++));
        envelope.Set("ackSequence", RuntimeValue.Integer(sequence));
        envelope.Set("envelopeId", RuntimeValue.String(envelopeId ?? Guid.NewGuid().ToString("N")));
        envelope.Set("serverTimeUtc", RuntimeValue.String(DateTime.UtcNow.ToString("O")));
        if (error != null)
        {
            envelope.Set("error", error);
        }
        return RuntimeValue.Object(envelope);
    }

    private void EnqueueLifecycleEvent(string eventName, string componentId, RuntimeValue payload)
    {
        if (!_lifecycleHooks.TryGetValue(eventName, out var set) || !set.Contains(componentId))
        {
            return;
        }

        var lifecyclePayload = new JsonObject();
        lifecyclePayload.Set("componentId", RuntimeValue.String(componentId));
        lifecyclePayload.Set("eventName", RuntimeValue.String(eventName));
        lifecyclePayload.Set("payload", payload);
        EnqueueEvent(new UiEvent("lifecycle", "/", RuntimeValue.Object(lifecyclePayload)));
    }

    private static HashSet<string> CollectComponentIds(UiNode? root)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (root == null)
        {
            return set;
        }

        Visit(root, set);
        return set;
    }

    private static List<string> OrderedComponentIds(HashSet<string> componentIds)
    {
        var ordered = new List<string>(componentIds);
        ordered.Sort(StringComparer.Ordinal);
        return ordered;
    }

    private static void Visit(UiNode node, HashSet<string> collector)
    {
        if (node.Props.TryGetValue("componentId", out var componentValue) && componentValue.Type == ValueType.String)
        {
            var componentId = componentValue.AsString();
            if (!string.IsNullOrWhiteSpace(componentId))
            {
                collector.Add(componentId);
            }
        }
        else if (!string.IsNullOrWhiteSpace(node.Key))
        {
            collector.Add(node.Key);
        }

        foreach (var child in node.Children)
        {
            Visit(child, collector);
        }
    }

    private static string ComputeTreeHash(UiNode? root)
    {
        if (root == null)
        {
            return "null";
        }

        return root.ToRuntimeValue().ToString().GetHashCode().ToString("X8");
    }

    private void EnforcePayloadSizeLimit(RuntimeValue envelope, string messageType)
    {
        var payloadBytes = Encoding.UTF8.GetByteCount(RuntimeValueToJson(envelope));
        if (payloadBytes > MaxPayloadSizeBytes)
        {
            throw new Exception(
                $"UI {messageType} envelope exceeds maxPayloadBytes ({MaxPayloadSizeBytes} bytes). Actual: {payloadBytes} bytes.");
        }
    }

    private static string RuntimeValueToJson(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Integer => value.AsInteger().ToString(),
            ValueType.Float => value.AsFloat().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ValueType.String => JsonSerializer.Serialize(value.AsString()),
            ValueType.Boolean => value.AsBoolean() ? "true" : "false",
            ValueType.Null => "null",
            ValueType.Array => "[" + string.Join(",", value.AsArray().Select(RuntimeValueToJson)) + "]",
            ValueType.Object => ObjectToJson(value.AsObject()),
            _ => JsonSerializer.Serialize(value.ToString())
        };
    }

    private static string ObjectToJson(ObjectInstance obj)
    {
        if (obj is JsonObject jsonObj)
        {
            return "{" + string.Join(",", jsonObj.GetProperties().Select(kvp =>
            {
                var key = JsonSerializer.Serialize(kvp.Key);
                var val = RuntimeValueToJson(kvp.Value);
                return key + ":" + val;
            })) + "}";
        }

        if (obj is DictionaryInstance dict)
        {
            return "{" + string.Join(",", dict.GetEntries().Select(kvp =>
            {
                var key = JsonSerializer.Serialize(kvp.Key);
                var val = RuntimeValueToJson(kvp.Value);
                return key + ":" + val;
            })) + "}";
        }

        return JsonSerializer.Serialize(obj.ToString());
    }

    private void Touch()
    {
        LastAccessUtc = DateTime.UtcNow;
    }
}
