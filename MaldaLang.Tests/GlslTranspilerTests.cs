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
    [InlineData("three_sdf_pacman.malda", "yawXZ", "vec3")]
    [InlineData("three_sdf_pacman.malda", "sdPacman", "float")]
    [InlineData("three_sdf_pacman.malda", "sdGhostBody", "float")]
    [InlineData("three_sdf_pacman.malda", "sdGem", "float")]
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

    // ---- G23b step 1: top-level MALDA consts written into the GLSL ----

    private const string ConstKernels = """
        const K_SPHERE = 0;
        const K_RING = 9;
        const GLOW_R = 2.2;
        const FLOOR_Y = -1;
        const HALF: float = 2;
        const SHADOWS = true;
        const UNUSED = 5;
        const TWO_PI = math.pi * 2;
        var notConst = 3;

        @shader()
        function shapeRadius(kind: int, s: float) -> float {
            if (kind == K_SPHERE) {
                return s;
            }
            if (kind == K_RING && SHADOWS) {
                return s + GLOW_R / HALF;
            }
            return s * 1.3;
        }

        @shader()
        function floorDistance(p: vec3) -> float {
            return p.y - float(FLOOR_Y);
        }

        @shader()
        function fragmentMain() {
            gl_FragColor = vec4(shapeRadius(K_RING, uSize), floorDistance(vPos), 0.0, 1.0);
        }

        """;

    [Fact]
    public void GlslCompile_WritesTheMaldaConstsItsFunctionsUse_BeforeTheUniforms()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource(ConstKernels + """
            var fragment = glsl.compile({
                varyings: ["vec3 vPos"],
                uniforms: ["float uSize"],
                consts: ["float EPSILON = 0.0015"],
                functions: ["shapeRadius", "floorDistance"],
                main: "fragmentMain"
            });
            """);
        var glsl = GlslStringIn(js, "varying vec3 vPos;");

        // Typed from the literal, in declaration order, after the consts strings.
        Assert.Contains(
            "const float EPSILON = 0.0015;\nconst int K_SPHERE = 0;\nconst int K_RING = 9;\nconst float GLOW_R = 2.2;\n" +
            "const int FLOOR_Y = -1;\nconst float HALF = 2.0;\nconst bool SHADOWS = true;\nuniform float uSize;\n",
            glsl, StringComparison.Ordinal);
        // Emit follows use; a computed const and a var are not kernel consts.
        Assert.DoesNotContain("UNUSED", glsl, StringComparison.Ordinal);
        Assert.DoesNotContain("TWO_PI", glsl, StringComparison.Ordinal);
        Assert.DoesNotContain("notConst", glsl, StringComparison.Ordinal);
        Assert.Contains("if ((kind == K_SPHERE))", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public void GlslCompile_MaldaConst_OnlyForTheFunctionsOfThatCall()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource(ConstKernels + """
            var fragment = glsl.compile({ functions: ["floorDistance"] });
            """);
        var glsl = GlslStringIn(js, "float floorDistance(vec3 p)");

        Assert.StartsWith("const int FLOOR_Y = -1;\n\nfloat floorDistance(vec3 p)", glsl, StringComparison.Ordinal);
        Assert.DoesNotContain("K_SPHERE", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public void GlslCompile_MaldaConst_IsNotWrittenTwice_NorOverALocalOrAUniform()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            const K_SPHERE = 0;
            const LIMIT = 4;
            const SCALE = 2.0;
            const uGain = 1.5;
            const WIDTH = 8;

            @shader()
            function pick(kind: int, LIMIT: int) -> float {
                var SCALE: float = 0.5;
                if (kind == K_SPHERE && kind < LIMIT) {
                    return SCALE * uGain * float(WIDTH);
                }
                return 0.0;
            }

            var fragment = glsl.compile({
                header: "#define WIDTH 16",
                uniforms: ["uniform float uGain"],
                consts: ["int K_SPHERE = 7"],
                functions: ["pick"]
            });
            """);
        var glsl = GlslStringIn(js, "#define WIDTH 16");

        // The consts string wins; a parameter, a local, a uniform or a header name is not redeclared.
        Assert.Contains("const int K_SPHERE = 7;", glsl, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(glsl, @"\bconst\b"));
        Assert.Contains("uniform float uGain;", glsl, StringComparison.Ordinal);
    }

    [Fact]
    public void GlslSource_PrependsTheConstsItsFunctionsUse_AndGlslFunctionsStaysBare()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource(ConstKernels + """
            var source = glsl.source("floorDistance", "shapeRadius");
            var library = glsl.functions();
            """);

        var source = GlslStringIn(js, "const int K_SPHERE = 0;");
        Assert.StartsWith(
            "const int K_SPHERE = 0;\nconst int K_RING = 9;\nconst float GLOW_R = 2.2;\nconst int FLOOR_Y = -1;\n" +
            "const float HALF = 2.0;\nconst bool SHADOWS = true;\n\nfloat floorDistance(vec3 p)",
            source, StringComparison.Ordinal);
        Assert.Contains("\"floorDistance\": \"float floorDistance(vec3 p) {", js, StringComparison.Ordinal);
    }

    [Fact]
    public void GlslCompile_WithoutMaldaConsts_IsUnchanged()
    {
        var source = """
            @shader()
            function carPosition(index: int) -> vec3 {
                return uCars[index] * EPSILON;
            }
            """;
        var func = Assert.IsType<FunctionDeclaration>(Assert.Single(Parse(source)));
        var request = new GlslCompileRequest
        {
            Uniforms = ["vec3 uCars[CAR_COUNT]"],
            Consts = ["int CAR_COUNT = 8", "float EPSILON = 0.0015"],
            Functions = [func]
        };
        var withConsts = new GlslCompileRequest
        {
            Uniforms = request.Uniforms,
            Consts = request.Consts,
            Functions = request.Functions,
            MaldaConsts =
            [
                new GlslLiteralConst("EPSILON", GlslType.Float, "1.0"),
                new GlslLiteralConst("CAR_COUNT", GlslType.Int, "2"),
                new GlslLiteralConst("OTHER", GlslType.Int, "3")
            ]
        };

        Assert.Equal(GlslTranspiler.Compile(request), GlslTranspiler.Compile(withConsts));
    }

    [Theory]
    [InlineData("vec3 uCars[CAR_COUNT]", "uCars")]
    [InlineData("float EPSILON = 0.0015", "EPSILON")]
    [InlineData("uniform vec3 uCamPos", "uCamPos")]
    [InlineData("const int K=1;", "K")]
    [InlineData("highp float uTime", "uTime")]
    [InlineData("uniform highp sampler2D uMap", "uMap")]
    [InlineData("", null)]
    public void DeclaredName_ReadsTheNameOfAGlslDeclarationString(string declaration, string? expected)
    {
        Assert.Equal(expected, GlslTranspiler.DeclaredName(declaration));
    }

    /// <summary>The GLSL string literal of the generated JavaScript that contains <paramref name="marker"/>, unescaped.</summary>
    private static string GlslStringIn(string js, string marker)
    {
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(js, "\"(?:[^\"\\\\]|\\\\.)*\""))
        {
            var text = System.Text.Json.JsonSerializer.Deserialize<string>(match.Value)!;
            if (text.Contains(marker, StringComparison.Ordinal))
                return text;
        }

        Assert.Fail($"No string literal of the generated JavaScript contains '{marker}'.");
        return string.Empty;
    }

    // ---- G23b step 3: glsl.host at compile time ----

    private const string BoundKernels = """
        const OP_UNION = 1;

        @shader()
        function sdSphere(q: vec3, s: float) -> float {
            return length(q) - s;
        }

        @shader()
        function opAt(i: int) -> vec4 {
            return texelFetch(uProgram, ivec2(i, 0), 0);
        }

        @shader()
        function sceneDistance(p: vec3) -> float {
            var acc: float = uFar;
            for (var i: int = 0; i < uOpCount; i++) {
                var head: vec4 = opAt(i);
                if (int(head.x) == OP_UNION) {
                    acc = min(acc, sdSphere(p - head.yzw, uSizes[i]) * uScale.x);
                }
            }
            return acc;
        }

        @shader()
        function fragmentMain() {
            gl_FragColor = vec4(sceneDistance(vPos), 0.0, 0.0, 1.0);
        }

        var fragment = glsl.compile({
            varyings: ["vec3 vPos"],
            uniforms: ["sampler2D uProgram", "int uOpCount", "uniform float uFar", "vec3 uScale", "float uSizes[MAX_OPS]"],
            consts: ["int MAX_OPS = 16"],
            functions: ["sdSphere", "opAt", "sceneDistance"],
            main: "fragmentMain"
        });
        var material = three.createShaderMaterial({ "vertexShader": fragment, "fragmentShader": fragment, "uniforms": {} });

        """;

    private static GlslHostAnalysis AnalyzeHost(string source)
    {
        var statements = Parse(source);
        var functions = statements
            .OfType<FunctionDeclaration>()
            .Where(ShaderFunction.IsMarked)
            .ToDictionary(f => f.Name, StringComparer.Ordinal);
        var consts = statements
            .OfType<VarDeclStatement>()
            .Select(GlslLiteralConst.TryRead)
            .Where(constant => constant != null)
            .Select(constant => constant!)
            .ToList();
        return GlslHostAnalysis.Run(statements, functions, consts);
    }

    [Fact]
    public void GlslHost_BindsTheFunctionAndItsCallees_AndTypesTheUniforms()
    {
        var analysis = AnalyzeHost(BoundKernels + """
            var sceneAt = glsl.host("sceneDistance", material);
            """);

        Assert.Equal(new[] { "sceneDistance" }, analysis.BoundRoots);
        Assert.Equal(new[] { "sdSphere", "opAt", "sceneDistance" }, analysis.BoundFunctions.Select(f => f.Name));
        Assert.Empty(analysis.PlainFunctions);
        Assert.Equal(GlslType.Float, analysis.BoundTypes["sceneDistance"].ReturnType);
        Assert.Equal(new[] { "opAt", "sdSphere" }, analysis.BoundTypes["sceneDistance"].Callees);

        Assert.Equal("sampler2D", analysis.UniformTypes["uProgram"].ToString());
        Assert.Equal(GlslType.Int, analysis.UniformTypes["uOpCount"]);
        Assert.Equal(GlslType.Float, analysis.UniformTypes["uFar"]);
        Assert.Equal(GlslType.Vec(3), analysis.UniformTypes["uScale"]);
        Assert.Equal("float[]", analysis.UniformTypes["uSizes"].ToString());
        Assert.Equal("1", analysis.Consts["OP_UNION"].GlslValue);
    }

    [Fact]
    public void GlslHost_AFunctionCanBeBoundAndCalledByName()
    {
        var analysis = AnalyzeHost(BoundKernels + """
            var sceneAt = glsl.host("sceneDistance", material);
            var other = glsl.host("sceneDistance", material);
            io.print(sdSphere([1, 0, 0], 0.5));
            """);

        Assert.Equal(new[] { "sceneDistance" }, analysis.BoundRoots);
        Assert.Equal(new[] { "sdSphere" }, analysis.PlainFunctions.Select(f => f.Name));
        Assert.Contains("sdSphere", analysis.BoundTypes.Keys);
    }

    [Fact]
    public void GlslHost_NothingReached_IsEmpty()
    {
        var analysis = AnalyzeHost(BoundKernels + "io.print(fragment.length > 0);");

        Assert.Empty(analysis.BoundRoots);
        Assert.Empty(analysis.BoundFunctions);
        Assert.Empty(analysis.PlainFunctions);
    }

    [Theory]
    [InlineData("function f(p: vec3) -> float { return p.x * uGain; }", "io.print(f([1, 2, 3]));",
        "uniform 'uGain' has no host value when the function is called by name")]
    [InlineData("function f(p: vec3) -> float { return p.x * uGain; }", "io.print(f([1, 2, 3]));",
        "glsl.host(\"f\", material)")]
    [InlineData("function f(p: vec3) -> float { return texelFetch(uMap, ivec2(0, 0), 0).x; }", "io.print(f([1, 2, 3]));",
        "texelFetch() has no host value when the function is called by name: bind it to a material with glsl.host(\"f\", material)")]
    [InlineData("function f(p: vec3) -> float { return p.x * uMissing; }", "var h = glsl.host(\"f\", material);",
        "'uMissing' is not a parameter, a local, a top-level literal MALDA const or a uniform declared to glsl.compile in this file")]
    [InlineData("function f(p: vec3) -> float { return p.x * uTwice; }", "var h = glsl.host(\"f\", material);",
        "uniform 'uTwice' is declared as float and as vec3 in the glsl.compile calls of this file")]
    [InlineData("function f(p: vec3) -> float { return p.x + vUv.x; }", "var h = glsl.host(\"f\", material);",
        "varying 'vUv' has no host meaning")]
    [InlineData("function f(p: vec3) -> float { return gl_FragCoord.x + p.x; }", "var h = glsl.host(\"f\", material);",
        "gl_FragCoord has no host meaning")]
    [InlineData("function f(p: vec3) -> float { return dFdx(p.x) * uGain; }", "var h = glsl.host(\"f\", material);",
        "dFdx() has no host meaning")]
    [InlineData("function f(p: vec3) -> float { return texture(uMap, p.xy).x; }", "var h = glsl.host(\"f\", material);",
        "texture() has no host meaning")]
    [InlineData("function f(p: vec3, d: out float) -> bool { d = uGain; return true; }", "var h = glsl.host(\"f\", material);",
        "'out' parameter 'd' has no host meaning")]
    [InlineData("function f(p: vec3) -> vec3 { return p * mat3(uGain); }", "var h = glsl.host(\"f\", material);",
        "mat3() is not supported on the host")]
    [InlineData("function f(p: vec3) -> float { return p.x + EPSILON; }", "var h = glsl.host(\"f\", material);",
        "const 'EPSILON' is declared only in a glsl.compile consts string")]
    [InlineData("function f(p: vec3) -> float { return p.x * uCount; }", "var h = glsl.host(\"f\", material);",
        "'*' mixes float and int")]
    [InlineData("function f(p: vec3) -> float { return texelFetch(uGain, ivec2(0, 0), 0).x; }", "var h = glsl.host(\"f\", material);",
        "no overload of texelFetch() takes (float, ivec2, int)")]
    [InlineData("function f(p: vec3) -> float { return p.x; }", "var h = glsl.host(\"g\", material);",
        "glsl.host (", "references 'g', which is not an @shader() function in this file")]
    [InlineData("function f(p: vec3) -> float { return p.x; }", "var h = glsl.host(name, material);",
        "glsl.host (", "expects the name of an @shader() function as a string literal, and a material")]
    [InlineData("function f(p: vec3) -> float { return p.x; }", "var h = glsl.host(\"f\");",
        "glsl.host (", "expects the name of an @shader() function as a string literal, and a material")]
    public void GlslHost_RejectsWhatABoundFunctionCannotUse(string declaration, string hostCode, string expected, string? alsoExpected = null)
    {
        var source = "@shader()\n" + declaration + """

            var fragment = glsl.compile({
                varyings: ["vec2 vUv"],
                uniforms: ["float uGain", "int uCount", "sampler2D uMap", "float uTwice"],
                consts: ["float EPSILON = 0.0015"],
                functions: ["f"]
            });
            var second = glsl.compile({ uniforms: ["vec3 uTwice"], functions: ["f"] });
            var name = "f";
            var material = three.createShaderMaterial({ "vertexShader": fragment, "fragmentShader": second, "uniforms": {} });

            """ + hostCode;

        var ex = Assert.ThrowsAny<Exception>(() => new Compiler.Compiler().TranspileToJavaScriptFromSource(source));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        if (alsoExpected != null)
            Assert.Contains(alsoExpected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GlslHost_ErrorInACallee_NamesTheBoundFunctionThatReachesIt()
    {
        var ex = Assert.ThrowsAny<Exception>(() => AnalyzeHost("""
            @shader()
            function fog(p: vec3) -> float {
                return length(p) * vDepth;
            }

            @shader()
            function shade(p: vec3) -> float {
                return 1.0 - fog(p) * uDensity;
            }

            var fragment = glsl.compile({ varyings: ["float vDepth"], uniforms: ["float uDensity"], functions: ["fog", "shade"] });
            var shadeAt = glsl.host("shade", material);
            """));

        Assert.Contains("@shader() function 'fog' is reached from host code through 'shade' (3:", ex.Message, StringComparison.Ordinal);
        Assert.Contains("varying 'vDepth' has no host meaning", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("vec3 uCamPos", "uCamPos", "vec3")]
    [InlineData("uniform highp sampler2D uProgram", "uProgram", "sampler2D")]
    [InlineData("vec3 uCars[CAR_COUNT]", "uCars", "vec3[]")]
    [InlineData("float uAngles[8];", "uAngles", "float[8]")]
    [InlineData("int uCount", "uCount", "int")]
    [InlineData("float EPSILON = 0.0015", "EPSILON", "float")]
    public void TryParseDeclaration_ReadsNameAndType(string declaration, string name, string type)
    {
        Assert.True(GlslTranspiler.TryParseDeclaration(declaration, out var parsedName, out var parsedType));
        Assert.Equal(name, parsedName);
        Assert.Equal(type, parsedType.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("vec3")]
    [InlineData("void nothing")]
    [InlineData("float uOpen[8")]
    public void TryParseDeclaration_RejectsWhatIsNotADeclaration(string declaration)
    {
        Assert.False(GlslTranspiler.TryParseDeclaration(declaration, out _, out _));
    }

    private static List<MaldaLang.Parser.AST.Statements.Statement> Parse(string source)
    {
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }
}
