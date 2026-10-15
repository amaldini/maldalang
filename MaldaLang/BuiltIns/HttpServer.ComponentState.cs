// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using System.IO;
using MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;

public partial class HttpServerInstance
{
    public static void SetComponentState(string componentId, string key, RuntimeValue value)
    {
        lock (_componentStateLock)
        {
            CleanupExpiredComponentStateLocked();
            if (!_componentStateStore.TryGetValue(componentId, out var entry))
            {
                EnsureComponentCapacityLocked(throwIfCannotEvict: true);
                entry = new ComponentStateEntry();
                _componentStateStore[componentId] = entry;
            }

            entry.LastAccessUtc = DateTime.UtcNow;
            if (!entry.Values.ContainsKey(key) && entry.Values.Count >= _componentStateMaxKeysPerComponent)
            {
                var oldestKey = entry.Values.Keys.FirstOrDefault();
                if (oldestKey != null)
                {
                    entry.Values.Remove(oldestKey);
                }
            }

            entry.Values[key] = SnapshotComponentStateValue(value);
        }
    }

    public static void PinComponentState(string componentId)
    {
        lock (_componentStateLock)
        {
            CleanupExpiredComponentStateLocked();
            if (!_componentStateStore.TryGetValue(componentId, out var entry))
            {
                EnsureComponentCapacityLocked(throwIfCannotEvict: true);
                entry = new ComponentStateEntry();
                _componentStateStore[componentId] = entry;
            }

            entry.Pinned = true;
            entry.LastAccessUtc = DateTime.UtcNow;
        }
    }

    public static void UnpinComponentState(string componentId)
    {
        lock (_componentStateLock)
        {
            CleanupExpiredComponentStateLocked();
            if (_componentStateStore.TryGetValue(componentId, out var entry))
            {
                entry.Pinned = false;
                entry.LastAccessUtc = DateTime.UtcNow;
            }
        }
    }

    public static bool IsComponentStatePinned(string componentId)
    {
        lock (_componentStateLock)
        {
            CleanupExpiredComponentStateLocked();
            return _componentStateStore.TryGetValue(componentId, out var entry) && entry.Pinned;
        }
    }

    public static RuntimeValue GetComponentState(string componentId, string key)
    {
        lock (_componentStateLock)
        {
            CleanupExpiredComponentStateLocked();
            if (!_componentStateStore.TryGetValue(componentId, out var entry))
            {
                return RuntimeValue.Null();
            }

            entry.LastAccessUtc = DateTime.UtcNow;
            if (!entry.Values.TryGetValue(key, out var value))
            {
                return RuntimeValue.Null();
            }

            return SnapshotComponentStateValue(value);
        }
    }

    public static RuntimeValue GetComponentStateObject(string componentId)
    {
        var obj = new JsonObject();
        lock (_componentStateLock)
        {
            CleanupExpiredComponentStateLocked();
            if (_componentStateStore.TryGetValue(componentId, out var entry))
            {
                entry.LastAccessUtc = DateTime.UtcNow;
                foreach (var kvp in entry.Values)
                {
                    obj.Set(kvp.Key, SnapshotComponentStateValue(kvp.Value));
                }
            }
        }

        return RuntimeValue.Object(obj);
    }

