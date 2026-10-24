// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MaldaLang.UIHost;

/// <summary>
/// The <c>treeHash</c> of UI envelopes: 32-bit FNV-1a over the UTF-8 bytes of the tree's
/// canonical JSON, as 8 lowercase hex digits. Canonical JSON is what <c>JSON.stringify</c> writes
/// with object keys sorted by UTF-16 code unit, so <c>MaldaUiCore.treeHash</c> in
/// <c>wwwroot/malda-ui-client.js</c> computes the same hash from the tree it holds: numbers in
/// JavaScript form (<c>1</c>, <c>0.1</c>, <c>1e+21</c>; NaN and infinities as <c>null</c>), strings
/// escaped only where <c>JSON.stringify</c> escapes them. <c>UiSession</c> feeds a
/// <see cref="Writer"/> from its <c>UiNode</c> tree, the hub hashes its JSON tree with
/// <see cref="Of(JsonNode?)"/>; <c>UiPatchFuzzTests</c> checks all three agree.
/// </summary>
public static class UiTreeHash
{
    public static string Of(JsonNode? tree)
    {
        var writer = new Writer();
        Write(writer, tree);
        return writer.Hash;
    }

    private static void Write(Writer writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.Null();
                break;
            case JsonObject obj:
                writer.BeginObject();
                foreach (var (name, value) in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    writer.Name(name);
                    Write(writer, value);
                }
                writer.EndObject();
                break;
            case JsonArray array:
                writer.BeginArray();
                foreach (var item in array)
                    Write(writer, item);
                writer.EndArray();
                break;
            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String:
                        writer.String(value.GetValue<string>());
                        break;
                    case JsonValueKind.Number:
                        writer.Number(value.GetValue<double>());
                        break;
                    case JsonValueKind.True:
                        writer.Boolean(true);
                        break;
                    case JsonValueKind.False:
                        writer.Boolean(false);
                        break;
                    default:
                        writer.Null();
                        break;
                }
                break;
        }
    }

    /// <summary>
    /// Streams canonical JSON into the hash. The caller writes object members in sorted order
    /// (<see cref="StringComparer.Ordinal"/>); the writer places the commas.
    /// </summary>
    public sealed class Writer
    {
        private const uint OffsetBasis = 0x811c9dc5;
        private const uint Prime = 0x01000193;

        private readonly Stack<bool> _firstInContainer = new();
        private readonly byte[] _utf8 = new byte[4];
        private uint _hash = OffsetBasis;
        private bool _afterName;

        public string Hash => _hash.ToString("x8", CultureInfo.InvariantCulture);

        public void BeginObject() { Separate(); Ascii("{"); _firstInContainer.Push(true); }
        public void EndObject() { _firstInContainer.Pop(); Ascii("}"); }
        public void BeginArray() { Separate(); Ascii("["); _firstInContainer.Push(true); }
        public void EndArray() { _firstInContainer.Pop(); Ascii("]"); }

        public void Name(string name)
        {
            Separate();
            Quoted(name);
            Ascii(":");
            _afterName = true;
        }

        public void Null() { Separate(); Ascii("null"); }
        public void Boolean(bool value) { Separate(); Ascii(value ? "true" : "false"); }
        public void Number(double value) { Separate(); Ascii(JavaScriptNumber(value)); }
        public void String(string value) { Separate(); Quoted(value); }

        private void Separate()
        {
            if (_afterName)
            {
                _afterName = false;
                return;
            }

            if (_firstInContainer.Count == 0)
                return;
            if (_firstInContainer.Peek())
            {
                _firstInContainer.Pop();
                _firstInContainer.Push(false);
            }
            else
            {
                Ascii(",");
            }
        }

        private void Quoted(string text)
        {
            Ascii("\"");
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                switch (c)
                {
                    case '"': Ascii("\\\""); continue;
                    case '\\': Ascii("\\\\"); continue;
                    case '\b': Ascii("\\b"); continue;
                    case '\f': Ascii("\\f"); continue;
                    case '\n': Ascii("\\n"); continue;
                    case '\r': Ascii("\\r"); continue;
                    case '\t': Ascii("\\t"); continue;
                }

                if (c < 0x20)
                {
                    Ascii("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    Bytes(text.AsSpan(i, 2));
                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    // The envelope serializer (System.Text.Json) sends a lone surrogate as U+FFFD,
                    // so that is what the client holds and hashes.
                    Bytes("�");
                }
                else
                {
                    Bytes(text.AsSpan(i, 1));
                }
            }
            Ascii("\"");
        }

        private void Bytes(ReadOnlySpan<char> chars)
        {
            var count = Encoding.UTF8.GetBytes(chars, _utf8);
            for (var i = 0; i < count; i++)
                Add(_utf8[i]);
        }

        private void Ascii(string text)
        {
            foreach (var c in text)
                Add((byte)c);
        }

        private void Add(byte b) => _hash = (_hash ^ b) * Prime;
    }

    /// <summary>
    /// <c>Number.prototype.toString()</c>: the shortest round-trip digits (which .NET's "R" also
    /// gives), laid out by the ECMAScript rules — plain up to 21 integer digits and down to 1e-6,
    /// exponent form outside.
    /// </summary>
    public static string JavaScriptNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "null";
        if (value == 0)
            return "0";

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var negative = text[0] == '-';
        if (negative)
            text = text[1..];

        var exponent = 0;
        var e = text.IndexOfAny(new[] { 'E', 'e' });
        if (e >= 0)
        {
            exponent = int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..e];
        }

        var point = text.IndexOf('.');
        var integerPart = point >= 0 ? text[..point] : text;
        var digits = integerPart + (point >= 0 ? text[(point + 1)..] : "");
        // value = 0.digits × 10^n
        var n = integerPart.Length + exponent;
        var leadingZeros = digits.Length - digits.TrimStart('0').Length;
        digits = digits[leadingZeros..].TrimEnd('0');
        n -= leadingZeros;
        var k = digits.Length;

        string result;
        if (k <= n && n <= 21)
            result = digits + new string('0', n - k);
        else if (0 < n && n <= 21)
            result = digits[..n] + "." + digits[n..];
        else if (-6 < n && n <= 0)
            result = "0." + new string('0', -n) + digits;
        else
        {
            var exp = n - 1;
            var sign = exp < 0 ? "-" : "+";
            var mantissa = k == 1 ? digits : digits[..1] + "." + digits[1..];
            result = mantissa + "e" + sign + Math.Abs(exp).ToString(CultureInfo.InvariantCulture);
        }

        return negative ? "-" + result : result;
    }
}
