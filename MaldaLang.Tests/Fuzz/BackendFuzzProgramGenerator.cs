// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;

namespace MaldaLang.Tests.Fuzz;

/// <summary>
/// Seeded generator of small, always-terminating, error-free <c>.malda</c> programs for
/// differential testing of the interpreter, C# transpile and JavaScript transpile.
/// Values stay small (every stored int is reduced <c>% 1000</c>) so no backend hits int
/// overflow, divisors are non-zero literals and array indexes are in range by construction.
/// Doubles are stored, passed, returned and mixed with ints so the typed int/double fast
/// paths of the transpilers are exercised; they avoid <c>math.pow</c> and trigonometry,
/// whose last bit may differ between .NET and V8, and never reach NaN or infinity.
/// The same seed always yields the same program.
/// </summary>
internal sealed class BackendFuzzProgramGenerator
{
    private readonly Random _random;
    private readonly List<string> _lines = new();
    private int _indent;
    private int _nameCounter;

    private BackendFuzzProgramGenerator(int seed)
    {
        _random = new Random(seed);
    }

    /// <summary>
    /// A program of <paramref name="caseCount"/> cases. Each case is a function whose output is
    /// preceded by a <c>#n</c> header line, so a mismatch can be traced to one case.
    /// </summary>
    public static string Generate(int seed, int caseCount) =>
        new BackendFuzzProgramGenerator(seed).Program(caseCount);

    private sealed class Scope
    {
        public List<string> Ints { get; init; } = new();
        public List<string> Doubles { get; init; } = new();
        public List<string> Strings { get; init; } = new();
        public List<(string Name, int Length)> Arrays { get; init; } = new();
        public List<string> Lambdas { get; init; } = new();
        public List<(string Name, int Arity)> Functions { get; init; } = new();
        /// <summary>Helpers that take and return doubles; callers pass ints or doubles.</summary>
        public List<(string Name, int Arity)> DoubleFunctions { get; init; } = new();
        /// <summary>Loop variables: readable, never assigned by generated code.</summary>
        public HashSet<string> ReadOnly { get; init; } = new(StringComparer.Ordinal);

        public Scope Child() => new()
        {
            Ints = new List<string>(Ints),
            Doubles = new List<string>(Doubles),
            Strings = new List<string>(Strings),
            Arrays = new List<(string, int)>(Arrays),
            Lambdas = new List<string>(Lambdas),
            Functions = new List<(string, int)>(Functions),
            DoubleFunctions = new List<(string, int)>(DoubleFunctions),
            ReadOnly = new HashSet<string>(ReadOnly, StringComparer.Ordinal)
        };
    }

    private string Fresh(string prefix) => prefix + (++_nameCounter);

    private void Emit(string line) => _lines.Add(new string(' ', _indent * 4) + line);

    private double Roll() => _random.NextDouble();

    private int Between(int min, int maxInclusive) => _random.Next(min, maxInclusive + 1);

    private T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

    private static string Clamp(string expression) => $"({expression}) % 1000";

    // ---- expressions ----------------------------------------------------------------

    private string IntLeaf(Scope scope)
    {
        var roll = Roll();
        if (scope.Ints.Count > 0 && roll < 0.55)
            return Pick(scope.Ints);
        if (scope.Arrays.Count > 0 && roll < 0.65)
        {
            var (name, length) = Pick(scope.Arrays);
            return $"{name}[{_random.Next(length)}]";
        }
        if (scope.Arrays.Count > 0 && roll < 0.7)
            return $"{Pick(scope.Arrays).Name}.length";
        if (scope.Strings.Count > 0 && roll < 0.75)
            return $"str.length({Pick(scope.Strings)})";
        // math.floor / math.round return floats; int() keeps int-typed values ints.
        if (scope.Doubles.Count > 0 && roll < 0.8)
            return $"int({Pick(new[] { "", "math.floor", "math.round" })}({Pick(scope.Doubles)}))";
        return Between(-9, 20).ToString();
    }

    private string DoubleLeaf(Scope scope)
    {
        var roll = Roll();
        if (scope.Doubles.Count > 0 && roll < 0.5)
            return Pick(scope.Doubles);
        if (roll < 0.75)
            return Pick(new[] { "0.5", "1.25", "0.1", "2.0", "-3.75", "10.0", "0.2", "-0.5" });
        if (roll < 0.85)
            return $"float({IntLeaf(scope)})";
        return IntLeaf(scope);
    }

    /// <summary>A finite double of modest magnitude, often mixing int and double operands.</summary>
    private string DoubleExpr(Scope scope, int depth = 2)
    {
        if (depth <= 0 || Roll() < 0.3)
            return DoubleLeaf(scope);

        var roll = Roll();
        if (roll < 0.45)
        {
            var op = Pick(new[] { "+", "-", "+", "-", "*" });
            return op == "*"
                ? $"({DoubleLeaf(scope)} * {DoubleLeaf(scope)})"
                : $"({DoubleExpr(scope, depth - 1)} {op} {DoubleExpr(scope, depth - 1)})";
        }
        if (roll < 0.55)
            return $"({DoubleExpr(scope, depth - 1)} / {Pick(new[] { "2", "3", "4.0", "0.5", "8", "10" })})";
        if (roll < 0.6)
            return $"({IntExpr(scope, depth - 1)} / {Between(2, 7)})";
        if (roll < 0.65)
            return $"({DoubleExpr(scope, depth - 1)} % {Pick(new[] { "3", "2.5", "7" })})";
        if (roll < 0.75 && scope.DoubleFunctions.Count > 0)
        {
            var (name, arity) = Pick(scope.DoubleFunctions);
            // Mix int and double arguments so one helper sees both argument types.
            var args = Enumerable.Range(0, arity)
                .Select(_ => Roll() < 0.5 ? DoubleExpr(scope, depth - 1) : IntExpr(scope, depth - 1));
            return $"{name}({string.Join(", ", args)})";
        }
        if (roll < 0.82)
            return $"{Pick(new[] { "math.floor", "math.ceil", "math.round", "math.abs" })}({DoubleExpr(scope, depth - 1)})";
        if (roll < 0.86)
            return $"math.sqrt(math.abs({DoubleExpr(scope, depth - 1)}))";
        if (roll < 0.93)
            return $"{Pick(new[] { "math.min", "math.max" })}({DoubleExpr(scope, depth - 1)}, {IntExpr(scope, depth - 1)})";
        if (roll < 0.97)
            return $"({BoolExpr(scope, 1)} ? {DoubleLeaf(scope)} : {IntLeaf(scope)})";
        return $"(-({DoubleLeaf(scope)}))";
    }

