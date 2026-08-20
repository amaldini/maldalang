// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Compiler;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
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

    private static List<MaldaLang.Parser.AST.Statements.Statement> Parse(string source)
    {
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }
}
