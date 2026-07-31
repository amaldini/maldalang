namespace MaldaLang.Runtime.UI;

using System.Net;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

public sealed class UiTemplateRenderOptions
{
    public bool EscapeByDefault { get; init; } = true;
    public bool CompatRaw { get; init; }
    public HashSet<string>? RawKeys { get; init; }
    public Func<RuntimeValue, string>? StringifyValue { get; init; }
}

public static class UiTemplateEngine
{
    public static string Render(string template, JsonObject? model, UiTemplateRenderOptions? options = null)
    {
        options ??= new UiTemplateRenderOptions();
        var parser = new Parser(template ?? string.Empty);
        var nodes = parser.Parse();
        var context = new EvalContext(RuntimeValue.Object(model ?? new JsonObject()), null, null);
        var sb = new System.Text.StringBuilder(template?.Length ?? 0);
        foreach (var node in nodes)
        {
            node.Render(sb, context, options);
        }

        return sb.ToString();
    }

    private sealed class EvalContext
    {
        public RuntimeValue Current { get; }
        public Dictionary<string, RuntimeValue>? Locals { get; }
        public EvalContext? Parent { get; }

        public EvalContext(RuntimeValue current, Dictionary<string, RuntimeValue>? locals, EvalContext? parent)
        {
            Current = current;
            Locals = locals;
            Parent = parent;
        }
    }

    private interface ITemplateNode
    {
        void Render(System.Text.StringBuilder sb, EvalContext context, UiTemplateRenderOptions options);
    }

    private sealed class TextNode : ITemplateNode
    {
        private readonly string _value;
        public TextNode(string value) => _value = value;

        public void Render(System.Text.StringBuilder sb, EvalContext context, UiTemplateRenderOptions options)
        {
            sb.Append(_value);
        }
    }

    private sealed class ValueNode : ITemplateNode
    {
        private readonly string _expression;
        private readonly bool _isRaw;

        public ValueNode(string expression, bool isRaw)
        {
            _expression = expression;
            _isRaw = isRaw;
        }

        public void Render(System.Text.StringBuilder sb, EvalContext context, UiTemplateRenderOptions options)
        {
            var value = ResolveExpression(_expression, context);
            if (value.Type == ValueType.Null)
            {
                return;
            }

            var text = StringifyValue(value, options);
            var renderRaw = _isRaw || options.CompatRaw || (options.RawKeys != null && options.RawKeys.Contains(_expression));
            if (!renderRaw && options.EscapeByDefault)
            {
                text = EscapeHtml(text);
            }

            sb.Append(text);
        }
    }

    private sealed class IfNode : ITemplateNode
    {
        private readonly string _conditionExpression;
        private readonly List<ITemplateNode> _children;

        public IfNode(string conditionExpression, List<ITemplateNode> children)
        {
            _conditionExpression = conditionExpression;
            _children = children;
        }

        public void Render(System.Text.StringBuilder sb, EvalContext context, UiTemplateRenderOptions options)
        {
            var value = ResolveExpression(_conditionExpression, context);
            if (!IsTruthy(value))
            {
                return;
            }

            foreach (var child in _children)
            {
                child.Render(sb, context, options);
            }
        }
    }

    private sealed class EachNode : ITemplateNode
    {
        private readonly string _collectionExpression;
        private readonly string _alias;
        private readonly List<ITemplateNode> _children;

        public EachNode(string collectionExpression, string alias, List<ITemplateNode> children)
        {
            _collectionExpression = collectionExpression;
            _alias = alias;
            _children = children;
        }