    private string IntExpr(Scope scope, int depth = 2)
    {
        if (depth <= 0 || Roll() < 0.3)
            return IntLeaf(scope);

        var roll = Roll();
        if (roll < 0.6)
        {
            var op = Pick(new[] { "+", "-", "+", "-", "*" });
            // Products only of leaves, so magnitudes stay far from int overflow.
            return op == "*"
                ? $"({IntExpr(scope, 0)} * {IntExpr(scope, 0)})"
                : $"({IntExpr(scope, depth - 1)} {op} {IntExpr(scope, depth - 1)})";
        }
        if (roll < 0.7)
            return $"({IntExpr(scope, depth - 1)} % {Between(1, 9)})";
        if (roll < 0.78 && scope.Functions.Count > 0)
        {
            var (name, arity) = Pick(scope.Functions);
            var args = Enumerable.Range(0, arity).Select(_ => IntExpr(scope, depth - 1));
            return $"{name}({string.Join(", ", args)})";
        }
        if (roll < 0.84 && scope.Lambdas.Count > 0)
            return $"{Pick(scope.Lambdas)}({IntExpr(scope, depth - 1)})";
        if (roll < 0.9)
            return $"math.abs({IntExpr(scope, depth - 1)})";
        if (roll < 0.95)
            return $"math.max({IntExpr(scope, depth - 1)}, {IntExpr(scope, depth - 1)})";
        return $"(-({IntLeaf(scope)}))";
    }

    private string BoolExpr(Scope scope, int depth = 2)
    {
        var roll = Roll();
        if (depth <= 0 || roll < 0.6)
        {
            // Doubles are only ordered, never tested for equality: 1 == 1.0 is false on the
            // interpreter and C# but true in JavaScript (a documented divergence).
            if (scope.Doubles.Count > 0 && Roll() < 0.25)
                return $"{DoubleExpr(scope, 1)} {Pick(new[] { "<", "<=", ">", ">=" })} {(Roll() < 0.5 ? DoubleExpr(scope, 1) : IntExpr(scope, 1))}";
            var op = Pick(new[] { "<", "<=", ">", ">=", "==", "!=" });
            return $"{IntExpr(scope, 1)} {op} {IntExpr(scope, 1)}";
        }
        if (roll < 0.75)
            return $"({BoolExpr(scope, depth - 1)}) && ({BoolExpr(scope, depth - 1)})";
        if (roll < 0.9)
            return $"({BoolExpr(scope, depth - 1)}) || ({BoolExpr(scope, depth - 1)})";
        return $"not ({BoolExpr(scope, depth - 1)})";
    }

    private string StrExpr(Scope scope, int depth = 2)
    {
        var roll = Roll();
        if (depth <= 0 || roll < 0.3)
        {
            if (scope.Strings.Count > 0 && Roll() < 0.6)
                return Pick(scope.Strings);
            return "\"" + Pick(new[] { "a", "bc", "Hi", "x y", "", "Zed" }) + "\"";
        }
        if (roll < 0.5)
            return $"{StrExpr(scope, depth - 1)} + {StrExpr(scope, depth - 1)}";
        if (roll < 0.6)
            return $"string({IntExpr(scope, 1)})";
        if (roll < 0.7)
            return $"str.upper({StrExpr(scope, depth - 1)})";
        if (roll < 0.8)
            return $"{StrExpr(scope, depth - 1)} + {IntExpr(scope, 1)}";
        if (roll < 0.85 && scope.Ints.Count > 0)
            return $"$\"<{{{Pick(scope.Ints)}}}|{{{IntExpr(scope, 1)}}}>\"";
        if (roll < 0.9 && scope.Doubles.Count > 0)
            return Roll() < 0.5
                ? $"$\"<{{{Pick(scope.Doubles)}}}|{{{DoubleExpr(scope, 1)}}}>\""
                : $"{StrExpr(scope, depth - 1)} + {DoubleExpr(scope, 1)}";
        return $"str.lower({StrExpr(scope, depth - 1)})";
    }

    // ---- statements -----------------------------------------------------------------