    private static RuntimeValue SnapshotComponentStateValue(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Array => RuntimeValue.Array(value.AsArray().Select(SnapshotComponentStateValue).ToList()),
            ValueType.Object => RuntimeValue.Object(SnapshotComponentStateObject(value.AsObject())),
            _ => value
        };
    }

    private static ObjectInstance SnapshotComponentStateObject(ObjectInstance source)
    {
        ObjectInstance clone = source switch
        {
            JsonObject => new JsonObject(),
            DictionaryInstance => new DictionaryInstance(),
            _ => new ObjectInstance(source.Class)
        };

        foreach (var key in source.GetAllKeys())
        {
            try
            {
                clone.Set(key, SnapshotComponentStateValue(source.Get(key, null)));
            }
            catch
            {
                // Skip members that cannot be materialized as persistent state.
            }
        }

        NormalizeBoundedStateObject(clone);
        return clone;
    }

    private static void NormalizeBoundedStateObject(ObjectInstance stateObject)
    {
        var hasCount = HasStateObjectKey(stateObject, "count");
        var hasHead = HasStateObjectKey(stateObject, "head");
        var hasMaxItems = HasStateObjectKey(stateObject, "maxItems");
        var hasItems = HasStateObjectKey(stateObject, "items");
        if (!hasItems || (!hasCount && !hasHead && !hasMaxItems))
        {
            return;
        }

        var countValue = GetStateObjectValue(stateObject, "count");
        var headValue = GetStateObjectValue(stateObject, "head");
        var maxItemsValue = GetStateObjectValue(stateObject, "maxItems");
        var itemsValue = GetStateObjectValue(stateObject, "items");
        var items = itemsValue.Type == ValueType.Array ? itemsValue.AsArray() : new List<RuntimeValue>();
        if (hasItems && itemsValue.Type != ValueType.Array)
        {
            stateObject.Set("items", RuntimeValue.Array(items));
        }

        var itemsLength = items.Count;

        if (countValue.Type == ValueType.Integer)
        {
            var normalizedCount = Math.Clamp(countValue.AsInteger(), 0, itemsLength);
            if (normalizedCount != countValue.AsInteger())
            {
                stateObject.Set("count", RuntimeValue.Integer(normalizedCount));
            }
            countValue = RuntimeValue.Integer(normalizedCount);
        }

        var capacity = itemsLength;
        if (maxItemsValue.Type == ValueType.Integer)
        {
            var requestedCapacity = Math.Max(0, maxItemsValue.AsInteger());
            if (itemsLength > 0 && requestedCapacity > itemsLength)
            {
                requestedCapacity = itemsLength;
            }

            if (requestedCapacity != maxItemsValue.AsInteger())
            {
                stateObject.Set("maxItems", RuntimeValue.Integer(requestedCapacity));
            }

            capacity = requestedCapacity > 0 ? requestedCapacity : itemsLength;
        }

        if (headValue.Type == ValueType.Integer)
        {
            var moduloBase = capacity > 0 ? capacity : itemsLength;
            var normalizedHead = headValue.AsInteger();
            if (moduloBase <= 0)
            {
                normalizedHead = 0;
            }
            else
            {
                normalizedHead %= moduloBase;
                if (normalizedHead < 0)
                {
                    normalizedHead += moduloBase;
                }

                if (countValue.Type == ValueType.Integer && countValue.AsInteger() < moduloBase)
                {
                    normalizedHead = 0;
                }
            }

            if (normalizedHead != headValue.AsInteger())
            {
                stateObject.Set("head", RuntimeValue.Integer(normalizedHead));
            }
        }
    }

    private static RuntimeValue GetStateObjectValue(ObjectInstance stateObject, string key)
    {
        if (stateObject is JsonObject jsonObject)
        {
            return jsonObject.Get(key, null);
        }

        try
        {
            return stateObject.Get(key, null);
        }
        catch
        {
            return RuntimeValue.Null();
        }
    }

    private static bool HasStateObjectKey(ObjectInstance stateObject, string key)
    {
        if (stateObject is JsonObject jsonObject)
        {
            return jsonObject.GetProperties().ContainsKey(key);
        }

        if (stateObject is DictionaryInstance dictionaryInstance)
        {
            return dictionaryInstance.Entries.ContainsKey(key);
        }

        return stateObject.GetAllKeys().Contains(key);
    }

    public static void ClearComponentState(string componentId)
    {
        lock (_componentStateLock)
        {
            CleanupExpiredComponentStateLocked();
            _componentStateStore.Remove(componentId);
        }
    }

    public static void ClearAllComponentState()
    {
        lock (_componentStateLock)
        {
            _componentStateStore.Clear();
        }
    }

    public static void ConfigureComponentStatePolicy(int maxComponents, int maxKeysPerComponent, int ttlMilliseconds)
    {
        if (maxComponents <= 0)
            throw new Exception("maxComponents must be > 0");
        if (maxKeysPerComponent <= 0)
            throw new Exception("maxKeysPerComponent must be > 0");
        if (ttlMilliseconds <= 0)
            throw new Exception("ttlMilliseconds must be > 0");

        lock (_componentStateLock)
        {
            _componentStateMaxComponents = maxComponents;
            _componentStateMaxKeysPerComponent = maxKeysPerComponent;
            _componentStateTtl = TimeSpan.FromMilliseconds(ttlMilliseconds);
            CleanupExpiredComponentStateLocked();
            // Shrinking maxComponents must not throw when pinned entries exceed the new cap.
            EnsureComponentCapacityLocked(throwIfCannotEvict: false);
        }
    }

    private static void CleanupExpiredComponentStateLocked()
    {
        if (_componentStateStore.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var expired = _componentStateStore
            .Where(kvp => !kvp.Value.Pinned && (now - kvp.Value.LastAccessUtc) > _componentStateTtl)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in expired)
        {
            _componentStateStore.Remove(key);
        }
    }

    private static void EnsureComponentCapacityLocked(bool throwIfCannotEvict)
    {
        while (_componentStateStore.Count >= _componentStateMaxComponents)
        {
            var oldest = _componentStateStore
                .Where(kvp => !kvp.Value.Pinned)
                .OrderBy(kvp => kvp.Value.LastAccessUtc)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(oldest.Key))
            {
                if (throwIfCannotEvict)
                {
                    throw new Exception(
                        "Component state store is full of pinned entries. " +
                        "Call componentStateConfigure() to raise maxComponents, or componentStateUnpin()/ui.unpinState().");
                }

                break;
            }
            _componentStateStore.Remove(oldest.Key);
        }
    }
}
