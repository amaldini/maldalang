// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Compiler;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

public class GlslTranspilerTests : TestBase
{
    [Fact]
    public void ParseFunction_OutTypeHint_StoresPrefixedHint()
    {
        var source = "function closestHit(origin: vec3, tHit: out float) -> bool { return true; }";
        var statements = Parse(source);
        var func = Assert.IsType<FunctionDeclaration>(Assert.Single(statements));
        Assert.Equal(new[] { "vec3", "out float" }, func.ParameterTypeHints);
        Assert.Equal("bool", func.ReturnType);
    }

    [Fact]
    public void TranspileFunction_EmitsGlslSignatureAndBody()
    {
        var source = """
            @shader()
            function hitSphere(center: vec3, radius: float, origin: vec3, dir: vec3) -> float {
                var oc: vec3 = origin - center;
                var disc: float = dot(oc, dir);
                if (disc < 0.0) {
                    return -1.0;
                }
                return sqrt(disc);
            }
            """;
        var func = Assert.IsType<FunctionDeclaration>(Assert.Single(Parse(source)));
        var glsl = GlslTranspiler.TranspileFunction(func);

        Assert.Contains("float hitSphere(vec3 center, float radius, vec3 origin, vec3 dir)", glsl, StringComparison.Ordinal);
        Assert.Contains("vec3 oc = (origin - center);", glsl, StringComparison.Ordinal);
        Assert.Contains("if ((disc < 0.0))", glsl, StringComparison.Ordinal);
        Assert.Contains("return sqrt(disc);", glsl, StringComparison.Ordinal);
        Assert.DoesNotContain("function hitSphere", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_RenamesEntryToMain_AndWritesUniforms()
    {
        var source = """
            @shader()
            function fragmentMain() {
                gl_FragColor = vec4(vUv, 0.0, 1.0);
            }
            """;
        var func = Assert.IsType<FunctionDeclaration>(Assert.Single(Parse(source)));
        var glsl = GlslTranspiler.Compile(new GlslCompileRequest
        {
            Varyings = ["vec2 vUv"],
            Uniforms = ["vec3 uCamPos"],
            Consts = ["float EPSILON = 0.0015"],
            Functions = [func],
            MainFunctionName = "fragmentMain"
        });

        Assert.Contains("varying vec2 vUv;", glsl, StringComparison.Ordinal);
        Assert.Contains("uniform vec3 uCamPos;", glsl, StringComparison.Ordinal);
        Assert.Contains("const float EPSILON = 0.0015;", glsl, StringComparison.Ordinal);
        Assert.Contains("void main() {", glsl, StringComparison.Ordinal);
        Assert.DoesNotContain("function fragmentMain", glsl, StringComparison.Ordinal);
        Assert.DoesNotContain("void fragmentMain", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_UniformArray_DeclaresArrayAndIndexes()
    {
        var source = """
            @shader()
            function carPosition(index: int) -> vec3 {
                return uCars[index];
            }
            """;
        var func = Assert.IsType<FunctionDeclaration>(Assert.Single(Parse(source)));
        var glsl = GlslTranspiler.Compile(new GlslCompileRequest
        {
            Uniforms = ["vec3 uCars[CAR_COUNT]", "float uAngles[8]"],
            Consts = ["int CAR_COUNT = 8"],
            Functions = [func]
        });

        Assert.Contains("uniform vec3 uCars[CAR_COUNT];", glsl, StringComparison.Ordinal);
        Assert.Contains("uniform float uAngles[8];", glsl, StringComparison.Ordinal);
        Assert.Contains("return uCars[index];", glsl, StringComparison.Ordinal);
        Assert.True(
            glsl.IndexOf("const int CAR_COUNT", StringComparison.Ordinal) < glsl.IndexOf("uniform vec3 uCars", StringComparison.Ordinal),
            "a const that sizes a uniform array must be declared first");
    }

    [Fact]
    public void JsTranspile_InlinesGlslCompile_AndSkipsShaderJsFunctions()
    {
        var source = """
            @shader()
            function vertexMain() {
                vUv = uv;
                gl_Position = vec4(position.xy, 0.0, 1.0);
            }

            var vertexShader = glsl.compile({
                varyings: ["vec2 vUv"],
                functions: ["vertexMain"],
                main: "vertexMain"
            });
            """;
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource(source);

        Assert.Contains("varying vec2 vUv", js, StringComparison.Ordinal);
        Assert.Contains("gl_Position", js, StringComparison.Ordinal);
        Assert.Contains("void main()", js, StringComparison.Ordinal);
        Assert.DoesNotContain("function vertexMain", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.glsl", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspile_MapsMathCalls_AndOutParams()
    {
        var source = """
            @shader()
            function shade(tHit: out float, dir: vec3) -> bool {
                tHit = math.sqrt(dot(dir, dir));
                return tHit > 0.0;
            }

            var frag = glsl.compile({
                functions: ["shade"]
            });
            """;
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource(source);
        Assert.Contains("bool shade(out float tHit, vec3 dir)", js, StringComparison.Ordinal);
        Assert.Contains("tHit = sqrt(dot(dir, dir));", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.math.sqrt", js, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileFunction_FixedArrayLocalsAndTypedFor_EmitGlslStackLoop()
    {
        var source = """
            @shader()
            function runStack(n: int) -> float {
                var stack: float[8];
                var points: vec3[STACK] = uPoints;
                var sp: int = 0;
                for (var i: int = 0; i < 64; i++) {
                    if (i >= n) {
                        continue;
                    }
                    stack[sp] = length(points[i]);
                    sp++;
                }
                return stack[0];
            }
            """;
        var func = Assert.IsType<FunctionDeclaration>(Assert.Single(Parse(source)));
        var glsl = GlslTranspiler.TranspileFunction(func);

        Assert.Contains("float stack[8];", glsl, StringComparison.Ordinal);
        Assert.Contains("vec3 points[STACK] = uPoints;", glsl, StringComparison.Ordinal);
        // A real `for`, so `continue` still runs the increment (not the parser's desugared while).
        Assert.Contains("for (int i = 0; (i < 64); i++)", glsl, StringComparison.Ordinal);
        Assert.DoesNotContain("while", glsl, StringComparison.Ordinal);
        Assert.Contains("sp++;", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_FixedArrayHintOutsideShader_IsAnError()
    {
        var parser = new Parser.Parser(new Lexer("var stack: float[8];").Tokenize());
        parser.Parse();
        Assert.Contains(parser.Errors, e => e.Message.Contains("only valid inside @shader() functions", StringComparison.Ordinal));
    }

    [Fact]
    public void TypedForInitializer_RunsOnTheHost()
    {
        var output = RunProgram("""
            var total = 0;
            for (var i: int = 0; i < 4; i++) {
                if (i == 1) {
                    continue;
                }
                total += i;
            }
            io.print(total);
            """);
        Assert.Equal("5", output.Trim());
    }

    // ---- G23a step 1: the type pass ----

    [Theory]
    [InlineData("a + b", "float")]
    [InlineData("v + w", "vec3")]
    [InlineData("v * a", "vec3")]
    [InlineData("a * v", "vec3")]
    [InlineData("v / a", "vec3")]
    [InlineData("-v", "vec3")]
    [InlineData("i + j", "int")]
    [InlineData("i / j", "int")]
    [InlineData("i % j", "int")]
    [InlineData("a / b", "float")]
    [InlineData("float(i) / a", "float")]
    [InlineData("iv * i", "ivec2")]
    [InlineData("a < b", "bool")]
    [InlineData("i == j", "bool")]
    [InlineData("v == w", "bool")]
    [InlineData("flag && a > 0.0", "bool")]
    [InlineData("flag ? v : w", "vec3")]
    [InlineData("1", "int")]
    [InlineData("1.0", "float")]
    [InlineData("true", "bool")]
    public void TypePass_OperatorResultTypes(string expression, string expected)
    {
        Assert.Equal(expected, TypeOfExpression(expression, expected));
    }

    [Theory]
    [InlineData("v.x", "float")]
    [InlineData("v.xz", "vec2")]
    [InlineData("v.zyx", "vec3")]
    [InlineData("v.xxyy", "vec4")]
    [InlineData("c.rgb", "vec3")]
    [InlineData("c.a", "float")]
    [InlineData("c.st", "vec2")]
    [InlineData("iv.yx", "ivec2")]
    [InlineData("v[i]", "float")]
    [InlineData("arr[i]", "float")]
    [InlineData("pts[2].xy", "vec2")]
    [InlineData("arr.length()", "int")]
    public void TypePass_SwizzlesAndIndexing(string expression, string expected)
    {
        Assert.Equal(expected, TypeOfExpression(expression, expected));
    }

    [Theory]
    [InlineData("vec3(a)", "vec3")]
    [InlineData("vec3(a, b, 1.0)", "vec3")]
    [InlineData("vec4(v, 1.0)", "vec4")]
    [InlineData("vec4(v.xy, v.xy)", "vec4")]
    [InlineData("vec2(v)", "vec2")]
    [InlineData("vec2(iv)", "vec2")]
    [InlineData("ivec2(i, j)", "ivec2")]
    [InlineData("int(a)", "int")]
    [InlineData("float(i)", "float")]
    [InlineData("length(v)", "float")]
    [InlineData("dot(v, w)", "float")]
    [InlineData("distance(v, w)", "float")]
    [InlineData("cross(v, w)", "vec3")]
    [InlineData("normalize(v)", "vec3")]
    [InlineData("abs(v)", "vec3")]
    [InlineData("abs(i)", "int")]
    [InlineData("max(v, 0.0)", "vec3")]
    [InlineData("max(v, w)", "vec3")]
    [InlineData("min(a, b)", "float")]
    [InlineData("min(i, j)", "int")]
    [InlineData("clamp(v, 0.0, 1.0)", "vec3")]
    [InlineData("clamp(a, 0.0, 1.0)", "float")]
    [InlineData("mix(v, w, a)", "vec3")]
    [InlineData("mix(v, w, v)", "vec3")]
    [InlineData("step(0.5, v)", "vec3")]
    [InlineData("smoothstep(0.0, 1.0, v)", "vec3")]
    [InlineData("smoothstep(0.0, 1.0, a)", "float")]
    [InlineData("fract(sin(a) * 43758.5453)", "float")]
    [InlineData("floor(v.xz)", "vec2")]
    [InlineData("mod(v, 2.0)", "vec3")]
    [InlineData("pow(a, b)", "float")]
    [InlineData("atan(a, b)", "float")]
    [InlineData("math.sqrt(a)", "float")]
    [InlineData("math.max(a, b)", "float")]
    [InlineData("reflect(v, w)", "vec3")]
    [InlineData("lessThan(v, w)", "bvec3")]
    [InlineData("any(lessThan(v, w))", "bool")]
    public void TypePass_ConstructorsAndBuiltIns(string expression, string expected)
    {
        Assert.Equal(expected, TypeOfExpression(expression, expected));
    }

    [Theory]
    [InlineData("a + i", "no implicit int to float conversion")]
    [InlineData("v * i", "no implicit int to float conversion")]
    [InlineData("v + c", "cannot combine vec3 and vec4")]
    [InlineData("a % b", "'%' needs int operands")]
    [InlineData("v < w", "needs two int or two float scalars")]
    [InlineData("a == i", "same type")]
    [InlineData("!a", "'!' needs a bool")]
    [InlineData("flag + flag", "needs int or float operands")]
    [InlineData("flag ? a : i", "branches of ?: differ")]
    [InlineData("v.w", "'.w' is not a swizzle of vec3")]
    [InlineData("v.xr", "'.xr' is not a swizzle of vec3")]
    [InlineData("a.x", "needs a vector")]
    [InlineData("v[a]", "an index must be int")]
    [InlineData("a[0]", "cannot be indexed")]
    [InlineData("vec3(a, b)", "needs 3 components")]
    [InlineData("vec2(v, a)", "too many arguments")]
    [InlineData("max(v, i)", "no overload of max() takes (vec3, int)")]
    [InlineData("cross(v.xy, v.xy)", "no overload of cross()")]
    [InlineData("length(i)", "no overload of length()")]
    [InlineData("frobnicate(a)", "'frobnicate' is not an @shader() function")]
    [InlineData("uTime * a", "'uTime' is not a parameter or local")]
    [InlineData("math.random()", "math.random has no GLSL mapping")]
    public void TypePass_RejectsIllTypedExpressions(string expression, string expectedMessage)
    {
        var ex = Assert.Throws<NotSupportedException>(() => TypeOfExpression(expression, "float"));
        Assert.Contains(expectedMessage, ex.Message, StringComparison.Ordinal);
        Assert.Contains("in @shader() function 'probe'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var x: float = 1;", "cannot initialize 'x' (float) with int")]
    [InlineData("var x: vec3 = vec2(a, b);", "cannot initialize 'x' (vec3) with vec2")]
    [InlineData("var x = a;", "local 'x' needs a GLSL type hint")]
    [InlineData("var n: int = 0; n = a;", "cannot assign float to int")]
    [InlineData("var x: float = a; x *= v;", "cannot assign vec3 to float")]
    [InlineData("if (a) { return; }", "an if condition must be bool, not float")]
    [InlineData("return a;", "returns float, but the function is declared -> void")]
    [InlineData("var x: float = a; var x: float = b;", "'x' is declared twice")]
    public void TypePass_RejectsIllTypedStatements(string body, string expectedMessage)
    {
        var func = ParseProbe(body);
        var ex = Assert.Throws<NotSupportedException>(() => GlslTranspiler.AnalyzeTypes(func));
        Assert.Contains(expectedMessage, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypePass_StatementsScopesAndCompoundAssignment()
    {
        var source = """
            @shader()
            function walk(n: int, p: vec3, hit: out vec3) -> float {
                var stack: float[8];
                var sp: int = 0;
                var acc: vec3 = p;
                for (var i: int = 0; i < 8; i++) {
                    if (i >= n) {
                        continue;
                    }
                    var d: float = length(acc) - float(i);
                    stack[sp] = d;
                    sp++;
                    acc *= 0.5;
                    acc.xz += vec2(d, d);
                }
                var d: float = stack[0];
                hit = acc;
                return d > 0.0 ? d : -d;
            }
            """;
        var func = Assert.IsType<FunctionDeclaration>(Assert.Single(Parse(source)));
        var types = GlslTranspiler.AnalyzeTypes(func);

        Assert.Equal("float", types.ReturnType.ToString());
        Assert.Equal(new[] { "int n", "vec3 p", "out vec3 hit" },
            types.Parameters.Select(p => $"{p.Qualifier} {p.Type} {p.Name}".Trim()));
        var stack = Assert.IsType<VarDeclStatement>(func.Body.Statements[0]);
        Assert.Equal("float[8]", types.TypeOf(stack).ToString());
        Assert.Equal(8, types.TypeOf(stack).ArrayLength);
        Assert.Empty(types.Callees);
    }

    [Fact]
    public void TypePass_UserCalls_CheckArgumentsAndRecordCallees()
    {
        var functions = ParseShaderFunctions("""
            @shader()
            function sdSphere(q: vec3, s: float) -> float {
                return length(q) - s;
            }

            @shader()
            function pick(kind: int, q: vec3, s: float) -> float {
                if (kind == 0) {
                    return sdSphere(q, s);
                }
                return sdSphere(q * 2.0, s) * 0.5;
            }

            @shader()
            function bad(q: vec3) -> float {
                return sdSphere(q, 1);
            }
            """);

        var types = GlslTranspiler.AnalyzeTypes(functions["pick"], functions);
        Assert.Equal(new[] { "sdSphere" }, types.Callees);

        var ex = Assert.Throws<NotSupportedException>(() => GlslTranspiler.AnalyzeTypes(functions["bad"], functions));
        Assert.Contains("argument 's' of sdSphere() is float, not int", ex.Message, StringComparison.Ordinal);
        Assert.Contains("in @shader() function 'bad'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypePass_Globals_TypeUniformsAndSamplers()
    {
        var func = Assert.IsType<FunctionDeclaration>(Assert.Single(Parse("""
            @shader()
            function fetch(at: int) -> vec4 {
                var points: vec3[COUNT] = uPoints;
                return texelFetch(uProgram, ivec2(at, 0), 0) * uGain + vec4(points[0], 0.0);
            }
            """)));
        var globals = new Dictionary<string, GlslType>
        {
            ["uProgram"] = GlslType.Opaque("sampler2D"),
            ["uGain"] = GlslType.Float,
            ["uPoints"] = GlslType.Vec(3) with { ArrayLength = 8 }
        };

        var types = GlslTranspiler.AnalyzeTypes(func, globals: globals);
        Assert.Equal(GlslType.Vec(4), types.ReturnType);
    }

    [Theory]
    [InlineData("float", "float")]
    [InlineData(" vec3 ", "vec3")]
    [InlineData("ivec2", "ivec2")]
    [InlineData("bvec4", "bvec4")]
    [InlineData("float[8]", "float[8]")]
    [InlineData("vec3[COUNT]", "vec3[]")]
    [InlineData("mat3", "mat3")]
    [InlineData("sampler2D", "sampler2D")]
    public void GlslType_ParsesAndPrintsHints(string hint, string expected)
    {
        Assert.True(GlslType.TryParse(hint, out var type));
        Assert.Equal(expected, type.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("out float")]
    [InlineData("float[8")]
    [InlineData("void[2]")]
    public void GlslType_RejectsMalformedHints(string hint)
    {
        Assert.False(GlslType.TryParse(hint, out _));
    }

    // The pure kernels G23a wants to call from the host must pass the type pass as they are written today.
    [Theory]
    [InlineData("three_sdf_interpreter.malda", "sdBox", "float")]
    [InlineData("three_sdf_interpreter.malda", "sdTorus", "float")]
    [InlineData("three_sdf_interpreter.malda", "sdCylinder", "float")]
    [InlineData("three_sdf_interpreter.malda", "sdShape", "float")]
    [InlineData("three_sdf_interpreter.malda", "shapeColor", "vec3")]
    [InlineData("three_sdf_interpreter.malda", "shapeRadius", "float")]
    [InlineData("three_sdf_invaders.malda", "hash3", "float")]
    [InlineData("three_sdf_invaders.malda", "smin", "float")]
    [InlineData("three_sdf_invaders.malda", "sdRoundBox", "float")]
    [InlineData("three_sdf_invaders.malda", "sdCapsule", "float")]
    [InlineData("three_sdf_invaders.malda", "sdEllipsoid", "float")]
    public void TypePass_AcceptsThePureKernelsOfTheSdfExamples(string file, string name, string returnType)
    {
        var path = Path.Combine(PlanningPaths.RepoRoot, "Examples", "Games", file);
        var functions = ParseShaderFunctions(File.ReadAllText(path));

        var types = GlslTranspiler.AnalyzeTypes(functions[name], functions);
        Assert.Equal(returnType, types.ReturnType.ToString());
    }

    [Fact]
    public void TypePass_SdShape_PullsInItsCallees()
    {
        var path = Path.Combine(PlanningPaths.RepoRoot, "Examples", "Games", "three_sdf_interpreter.malda");
        var functions = ParseShaderFunctions(File.ReadAllText(path));

        var types = GlslTranspiler.AnalyzeTypes(functions["sdShape"], functions);
        Assert.Equal(new[] { "sdBox", "sdTorus", "sdCylinder" }, types.Callees);
    }

    private const string ProbeParameters =
        "a: float, b: float, i: int, j: int, flag: bool, v: vec3, w: vec3, c: vec4, iv: ivec2";

    private static FunctionDeclaration ParseProbe(string body)
    {
        var source = "@shader()\nfunction probe(" + ProbeParameters + ") {\nvar arr: float[4];\nvar pts: vec3[4];\n" + body + "\n}";
        return Assert.IsType<FunctionDeclaration>(Assert.Single(Parse(source)));
    }

    /// <summary>Types <paramref name="expression"/> as the initializer of a local declared <paramref name="declaredAs"/>.</summary>
    private static string TypeOfExpression(string expression, string declaredAs)
    {
        var func = ParseProbe("var keep: " + declaredAs + " = " + expression + ";");
        var keep = Assert.IsType<VarDeclStatement>(func.Body.Statements[^1]);
        return GlslTranspiler.AnalyzeTypes(func).TypeOf(keep.Initializer).ToString();
    }

    private static Dictionary<string, FunctionDeclaration> ParseShaderFunctions(string source)
    {
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements
            .OfType<FunctionDeclaration>()
            .Where(ShaderFunction.IsMarked)
            .ToDictionary(f => f.Name, StringComparer.Ordinal);
    }

    private static List<MaldaLang.Parser.AST.Statements.Statement> Parse(string source)
    {
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }
}