    private void Statement(Scope scope, int depth)
    {
        if (Roll() < 0.15)
        {
            StdLibStatement(scope);
            return;
        }
        if (Roll() < 0.25)
        {
            ExtraStatement(scope, depth);
            return;
        }
        if (Roll() < 0.2)
        {
            DoubleStatement(scope, depth);
            return;
        }
        if (Roll() < 0.12)
        {
            FunctionValueStatement(scope);
            return;
        }
        if (Roll() < 0.1)
        {
            ErrorPathStatement(scope);
            return;
        }

        var roll = Roll();
        if (roll < 0.18 || scope.Ints.Count == 0)
        {
            var name = Fresh("v");
            Emit($"var {name} = {Clamp(IntExpr(scope))};");
            scope.Ints.Add(name);
        }
        else if (roll < 0.3)
        {
            var name = Pick(scope.Ints);
            if (scope.ReadOnly.Contains(name))
                return;
            var op = Pick(new[] { "=", "+=", "-=" });
            Emit($"{name} {op} {Clamp(IntExpr(scope))};");
            if (op != "=")
                Emit($"{name} = {name} % 1000;");
        }
        else if (roll < 0.38)
        {
            var name = Fresh("s");
            Emit($"var {name} = {StrExpr(scope)};");
            scope.Strings.Add(name);
        }
        else if (roll < 0.46)
        {
            var name = Fresh("arr");
            var length = Between(1, 4);
            var items = Enumerable.Range(0, length).Select(_ => Clamp(IntExpr(scope, 1)));
            Emit($"var {name} = [{string.Join(", ", items)}];");
            scope.Arrays.Add((name, length));
        }
        else if (roll < 0.5 && scope.Arrays.Count > 0)
        {
            var (name, length) = Pick(scope.Arrays);
            if (Roll() < 0.5)
                Emit($"{name}.append({Clamp(IntExpr(scope))});");
            else
                Emit($"{name}[{_random.Next(length)}] = {Clamp(IntExpr(scope))};");
        }
        else if (roll < 0.6 && depth > 0)
        {
            Emit($"if ({BoolExpr(scope)}) {{");
            Block(scope.Child(), depth - 1);
            if (Roll() < 0.5)
            {
                Emit("} else {");
                Block(scope.Child(), depth - 1);
            }
            Emit("}");
        }
        else if (roll < 0.67 && depth > 0)
        {
            var index = Fresh("i");
            Emit($"for (var {index} = 0; {index} < {Between(0, 4)}; {index} = {index} + 1) {{");
            var inner = scope.Child();
            inner.Ints.Add(index);
            inner.ReadOnly.Add(index);
            Block(inner, depth - 1);
            Emit("}");
        }
        else if (roll < 0.72 && depth > 0)
        {
            var counter = Fresh("w");
            Emit($"var {counter} = 0;");
            Emit($"while ({counter} < {Between(1, 4)}) {{");
            var inner = scope.Child();
            inner.Ints.Add(counter);
            inner.ReadOnly.Add(counter);
            _indent++;
            Emit($"{counter} = {counter} + 1;");
            if (Roll() < 0.3)
                Emit($"if ({BoolExpr(inner, 1)}) {{ continue; }}");
            _indent--;
            Block(inner, depth - 1);
            Emit("}");
        }
        else if (roll < 0.76 && depth > 0 && scope.Arrays.Count > 0)
        {
            var (array, _) = Pick(scope.Arrays);
            var item = Fresh("e");
            Emit($"foreach (var {item} in {array}) {{");
            var inner = scope.Child();
            inner.Ints.Add(item);
            inner.ReadOnly.Add(item);
            // Never mutate the array being iterated.
            inner.Arrays.RemoveAll(a => a.Name == array);
            Block(inner, depth - 1);
            Emit("}");
        }
        else if (roll < 0.8)
        {
            var name = Fresh("lam");
            Emit($"var {name} = (x) => {Clamp("x * 2 + " + IntLeaf(scope))};");
            scope.Lambdas.Add(name);
        }
        else if (roll < 0.86 && scope.Arrays.Count > 0)
        {
            var (array, _) = Pick(scope.Arrays);
            var method = Pick(new[] { "map", "filter" });
            var body = method == "map" ? $"x + {IntLeaf(scope)}" : $"x > {Between(-5, 10)}";
            Emit($"io.print({array}.{method}((x) => {body}));");
        }
        else
        {
            var kind = Roll();
            if (kind < 0.5)
                Emit($"io.print({IntExpr(scope)});");
            else if (kind < 0.7)
                Emit($"io.print({StrExpr(scope)});");
            else if (kind < 0.85)
                Emit($"io.print({BoolExpr(scope)});");
            else if (scope.Arrays.Count > 0)
                Emit($"io.print({Pick(scope.Arrays).Name});");
        }
    }

    /// <summary>Literal texts for the string built-ins: accents, punctuation, digits, quotes.</summary>
    private static readonly string[] StdLibTexts =
    {
        "Héllo, Wörld!", "  naïve  café ", "A-B_c.d", "the cat sat", "x y  z", "a1b22c3", "Zed",
        "q\\\"r <&> +'`", "", "start 12 and -3.75"
    };

    /// <summary>Lower-case ASCII words: sort() compares text with the culture rules of each host.</summary>
    private static readonly string[] SortWords = { "pear", "fig", "apple", "kiwi", "plum", "date" };

    private string StdText(Scope scope) =>
        Roll() < 0.5 ? "\"" + Pick(StdLibTexts) + "\"" : StrExpr(scope, 1);