        public void Render(System.Text.StringBuilder sb, EvalContext context, UiTemplateRenderOptions options)
        {
            var source = ResolveExpression(_collectionExpression, context);
            if (source.Type != ValueType.Array)
            {
                return;
            }

            var index = 0;
            foreach (var item in source.AsArray())
            {
                var locals = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal)
                {
                    [_alias] = item,
                    ["index"] = RuntimeValue.Integer(index)
                };
                var childContext = new EvalContext(item, locals, context);
                foreach (var child in _children)
                {
                    child.Render(sb, childContext, options);
                }

                index++;
            }
        }
    }

    private sealed class Parser
    {
        private readonly string _template;
        private int _position;

        public Parser(string template)
        {
            _template = template;
        }

        public List<ITemplateNode> Parse()
        {
            return ParseNodes(null);
        }

        private List<ITemplateNode> ParseNodes(string? expectedClosingTag)
        {
            var nodes = new List<ITemplateNode>();
            while (_position < _template.Length)
            {
                if (Match("{{{"))
                {
                    var expression = ReadUntil("}}}");
                    nodes.Add(new ValueNode(expression.Trim(), true));
                    continue;
                }

                if (Match("{{"))
                {
                    var token = ReadUntil("}}").Trim();
                    if (token.StartsWith("/", StringComparison.Ordinal))
                    {
                        var closing = token[1..].Trim();
                        if (expectedClosingTag == null)
                        {
                            throw new Exception($"Unexpected template closing tag '{{{{/{closing}}}}}'.");
                        }

                        if (!string.Equals(closing, expectedClosingTag, StringComparison.Ordinal))
                        {
                            throw new Exception($"Template closing tag mismatch. Expected '{{{{/{expectedClosingTag}}}}}' but found '{{{{/{closing}}}}}'.");
                        }

                        return nodes;
                    }

                    if (token.StartsWith("#if ", StringComparison.Ordinal))
                    {
                        var condition = token[4..].Trim();
                        if (condition.Length == 0)
                        {
                            throw new Exception("Template if block requires a condition expression.");
                        }

                        var children = ParseNodes("if");
                        nodes.Add(new IfNode(condition, children));
                        continue;
                    }

                    if (token.StartsWith("#each ", StringComparison.Ordinal))
                    {
                        var payload = token[6..].Trim();
                        if (payload.Length == 0)
                        {
                            throw new Exception("Template each block requires an expression.");
                        }

                        var alias = "item";
                        var collection = payload;
                        var aliasMarker = payload.IndexOf(" as ", StringComparison.Ordinal);
                        if (aliasMarker >= 0)
                        {
                            collection = payload[..aliasMarker].Trim();
                            alias = payload[(aliasMarker + 4)..].Trim();
                        }

                        if (collection.Length == 0 || alias.Length == 0)
                        {
                            throw new Exception("Template each block expects '{{#each collection as alias}}'.");
                        }

                        var children = ParseNodes("each");
                        nodes.Add(new EachNode(collection, alias, children));
                        continue;
                    }

                    nodes.Add(new ValueNode(token, false));
                    continue;
                }

                var nextTag = _template.IndexOf("{{", _position, StringComparison.Ordinal);
                if (nextTag < 0)
                {
                    nodes.Add(new TextNode(_template[_position..]));
                    _position = _template.Length;
                }
                else
                {
                    nodes.Add(new TextNode(_template.Substring(_position, nextTag - _position)));
                    _position = nextTag;
                }
            }

            if (expectedClosingTag != null)
            {
                throw new Exception($"Missing template closing tag '{{{{/{expectedClosingTag}}}}}'.");
            }

            return nodes;
        }

        private bool Match(string marker)
        {
            if (_position + marker.Length > _template.Length)
            {
                return false;
            }

            if (!_template.AsSpan(_position, marker.Length).SequenceEqual(marker))
            {
                return false;
            }

            _position += marker.Length;
            return true;
        }

        private string ReadUntil(string terminator)
        {
            var end = _template.IndexOf(terminator, _position, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new Exception($"Unterminated template token. Missing '{terminator}'.");
            }

            var result = _template.Substring(_position, end - _position);
            _position = end + terminator.Length;
            return result;
        }
    }

    private static string EscapeHtml(string input)
    {
        return WebUtility.HtmlEncode(input);
    }

    private static string StringifyValue(RuntimeValue value, UiTemplateRenderOptions options)
    {
        if (value.Type == ValueType.String)
        {
            return value.AsString();
        }

        if (options.StringifyValue != null)
        {
            return options.StringifyValue(value);
        }

        return value.ToString();
    }

    private static RuntimeValue ResolveExpression(string expression, EvalContext context)
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            return RuntimeValue.Null();
        }

        var root = ResolveRoot(trimmed, context, out var consumedWholeExpression);
        if (consumedWholeExpression)
        {
            return root;
        }

        if (root.Type == ValueType.Null)
        {
            return RuntimeValue.Null();
        }

        var segments = trimmed.Split('.');
        var current = root;
        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i].Trim();
            if (segment.Length == 0)
            {
                return RuntimeValue.Null();
            }

            current = ResolveMember(current, segment);
            if (current.Type == ValueType.Null)
            {
                return RuntimeValue.Null();
            }
        }

        return current;
    }

    private static RuntimeValue ResolveRoot(string expression, EvalContext context, out bool consumedWholeExpression)
    {
        consumedWholeExpression = false;

        var contextIter = context;
        while (contextIter != null)
        {
            if (contextIter.Locals != null && contextIter.Locals.TryGetValue(expression, out var localFull))
            {
                consumedWholeExpression = true;
                return localFull;
            }

            var directLocal = FindInLocals(contextIter, expression.Split('.')[0]);
            if (directLocal.Type != ValueType.Null)
            {
                return directLocal;
            }

            var currentObject = ResolveMember(contextIter.Current, expression);
            if (currentObject.Type != ValueType.Null)
            {
                consumedWholeExpression = true;
                return currentObject;
            }

            contextIter = contextIter.Parent;
        }

        return RuntimeValue.Null();
    }

    private static RuntimeValue FindInLocals(EvalContext context, string key)
    {
        var current = context;
        while (current != null)
        {
            if (current.Locals != null && current.Locals.TryGetValue(key, out var value))
            {
                return value;
            }

            current = current.Parent;
        }

        return RuntimeValue.Null();
    }

    private static RuntimeValue ResolveMember(RuntimeValue value, string member)
    {
        if (value.Type == ValueType.Object)
        {
            var obj = value.AsObject();
            if (obj is JsonObject jsonObject)
            {
                return jsonObject.Get(member, null) ?? RuntimeValue.Null();
            }

            if (obj is DictionaryInstance dict)
            {
                try
                {
                    return dict.Get(member, null) ?? RuntimeValue.Null();
                }
                catch
                {
                    return RuntimeValue.Null();
                }
            }

            try
            {
                return obj.Get(member, null) ?? RuntimeValue.Null();
            }
            catch
            {
                return RuntimeValue.Null();
            }
        }

        if (value.Type == ValueType.Array && int.TryParse(member, out var index))
        {
            var array = value.AsArray();
            return index >= 0 && index < array.Count ? array[index] : RuntimeValue.Null();
        }

        return RuntimeValue.Null();
    }

    private static bool IsTruthy(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Null => false,
            ValueType.Boolean => value.AsBoolean(),
            ValueType.Integer => value.AsInteger() != 0,
            ValueType.Float => Math.Abs(value.AsFloat()) > double.Epsilon,
            ValueType.String => !string.IsNullOrEmpty(value.AsString()),
            ValueType.Array => value.AsArray().Count > 0,
            ValueType.Object => true,
            _ => true
        };
    }
}
