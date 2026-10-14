// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;

namespace MaldaLang.Tests.Fuzz;

/// <summary>
/// Seeded generator of small, always-terminating, error-free <c>.malda</c> programs for
/// differential testing of the interpreter, C# transpile and JavaScript transpile.
/// Values stay small (every stored int is reduced <c>% 1000</c>) so no backend hits int
/// overflow, divisors are non-zero literals and array indexes are in range by construction.
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
        public List<string> Strings { get; init; } = new();
        public List<(string Name, int Length)> Arrays { get; init; } = new();
        public List<string> Lambdas { get; init; } = new();
        public List<(string Name, int Arity)> Functions { get; init; } = new();
        /// <summary>Loop variables: readable, never assigned by generated code.</summary>
        public HashSet<string> ReadOnly { get; init; } = new(StringComparer.Ordinal);

        public Scope Child() => new()
        {
            Ints = new List<string>(Ints),
            Strings = new List<string>(Strings),
            Arrays = new List<(string, int)>(Arrays),
            Lambdas = new List<string>(Lambdas),
            Functions = new List<(string, int)>(Functions),
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
        return Between(-9, 20).ToString();
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
        if (roll < 0.9 && scope.Ints.Count > 0)
            return $"$\"<{{{Pick(scope.Ints)}}}|{{{IntExpr(scope, 1)}}}>\"";
        return $"str.lower({StrExpr(scope, depth - 1)})";
    }

    // ---- statements -----------------------------------------------------------------

    private void Statement(Scope scope, int depth)
    {
        if (Roll() < 0.25)
        {
            ExtraStatement(scope, depth);
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

        // Bounded recursion: arguments are clamped ints, so depth stays below 1000.
        var recursive = Fresh("rec");
        Emit($"function {recursive}(n) {{ if (n <= 0) {{ return 1; }} return ({recursive}(n - 1) + n * 3) % 1000; }}");
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
        Emit($"io.print({globalString});");

        var builder = new StringBuilder();
        foreach (var line in _lines)
            builder.Append(line).Append('\n');
        return builder.ToString();
    }
}