    /// <summary>
    /// Pure stdlib calls (I7): math without transcendental functions (their last bit may differ
    /// between .NET and V8), str.* text helpers, encodings and hashes, conversions, collections,
    /// toCsv / toJSON / parseJSON and assert / expect. Arguments stay in the domain where the
    /// interpreter documents a result (no error paths, no culture-dependent text).
    /// </summary>
    private void StdLibStatement(Scope scope)
    {
        string Int() => IntExpr(scope, 1);
        string Dbl() => DoubleExpr(scope, 1);
        string Text() => StdText(scope);
        var array = scope.Arrays.Count > 0 ? Pick(scope.Arrays).Name : $"[{Int()}, {Int()}, {Int()}]";

        var roll = Roll();
        if (roll < 0.25)
        {
            Emit($"io.print({Pick(new[]
            {
                $"math.clamp({Dbl()}, {Int()}, {Int()})",
                $"math.hypot({Int()}, {Dbl()})",
                $"math.trunc({Dbl()})",
                $"math.sign({Dbl()})",
                $"math.round({Dbl()})",
                $"math.sum({array})",
                $"math.average({array})",
                $"math.max({array})",
                $"math.min({array})",
                $"math.argmax({array})",
                $"math.argmin({array})",
                $"math.dot({array}, {array})",
                $"math.zeros({Between(0, 3)})",
                $"math.transpose([[{Int()}, {Int()}], [{Int()}, {Int()}]])",
                $"math.matmul([[{Int()}, {Int()}], [{Int()}, {Int()}]], [[{Int()}], [{Int()}]])",
                $"math.degToRad({Dbl()})",
                $"math.radToDeg({Dbl()})",
                $"math.pow({Between(-6, 9)}, {Between(0, 3)})",
                $"math.rsqrt(math.abs({Dbl()}) + 1)",
                $"math.sum([{Dbl()}, {Int()}, {Dbl()}])"
            })});");
        }
        else if (roll < 0.55)
        {
            Emit($"io.print({Pick(new[]
            {
                $"str.normalizeText({Text()})",
                $"str.normalizeText({Text()}, {{ \"removePunctuation\": false, \"lowercase\": {(Roll() < 0.5 ? "true" : "false")} }})",
                $"str.tokenize({Text()})",
                $"toJSON(str.tokenOverlap({Text()}, {Text()}))",
                $"str.similarity({Text()}, {Text()})",
                $"str.similarity({Text()}, {Text()}, \"{Pick(new[] { "jaccard", "contains", "char-ngram", "exact" })}\")",
                $"str.extractNumbers({Text()} + {Int()} + \" \" + {Dbl()})",
                $"str.padStart({Text()}, {Between(0, 12)}, \"*\")",
                $"str.padEnd({Text()}, {Between(0, 12)}, \"-\")",
                $"str.split({Text()})",
                $"str.split({Text()}, \" \")",
                $"str.split({Text()} + {Int()}, \"[0-9]+\")",
                $"str.regexReplace({Text()}, \"[aeiou]\", \"<$0>\")",
                $"str.regexReplace({Text()} + {Int()}, \"([a-z])([0-9]+)\", \"$2$1\")",
                $"toJSON(str.regexFind({Text()} + {Int()}, \"([a-z])([0-9]+)?\"))",
                $"str.regexMatch({Text()}, \"^[A-Z]\")",
                $"str.trimText({Text()})",
                $"str.upper({Text()})",
                $"str.lower({Text()})",
                $"str.join([{Int()}, null, {Text()}, {Dbl()}, true], \"|\")",
                $"str.includes({array}, {Int()})"
            })});");
        }
        else if (roll < 0.7)
        {
            Emit($"io.print({Pick(new[]
            {
                $"str.base64Encode({Text()})",
                $"str.base64Decode(str.base64Encode({Text()}))",
                $"str.md5({Text()})",
                $"str.sha256({Text()})",
                $"urlEncode({Text()})",
                $"urlDecode(urlEncode({Text()}))",
                $"toIntOr({Pick(new[] { "\" 42 \"", "\"x\"", "\"3.7\"", "\"\"", "\"1e2\"", $"string({Dbl()})", $"string({Int()})" })}, {Int()})",
                $"toIntOrNull({Pick(new[] { "\"-8\"", "\"abc\"", "\" \"", $"string({Dbl()})" })})",
                $"formatNumber({Dbl()}, {Between(0, 3)})",
                $"formatNumber({Int()}, {Between(0, 2)})",
                $"isString({Text()}) + \" \" + isArray({array}) + \" \" + isObject({{ \"k\": {Int()} }}) + \" \" + isObject({array})"
            })});");
        }
        else if (roll < 0.88)
        {
            Emit($"io.print({Pick(new[]
            {
                $"reverse({array})",
                $"sort({array})",
                $"sort([{Dbl()}, {Int()}, {Dbl()}, {Int()}])",
                $"sort([\"{Pick(SortWords)}\", \"{Pick(SortWords)}\", \"{Pick(SortWords)}\"])",
                $"sort({array}, (a, b) => b - a)",
                $"includes({array}, {Int()})",
                $"range({Between(-3, 3)}, {Between(4, 12)}, {Between(1, 3)})",
                $"range({Between(5, 9)}, {Between(-3, 2)}, -{Between(1, 3)})",
                $"toCsv([{{ \"a\": {Int()}, \"b\": {Text()}, \"c\": {Dbl()} }}, {{ \"a\": {Int()}, \"b\": null }}])",
                $"toCsv({array})",
                $"toCsv([{{ \"a\": {Int()}, \"b\": {Text()} }}], [\"b\", \"a\"], {{ \"delimiter\": \";\", \"includeHeader\": {(Roll() < 0.5 ? "true" : "false")} }})"
            })});");
        }
        else if (roll < 0.96)
        {
            var dict = Fresh("jd");
            Emit($"var {dict} = {{ \"s\": {Text()}, \"n\": [{Int()}, {Dbl()}, null, true], \"d\": {{ \"k\": {Text()} }} }};");
            Emit($"io.print(toJSON({dict}));");
            Emit($"io.print(toJSON(parseJSON(toJSON({dict}))));");
            Emit($"io.print(parseJSON(toJSON({dict})).s);");
        }
        else
        {
            Emit($"try {{ {Pick(new[] { "assert", "expect" })}({BoolExpr(scope, 1)}, \"failed-{_nameCounter}\"); io.print(\"held\"); }} catch (e) {{ io.print(\"caught \" + e); }}");
        }
    }

    /// <summary>Floats, closures over mutable state, break, dicts, classes, variants, str.*.</summary>
    private void ExtraStatement(Scope scope, int depth)
    {
        string Int() => IntExpr(scope, 1);
        var roll = Roll();
        if (roll < 0.12)
        {
            Emit($"io.print(({Int()}) / {Pick(new[] { 2, 3, 4, 5, 8, 10 })});");
        }
        else if (roll < 0.2)
        {
            Emit($"io.print(float({Int()}) * {Pick(new[] { "0.5", "1.25", "0.1", "2.0" })} + {Int()});");
        }
        else if (roll < 0.3)
        {
            var counter = Fresh("cnt");
            var increment = Fresh("inc");
            Emit($"var {counter} = {Int()};");
            Emit($"var {increment} = (n) => {{ {counter} = ({counter} + n) % 1000; return {counter}; }};");
            Emit($"io.print({increment}({Int()}));");
            Emit($"io.print({increment}({Int()}) + {counter});");
            scope.Ints.Add(counter);
        }
        else if (roll < 0.38 && depth > 0)
        {
            var index = Fresh("i");
            Emit($"for (var {index} = 0; {index} < 6; {index} = {index} + 1) {{");
            _indent++;
            var inner = scope.Child();
            inner.Ints.Add(index);
            inner.ReadOnly.Add(index);
            Emit($"if ({index} >= {Between(0, 5)}) {{ break; }}");
            Emit($"io.print({IntExpr(inner, 1)});");
            _indent--;
            Emit("}");
        }
        else if (roll < 0.48)
        {
            var dict = Fresh("d");
            Emit($"var {dict} = {{ \"a\": {Int()}, \"b\": {StrExpr(scope, 1)} }};");
            Emit($"{dict}.a = ({dict}.a + {Int()}) % 1000;");
            Emit($"{dict}[\"c\"] = {Int()};");
            Emit($"io.print({dict}.a + {dict}[\"c\"]);");
            Emit($"io.print({dict}.b + {dict}.a);");
            Emit($"io.print([{dict}, null, \"s\", [{Int()}, [true]]]);");
            Emit($"io.print($\"{{{dict}}}|{{null}}|{{[1, [2]]}}|{{{dict}.a > 3}}\");");
        }
        else if (roll < 0.6)
        {
            var box = Fresh("box");
            Emit($"var {box} = new Box({Int()});");
            Emit($"io.print({box}.add({Int()}));");
            Emit($"io.print({box}.twice() + {box}.v);");
            Emit($"{box}.v = {Int()};");
            Emit($"io.print({box}.add(1));");
            Emit($"io.print({box});");
            Emit($"io.print(\"box:\" + {box} + {box}.v);");
        }
        else if (roll < 0.72)
        {
            var shape = Fresh("sh");
            var constructor = Pick(new[] { $"Circle({Int()})", $"Rect({Int()}, {Int()})", "Empty()" });
            Emit($"var {shape} = {constructor};");
            Emit($"io.print(area({shape}));");
            Emit($"io.print({shape});");
            Emit($"io.print([{shape}, {shape}] + \"\");");
        }
        else if (roll < 0.84)
        {
            var text = StrExpr(scope, 1);
            Emit($"io.print({Pick(new[]
            {
                $"str.indexOf({text}, \"a\")",
                $"str.replace({text}, \"a\", \"<>\")",
                "str.split(\"x,y,,z\", \",\")",
                $"str.join([{Int()}, \"q\", true], \"-\")",
                $"str.trim(\"  \" + {text} + \" \")",
                $"str.startsWith({text}, \"H\")",
                $"str.repeat(\"ab\", {Between(0, 3)})",
                $"str.padStart(string({Int()}), 5, \"0\")",
                $"str.substring(\"abcdef\", {Between(0, 3)}, {Between(0, 3)})"
            })});");
        }
        else if (roll < 0.92 && scope.Arrays.Count > 0)
        {
            var (array, _) = Pick(scope.Arrays);
            Emit($"io.print({array}.map((x) => x * {Between(-2, 3)}).filter((x) => x != {Between(-3, 3)}));");
            Emit($"io.print(str.length(string({array})));");
        }
        else
        {
            var name = Fresh("t");
            Emit($"var {name} = {BoolExpr(scope, 1)} ? {Int()} : {Int()};");
            Emit($"io.print({name});");
        }
    }

    /// <summary>
    /// Double locals, compound assignment, ints widened to doubles in place, accumulating
    /// loops, double arrays and lambdas called with both int and double arguments.
    /// </summary>
    private void DoubleStatement(Scope scope, int depth)
    {
        var roll = Roll();
        if (roll < 0.25 || scope.Doubles.Count == 0)
        {
            var name = Fresh("x");
            Emit($"var {name} = {Clamp(DoubleExpr(scope))};");
            scope.Doubles.Add(name);
        }
        else if (roll < 0.4)
        {
            var name = Pick(scope.Doubles);
            if (scope.ReadOnly.Contains(name))
                return;
            var op = Pick(new[] { "=", "+=", "-=", "*=" });
            var value = op == "*=" ? DoubleLeaf(scope) : Clamp(DoubleExpr(scope));
            Emit($"{name} {op} {value};");
            if (op != "=")
                Emit($"{name} = {name} % 1000;");
        }
        else if (roll < 0.5)
        {
            // An int-initialized local that later holds a double.
            var name = Fresh("m");
            Emit($"var {name} = {Clamp(IntExpr(scope, 1))};");
            Emit($"{name} += {Pick(new[] { "0.5", "0.1", "-1.25" })};");
            Emit($"io.print({name});");
            scope.Doubles.Add(name);
        }
        else if (roll < 0.6 && depth > 0)
        {
            var acc = Fresh("acc");
            var index = Fresh("i");
            Emit($"var {acc} = {Pick(new[] { "0", "0.0", "1" })};");
            Emit($"for (var {index} = 0; {index} < {Between(1, 6)}; {index} = {index} + 1) {{");
            _indent++;
            Emit($"{acc} = ({acc} + {index} * {Pick(new[] { "0.1", "0.5", "1.5", "2" })}) % 1000;");
            _indent--;
            Emit("}");
            Emit($"io.print({acc});");
            scope.Doubles.Add(acc);
        }
        else if (roll < 0.7)
        {
            var name = Fresh("da");
            var items = Enumerable.Range(0, Between(1, 4))
                .Select(_ => Roll() < 0.6 ? Clamp(DoubleExpr(scope, 1)) : IntLeaf(scope));
            Emit($"var {name} = [{string.Join(", ", items)}];");
            Emit($"io.print({name});");
            var sum = Fresh("sum");
            var item = Fresh("e");
            Emit($"var {sum} = 0;");
            Emit($"foreach (var {item} in {name}) {{ {sum} = {sum} + {item}; }}");
            Emit($"io.print({sum});");
            Emit($"io.print({name}.map((y) => y * {DoubleLeaf(scope)}).filter((y) => y > {DoubleLeaf(scope)}));");
            scope.Doubles.Add(sum);
        }
        else if (roll < 0.8)
        {
            var name = Fresh("dl");
            Emit($"var {name} = (y) => y * {Pick(new[] { "0.5", "1.5", "3" })} + {DoubleLeaf(scope)};");
            Emit($"io.print({name}({IntExpr(scope, 1)}));");
            Emit($"io.print({name}({DoubleExpr(scope, 1)}));");
        }
        else if (roll < 0.9)
        {
            var kind = Roll();
            if (kind < 0.4)
                Emit($"io.print({DoubleExpr(scope)});");
            else if (kind < 0.6)
                Emit($"io.print(\"d:\" + {DoubleExpr(scope, 1)});");
            else if (kind < 0.8)
                Emit($"io.print([{DoubleExpr(scope, 1)}, {IntExpr(scope, 1)}]);");
            else
                Emit($"io.print(string({DoubleExpr(scope, 1)}) + {IntExpr(scope, 1)});");
        }
        else
        {
            var box = Fresh("box");
            Emit($"var {box} = new Box({DoubleExpr(scope, 1)});");
            Emit($"io.print({box}.add({DoubleLeaf(scope)}));");
            Emit($"io.print({box}.twice() + {box}.v);");
        }
    }

    /// <summary>Flat built-ins that generated code calls; a binding of the same name shadows them.</summary>
    private static readonly string[] ShadowableBuiltIns = { "reverse", "range", "sort", "includes" };

    private string Call(string name, int arity, Scope scope) =>
        $"{name}({string.Join(", ", Enumerable.Range(0, arity).Select(_ => IntExpr(scope, 1)))})";

    /// <summary>
    /// Functions as values and bindings that shadow them: lambda parameters, block locals,
    /// loop and catch variables named like a user function or a flat built-in; user functions
    /// of every arity stored in dicts, arrays and variables; closure factories and calls on
    /// call results. The shadowed function is called again afterwards, where it is visible.
    /// </summary>
    private void FunctionValueStatement(Scope scope)
    {
        string Int() => IntExpr(scope, 1);
        var helpers = scope.Functions;
        var unary = helpers.Where(f => f.Arity == 1).Select(f => f.Name).Concat(scope.Lambdas).ToList();
        var shadowNames = helpers.Select(f => f.Name).Append("area").Concat(ShadowableBuiltIns).ToList();
        var roll = Roll();
        if (roll < 0.15)
        {
            var name = Pick(shadowNames);
            var lambda = Fresh("sl");
            Emit($"var {lambda} = ({name}) => {Clamp($"{name} * {Between(-3, 3)} + {IntLeaf(scope)}")};");
            Emit($"io.print({lambda}({Int()}));");
        }
        else if (roll < 0.3)
        {
            var name = Pick(shadowNames);
            var lambda = Fresh("sb");
            Emit($"var {lambda} = (n) => {{ var {name} = {Clamp($"n + {IntLeaf(scope)}")}; {name} = {name} * 2; return {name}; }};");
            Emit($"io.print({lambda}({Int()}));");
        }
        else if (roll < 0.4)
        {
            var name = Pick(shadowNames);
            var lambda = Fresh("sf");
            var array = scope.Arrays.Count > 0 ? Pick(scope.Arrays).Name : $"[{Int()}, {Int()}]";
            Emit($"var {lambda} = (n) => {{ var t = 0; foreach (var {name} in {array}) {{ t = (t + {name} * n) % 1000; }} return t; }};");
            Emit($"io.print({lambda}({Int()}));");
        }
        else if (roll < 0.5)
        {
            var name = Pick(shadowNames);
            Emit($"try {{ assert(false, \"shadow-{_nameCounter}\"); }} catch ({name}) {{ io.print(\"caught \" + {name}); }}");
        }
        else if (roll < 0.7 && helpers.Count > 0)
        {
            var dict = Fresh("fx");
            var (name, arity) = Pick(helpers);
            var lambda = scope.Lambdas.Count > 0 ? Pick(scope.Lambdas) : "(x) => x - 1";
            Emit($"var {dict} = {{ \"f\": {name}, \"l\": {lambda}, \"k\": (x) => {Clamp($"x + {IntLeaf(scope)}")} }};");
            Emit($"io.print({Call($"{dict}.f", arity, scope)});");
            Emit($"io.print({Call($"{dict}[\"f\"]", arity, scope)});");
            Emit($"io.print({dict}.l({Int()}) + {dict}[\"k\"]({Int()}));");
            if (Roll() < 0.5)
            {
                Emit($"{dict}.k = (x) => {Clamp($"x * {Between(2, 4)}")};");
                Emit($"io.print({dict}.k({Int()}));");
            }
            var alias = Fresh("fv");
            Emit($"var {alias} = {name};");
            Emit($"io.print({Call(alias, arity, scope)});");
        }
        else if (roll < 0.8 && unary.Count > 0)
        {
            var items = Enumerable.Range(0, Between(1, 3)).Select(_ => Pick(unary)).Append("(x) => x % 7").ToList();
            var array = Fresh("fa");
            Emit($"var {array} = [{string.Join(", ", items)}];");
            Emit($"io.print({array}[{_random.Next(items.Count)}]({Int()}));");
            Emit($"io.print([{Int()}, {Int()}].map({Pick(unary)}));");
        }
        else
        {
            var factory = Fresh("mk");
            Emit($"var {factory} = (k) => (x) => {Clamp("x * 2 + k")};");
            Emit($"io.print({factory}({Int()})({Int()}));");
            var made = Fresh("lam");
            Emit($"var {made} = {factory}({Int()});");
            scope.Lambdas.Add(made);
            Emit($"io.print({made}({Int()}));");
        }

        // The shadowed names are functions again out here.
        if (helpers.Count > 0 && Roll() < 0.5)
        {
            var (name, arity) = Pick(helpers);
            Emit($"io.print({Call(name, arity, scope)});");
        }
    }

    /// <summary>
    /// Errors that every backend raises the same way: throw of strings and ints (also from
    /// nested lambdas and map callbacks), rethrow, and finally after the try or catch body
    /// leaves by falling through, break, continue, return or throw. Built-in failures (null
    /// member or call, bad JSON, pop of an empty array) print a fixed text, since their messages
    /// differ by host. Array reads out of range and division by zero throw the same message on
    /// every backend, a write past the end of an array pads it with null, and int() of a string
    /// that is not an integer gives 0.
    /// </summary>
    private void ErrorPathStatement(Scope scope)
    {
        string Int() => IntExpr(scope, 1);
        var tag = Fresh("err");
        var roll = Roll();
        if (roll < 0.15)
        {
            var thrown = Roll() < 0.5 ? $"\"{tag}-\" + {Int()}" : Clamp(Int());
            Emit($"try {{ if ({BoolExpr(scope, 1)}) {{ throw {thrown}; }} io.print(\"no throw\"); }} catch (e) {{ io.print(\"caught \" + e); }}");
        }
        else if (roll < 0.3)
        {
            var log = Fresh("log");
            Emit($"var {log} = \"\";");
            Emit($"try {{ {log} = {log} + \"t\"; if ({BoolExpr(scope, 1)}) {{ throw \"{tag}\"; }} {log} = {log} + \"n\"; }} catch (e) {{ {log} = {log} + \"c\" + e; }} finally {{ {log} = {log} + \"f\"; }}");
            Emit($"io.print({log});");
        }
        else if (roll < 0.5)
        {
            // A loop whose try or catch body leaves by break or continue; finally still runs.
            var acc = Fresh("acc");
            var index = Fresh("i");
            var exit = Pick(new[] { "break", "continue" });
            var exitInCatch = Roll() < 0.5;
            Emit($"var {acc} = 0;");
            Emit($"for (var {index} = 0; {index} < {Between(1, 5)}; {index} = {index} + 1) {{");
            _indent++;
            var tryBody = exitInCatch
                ? $"if ({index} % {Between(2, 3)} == 0) {{ throw \"{tag}\"; }} {acc} = {acc} + 1;"
                : $"{acc} = {acc} + 1; if ({index} >= {Between(0, 3)}) {{ {exit}; }}";
            var catchBody = exitInCatch ? $"{acc} = {acc} + 10; {exit};" : $"{acc} = {acc} + 10;";
            Emit($"try {{ {tryBody} }} catch (e) {{ {catchBody} }} finally {{ {acc} = {acc} + 100; }}");
            _indent--;
            Emit("}");
            Emit($"io.print({acc});");
            scope.Ints.Add(acc);
        }
        else if (roll < 0.6)
        {
            var lambda = Fresh("rt");
            var fromCatch = Roll() < 0.5;
            var body = fromCatch
                ? $"try {{ throw \"{tag}\"; }} catch (e) {{ return n + 1; }} finally {{ io.print(\"fin {tag}\"); }}"
                : $"try {{ if (n > {Between(-5, 10)}) {{ return n * 2; }} }} finally {{ io.print(\"fin {tag}\"); }} return n;";
            Emit($"var {lambda} = (n) => {{ {body} }};");
            Emit($"io.print({lambda}({Int()}));");
        }
        else if (roll < 0.7)
        {
            Emit($"try {{ try {{ throw \"{tag}\"; }} catch (e) {{ throw \"re-\" + e; }} finally {{ io.print(\"fin {tag}\"); }} }} catch (e) {{ io.print(\"outer \" + e); }}");
        }
        else if (roll < 0.8)
        {
            var thrower = Fresh("th");
            Emit($"var {thrower} = (n) => {{ if (n > {Between(-5, 10)}) {{ throw \"{tag}:\" + n; }} return n; }};");
            Emit($"try {{ io.print({thrower}({Int()}) + {thrower}({Int()})); }} catch (e) {{ io.print(\"caught \" + e); }}");
        }
        else if (roll < 0.85)
        {
            var array = scope.Arrays.Count > 0 ? Pick(scope.Arrays).Name : $"[{Int()}, {Int()}, {Int()}]";
            Emit($"try {{ io.print({array}.map((x) => {{ if (x > {Between(-5, 20)}) {{ throw \"{tag}:\" + x; }} return x; }})); }} catch (e) {{ io.print(\"caught \" + e); }}");
        }
        else if (roll < 0.94)
        {
            var name = Fresh("ix");
            var length = Between(1, 3);
            Emit($"var {name} = [{string.Join(", ", Enumerable.Range(0, length).Select(_ => Int()))}];");
            var zero = Fresh("z");
            Emit($"var {zero} = {Pick(new[] { "0", "0.0" })};");
            var statement = Pick(new[]
            {
                $"io.print({name}[{Pick(new[] { length, length + 2, -1 })}]);",
                $"io.print({name}[{length}] + 1);",
                $"{name}[{length + 1}] += 1; io.print({name});",
                $"{name}[{length + Between(0, 2)}] = {Int()}; io.print({name}); io.print({name}.length);",
                $"{name}[-1] = 1; io.print({name});",
                $"io.print({Int()} / {zero});",
                $"io.print({Int()} % {zero});",
                $"{name}[0] /= {zero}; io.print({name});",
                $"io.print(int({Pick(new[] { "\"abc\"", "\"\"", "\"12x\"", "\" 7 \"", "\"3.5\"", "\"1e3\"", "\"-42\"" })}));"
            });
            Emit($"try {{ {statement} }} catch (e) {{ io.print(\"caught \" + e); }}");
        }
        else
        {
            var failing = Pick(new[]
            {
                "var n = null; io.print(n.x);",
                "var n = null; io.print(n(1));",
                "io.print(parseJSON(\"{x\"));",
                "var e0 = []; io.print(e0.pop());",
                $"assert({BoolExpr(scope, 1)}, \"{tag}\"); io.print(\"held\");"
            });
            Emit($"try {{ {failing} }} catch (e) {{ io.print(\"failed {tag}\"); }}");
        }
    }

    private void Block(Scope scope, int depth)
    {
        _indent++;
        var count = Between(1, 4);
        for (var i = 0; i < count; i++)
            Statement(scope, depth);
        _indent--;
    }

    private string Program(int caseCount)
    {
        var top = new Scope();

        Emit("class Box {");
        Emit("    var v = 0;");
        Emit("    function Box(v) { this.v = v % 1000; }");
        Emit($"    function add(n) {{ this.v = (this.v + n * {Between(1, 3)}) % 1000; return this.v; }}");
        Emit("    function twice() { return this.v * 2; }");
        Emit("}");
        Emit("type Shape = Circle(r) | Rect(w, h) | Empty();");
        Emit("function area(s) {");
        Emit("    return match s {");
        Emit($"        case Circle(r): r * r * {Between(1, 3)};");
        Emit("        case Rect(w, h): w * h;");
        Emit("        case Empty(): 0;");
        Emit("    };");
        Emit("}");

        // Module state that the helper functions and every case read and write.
        var globalCount = Between(1, 3);
        for (var i = 0; i < globalCount; i++)
        {
            var name = Fresh("g");
            Emit($"var {name} = {Between(-5, 30)};");
            top.Ints.Add(name);
        }
        var globalDouble = Fresh("gd");
        Emit($"var {globalDouble} = {Pick(new[] { "0.5", "2.25", "-1.5", "0.1" })};");
        top.Doubles.Add(globalDouble);
        var globalString = Fresh("gs");
        Emit($"var {globalString} = \"start\";");
        top.Strings.Add(globalString);

        // Helpers may call only earlier helpers, so there is no unbounded recursion.
        var helperCount = Between(1, 4);
        for (var i = 0; i < helperCount; i++)
        {
            var name = Fresh("f");
            var arity = Between(0, 2);
            var parameters = Enumerable.Range(0, arity).Select(_ => Fresh("p")).ToList();
            Emit($"function {name}({string.Join(", ", parameters)}) {{");
            var body = top.Child();
            body.Ints.AddRange(parameters);
            body.Lambdas.Clear();
            Block(body, 1);
            _indent++;
            Emit($"return {Clamp(IntExpr(body))};");
            _indent--;
            Emit("}");
            top.Functions.Add((name, arity));
        }

        // Double helpers call only earlier helpers too; callers pass ints and doubles.
        var doubleHelperCount = Between(1, 3);
        for (var i = 0; i < doubleHelperCount; i++)
        {
            var name = Fresh("fd");
            var arity = Between(1, 2);
            var parameters = Enumerable.Range(0, arity).Select(_ => Fresh("q")).ToList();
            Emit($"function {name}({string.Join(", ", parameters)}) {{");
            var body = top.Child();
            body.Doubles.AddRange(parameters);
            body.Lambdas.Clear();
            _indent++;
            var local = Fresh("t");
            Emit($"var {local} = {parameters[0]} * {Pick(new[] { "0.5", "1.5", "2" })};");
            body.Doubles.Add(local);
            if (Roll() < 0.5)
                Emit($"if ({local} > {Between(-5, 50)}) {{ return {Clamp(DoubleExpr(body, 1))}; }}");
            Emit($"return {Clamp(DoubleExpr(body))};");
            _indent--;
            Emit("}");
            top.DoubleFunctions.Add((name, arity));
        }

        // Bounded recursion: the function stops above depth 900 itself, because calls through
        // function arrays and map() pass unclamped values (g * g) that would overflow the
        // native stack on C# and JavaScript while the interpreter keeps going.
        var recursive = Fresh("rec");
        Emit($"function {recursive}(n) {{ if (n <= 0 || n > 900) {{ return 1; }} return ({recursive}(n - 1) + n * 3) % 1000; }}");
        Emit(string.Empty);

        for (var c = 0; c < caseCount; c++)
        {
            Emit($"function case{c}() {{");
            var scope = top.Child();
            scope.Functions.Add((recursive, 1));
            _indent++;
            var count = Between(2, 6);
            for (var i = 0; i < count; i++)
                Statement(scope, 2);
            Emit($"io.print({recursive}({Between(0, 6)}));");
            _indent--;
            Emit("}");
        }

        Emit(string.Empty);
        for (var c = 0; c < caseCount; c++)
        {
            Emit($"io.print(\"#{c}\");");
            Emit($"case{c}();");
        }
        Emit($"io.print([{string.Join(", ", top.Ints)}]);");
        Emit($"io.print({globalDouble});");
        Emit($"io.print({globalString});");

        var builder = new StringBuilder();
        foreach (var line in _lines)
            builder.Append(line).Append('\n');
        return builder.ToString();
    }
}
