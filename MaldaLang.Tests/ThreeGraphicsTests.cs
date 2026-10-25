// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.Text;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// <c>three.*</c> graphics (J5): geometry from buffers, picking, orbit controls, render targets and
/// readback, run in Node against the repository's three.js bundle with only the WebGL renderer
/// faked; and <c>glsl.source</c> / <c>glsl.functions</c> for shaders assembled at runtime.
/// </summary>
[Collection("Sequential")]
public class ThreeGraphicsTests
{
    /// <summary>A canvas and DOM to mount on, real three.js, and a WebGLRenderer that records calls.</summary>
    private const string Prelude = """
        const [runtimePath, threePath] = process.argv.slice(2);
        const real = require(threePath);
        const listeners = {};
        const canvas = {
          style: {},
          getBoundingClientRect() { return { left: 0, top: 0, width: 200, height: 100 }; },
          addEventListener(type, fn) { listeners[type] = fn; },
          removeEventListener(type) { delete listeners[type]; },
          setPointerCapture() {}, releasePointerCapture() {},
          toDataURL(type) { return "data:" + type + ";base64,AAAA"; }
        };
        const mount = { appendChild(el) { return el; }, removeChild() {} };
        globalThis.document = { body: mount, querySelector() { return mount; } };
        globalThis.window = { addEventListener() {}, removeEventListener() {}, requestAnimationFrame() { return 1; }, cancelAnimationFrame() {}, devicePixelRatio: 1 };
        function FakeRenderer() {
          this.domElement = canvas;
          this.target = null;
          this.size = new real.Vector2(200, 100);
          this.setSize = (w, h) => this.size.set(w, h);
          this.getSize = (v) => v.copy(this.size);
          this.setPixelRatio = () => {};
          this.setClearColor = (c, a) => { this.clearColor = [c, a]; };
          this.getClearColor = (c) => c.set(0x112233);
          this.getClearAlpha = () => 1;
          this.clear = () => {};
          this.getRenderTarget = () => this.target;
          this.setRenderTarget = (t) => { this.target = t; };
          this.render = (scene, camera) => { this.lastRender = { scene, camera, target: this.target }; };
          this.readRenderTargetPixels = () => {};
        }
        globalThis.THREE = Object.assign({}, real, { WebGLRenderer: FakeRenderer });
        require(runtimePath);
        const three = globalThis.mlRuntime.three;
        const check = (ok, what) => { if (!ok) throw new Error(what); };
        const throws = (fn, pattern, what) => {
          try { fn(); } catch (e) { check(pattern.test(e.message), what + ": " + e.message); return; }
          throw new Error(what + ": no error");
        };
        const renderer = three.createRenderer(200, 100, "#app");
        const scene = three.createScene();
        const camera = three.createPerspectiveCamera(60, 2, 0.1, 100);
        three.setPosition(camera, 0, 0, 5);
        // A 2x2 quad in the z = 0 plane, as two triangles.
        const positions = new Float32Array([-1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0]);
        const quad = three.createBufferGeometry({ position: positions, index: [0, 1, 2, 0, 2, 3] });
        const mesh = three.createMesh(quad, three.createStandardMaterial({}));
        three.add(scene, mesh);

        """;

    [Fact]
    public void Geometry_FromBuffers_UpdatesInPlace_AndBuildsLinesAndPoints()
    {
        RunScript("""
            check(quad.getAttribute("position").array === positions, "a float32 buffer is the attribute's own storage");
            check(Math.abs(quad.getAttribute("normal").array[2] - 1) < 1e-6, "normals computed when none are given");
            check(quad.index.array instanceof Uint32Array && quad.index.count === 6, "index");
            check(quad.boundingSphere && quad.boundingBox, "bounds computed");

            const copied = three.createBufferGeometry({ position: new Float64Array([0, 0, 0, 1, 0, 0, 0, 1, 0]) });
            check(copied.getAttribute("position").array instanceof Float32Array, "float64 is copied to float32");
            check(three.getAttribute(copied, "position") === copied.getAttribute("position").array, "getAttribute gives the storage");
            check(three.getAttribute(copied, "uv") === null, "missing attribute is null");

            positions[2] = -3; positions[5] = -3; positions[8] = -3; positions[11] = -3;
            three.updateGeometry(quad, "position");
            check(quad.getAttribute("position").version > 0 && Math.abs(quad.boundingBox.min.z + 3) < 1e-6, "in-place update flags and rebounds");
            three.updateGeometry(quad, "position", [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0]);
            check(quad.getAttribute("position").array === positions && positions[2] === 0, "same length copies into the storage");
            three.updateGeometry(quad, "position", [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 2, 2, 0]);
            check(quad.getAttribute("position").count === 5, "another length replaces the attribute");
            three.updateGeometry(quad, "index", [0, 1, 4]);
            check(quad.index.count === 3, "new index");

            throws(() => three.createBufferGeometry({ position: [1, 2] }), /has 2 numbers, not a multiple of 3/, "position length");
            throws(() => three.createBufferGeometry({ position: [0, 0, 0], index: [0, 1] }), /index 1 is 1, outside the 1 vertices/, "index range");
            throws(() => three.createBufferGeometry({ normal: [0, 0, 1] }), /requires a "position" buffer/, "position required");
            throws(() => three.updateGeometry(quad, "uv"), /has no 'uv' attribute/, "update a missing attribute");
            const custom = three.createBufferGeometry({ position: [0, 0, 0], aHeat: [0.5] }, { computeNormals: false, dynamic: true });
            check(custom.getAttribute("aHeat").itemSize === 1 && !custom.getAttribute("normal"), "custom attribute, no normals");
            check(custom.getAttribute("position").usage === real.DynamicDrawUsage, "dynamic usage");

            const lineGeometry = three.createBufferGeometry({ position: [0, 0, 0, 1, 0, 0, 1, 1, 0], color: [1, 0, 0, 0, 1, 0, 0, 0, 1] }, { computeNormals: false });
            const segments = three.createLines(lineGeometry);
            check(segments.isLineSegments && segments.material.vertexColors === true, "segments use the color buffer");
            const strip = three.createLines(lineGeometry, { strip: true, color: "#ff0000", vertexColors: false, opacity: 0.5 });
            check(strip.isLine && !strip.isLineSegments && strip.material.transparent && strip.material.color.getHexString() === "ff0000", "line strip");
            const points = three.createPoints(lineGeometry, { size: 4, sizeAttenuation: false });
            check(points.isPoints && points.material.size === 4 && points.material.sizeAttenuation === false, "points");
            three.setDrawRange(lineGeometry, 1, 2);
            check(lineGeometry.drawRange.start === 1 && lineGeometry.drawRange.count === 2, "draw range");
            three.setDrawRange(lineGeometry, 0, null);
            check(lineGeometry.drawRange.count === Infinity, "draw range to the end");
            """);
    }

    [Fact]
    public void Picking_RaycastsMeshesLinesPointsAndInstances()
    {
        RunScript("""
            const hit = three.pick(camera, scene, 100, 50);
            check(hit.object === mesh && Math.abs(hit.distance - 5) < 1e-6, "pick the renderer's center pixel");
            check(hit.point.join() === "0,0,0" && Math.abs(hit.normal[2] - 1) < 1e-6, "point and world normal");
            check(hit.faceIndex === 0 || hit.faceIndex === 1, "face index");
            check(hit.vertices.length === 3 && hit.index === null && hit.instanceId === null && hit.uv === null, "vertices, no index / instance / uv");
            check(three.pick(camera, scene, 2, 2) === null, "nothing under a corner");
            check(three.pick(camera, scene, 20, 10, { width: 40, height: 20 }).object === mesh, "pixel size option");

            positions[2] = -3; positions[5] = -3; positions[8] = -3; positions[11] = -3;
            three.updateGeometry(quad, "position");
            check(Math.abs(three.pick(camera, scene, 100, 50).distance - 8) < 1e-6, "an in-place update moves the hit");

            mesh.visible = false;
            check(three.pick(camera, scene, 100, 50) === null, "hidden objects are not picked");
            mesh.visible = true;

            const ray = three.raycast(scene, [0, 0, 10], [0, 0, -2]);
            check(Math.abs(ray.point[2] + 3) < 1e-6 && Math.abs(ray.distance - 13) < 1e-6, "world ray");
            check(three.raycast(scene, [0, 0, 10], [0, 0, -1], { far: 5 }) === null, "far limit");
            throws(() => three.raycast(scene, [0, 0, 0], [0, 0, 0]), /direction must not be zero/, "zero direction");

            const back = three.createMesh(three.createBufferGeometry({ position: [-1, -1, -6, 1, -1, -6, 0, 1, -6] }), three.createStandardMaterial({}));
            three.add(scene, back);
            const all = three.pick(camera, scene, 100, 50, { all: true });
            // The center ray runs along the quad's diagonal, so both of its triangles report a hit.
            check(all.length === 3 && all[0].object === mesh && all[2].object === back && all[0].distance <= all[2].distance, "all hits, nearest first");

            const cloud = three.createPoints(three.createBufferGeometry({ position: [0.5, 0.5, 1, 3, 3, 1] }, { computeNormals: false }));
            const cloudScene = three.createScene();
            three.add(cloudScene, cloud);
            const pointHit = three.raycast(cloudScene, [0.5, 0.52, 5], [0, 0, -1], { pointThreshold: 0.05 });
            check(pointHit.object === cloud && pointHit.index === 0 && pointHit.faceIndex === null, "point index");
            check(three.raycast(cloudScene, [0.5, 0.7, 5], [0, 0, -1], { pointThreshold: 0.05 }) === null, "point threshold");

            const boxes = new real.InstancedMesh(new real.BoxGeometry(1, 1, 1), new real.MeshStandardMaterial(), 3);
            [[-3, 0, 0], [0, 0, 0], [3, 0, 0]].forEach((p, i) => boxes.setMatrixAt(i, new real.Matrix4().makeTranslation(p[0], p[1], p[2])));
            const boxScene = three.createScene();
            three.add(boxScene, boxes);
            check(three.raycast(boxScene, [3, 0, 5], [0, 0, -1]).instanceId === 2, "instance id");
            throws(() => three.pick({}, scene, 0, 0), /expects a camera first/, "camera required");
            """);
    }

    [Fact]
    public void Controls_OrbitPanZoomFitAndSwitchProjection()
    {
        RunScript("""
            const controls = three.createOrbitControls(camera, { target: [0, 0, 0], minDistance: 1, maxDistance: 50 });
            check(Math.abs(camera.position.z - 5) < 1e-6 && three.getControlsCamera(controls) === camera, "the camera stays where it was");
            check(typeof listeners.pointerdown === "function" && typeof listeners.wheel === "function", "listeners on the canvas");

            three.orbitControls(controls, Math.PI / 2, 0);
            check(Math.abs(camera.position.x - 5) < 1e-6 && Math.abs(camera.position.z) < 1e-6, "orbit a quarter turn");
            three.zoomControls(controls, 2);
            check(Math.abs(camera.position.length() - 2.5) < 1e-6, "zoom 2 halves the distance");
            three.zoomControls(controls, 100);
            check(Math.abs(camera.position.length() - 1) < 1e-6, "min distance");
            three.zoomControls(controls, 0.4);
            three.panControls(controls, 0, 1);
            check(Math.abs(controls.target.y - 1) < 1e-6 && Math.abs(camera.position.y - 1) < 1e-6, "pan moves camera and target");
            three.orbitControls(controls, 0, -10);
            check(Math.abs(camera.position.distanceTo(controls.target) - 2.5) < 1e-6 && camera.position.y > 1, "polar angle clamps, distance kept");
            three.setControlsTarget(controls, 0, 0, 0);
            check(controls.target.length() === 0, "new target");

            const distance = () => camera.position.distanceTo(controls.target);
            const start = distance();
            listeners.pointerdown({ pointerId: 1, button: 0, clientX: 100, clientY: 50 });
            check(!three.controlsMoved(controls), "a press alone has not moved");
            listeners.pointermove({ pointerId: 1, clientX: 120, clientY: 50 });
            listeners.pointerup({ pointerId: 1 });
            check(three.controlsMoved(controls) && Math.abs(distance() - start) < 1e-6, "drag orbits at the same distance");
            const targetBefore = controls.target.clone();
            listeners.pointerdown({ pointerId: 1, button: 2, clientX: 100, clientY: 50 });
            listeners.pointermove({ pointerId: 1, clientX: 140, clientY: 50 });
            listeners.pointerup({ pointerId: 1 });
            check(controls.target.distanceTo(targetBefore) > 0.1, "right drag pans");
            let prevented = false;
            listeners.wheel({ deltaY: -100, preventDefault() { prevented = true; } });
            check(prevented && distance() < start, "wheel up zooms in and keeps the page still");
            const pinchStart = distance();
            listeners.pointerdown({ pointerId: 1, button: 0, clientX: 90, clientY: 50 });
            listeners.pointerdown({ pointerId: 2, button: 0, clientX: 110, clientY: 50 });
            listeners.pointermove({ pointerId: 2, clientX: 130, clientY: 50 });
            listeners.pointerup({ pointerId: 1 }); listeners.pointerup({ pointerId: 2 });
            check(distance() < pinchStart, "spreading two fingers zooms in");

            three.setControlsEnabled(controls, false);
            const frozen = camera.position.clone();
            listeners.pointerdown({ pointerId: 1, button: 0, clientX: 100, clientY: 50 });
            listeners.pointermove({ pointerId: 1, clientX: 180, clientY: 50 });
            check(camera.position.equals(frozen), "disabled controls ignore the pointer");
            three.setControlsEnabled(controls, true);

            const far = three.createMesh(new real.BoxGeometry(4, 4, 4), three.createStandardMaterial({}));
            three.setPosition(far, 10, 0, 0);
            three.fitToBounds(controls, far, 1);
            const radius = Math.sqrt(12);
            check(controls.target.distanceTo(new real.Vector3(10, 0, 0)) < 1e-6, "fit targets the center");
            check(Math.abs(distance() - Math.min(50, radius / Math.sin(Math.PI / 6))) < 1e-6, "fit distance from the bounding sphere");

            const ortho = three.setProjection(controls, "orthographic");
            check(ortho.isOrthographicCamera && three.getControlsCamera(controls) === ortho, "orthographic");
            const visible = (ortho.top - ortho.bottom) / ortho.zoom;
            check(Math.abs(visible - 2 * distance() * Math.tan(Math.PI / 6)) < 1e-6, "same size at the target");
            check(three.setProjection(controls, "orthographic") === ortho, "switching to the current kind is a no-op");
            three.zoomControls(controls, 2);
            check(ortho.zoom === 2, "orthographic zoom");
            const backToPerspective = three.setProjection(controls, "perspective");
            check(backToPerspective === camera && Math.abs(2 * distance() * Math.tan(Math.PI / 6) - visible / 2) < 1e-6, "back to perspective at the zoomed size");
            throws(() => three.setProjection(controls, "fisheye"), /must be "perspective" or "orthographic"/, "kind");

            const bare = three.createPerspectiveCamera(60, 1, 0.1, 10);
            three.fitToBounds(bare, far);
            check(bare.far > 10 && Math.abs(bare.position.x - 10) < 1e-6, "fit a bare camera along its view");
            three.disposeControls(controls);
            check(listeners.pointerdown === undefined, "dispose removes listeners");
            throws(() => three.orbitControls({}, 0, 0), /expects controls made by three.createOrbitControls/, "controls required");
            """);
    }

    [Fact]
    public void RenderTargets_ReadPixelsAndIdBufferPicking()
    {
        RunScript("""
            const target = three.createRenderTarget(4, 2);
            check(target.width === 4 && target.texture.colorSpace === real.SRGBColorSpace, "target is sRGB like the canvas");
            check(three.createRenderTarget(1, 1, { linear: true }).texture.colorSpace !== real.SRGBColorSpace, "linear option");
            three.renderToTarget(renderer, target, scene, camera);
            check(renderer.lastRender.target === target && renderer.getRenderTarget() === null, "draws into the target, then back to the canvas");
            check(three.getTargetTexture(target) === target.texture, "target texture");

            // WebGL rows come bottom first: row value = 10 + bottom-up row number.
            renderer.readRenderTargetPixels = (t, x, y, w, h, out) => {
              for (let i = 0; i < w * h; i++) { out[i * 4] = 10 + y + Math.floor(i / w); out[i * 4 + 1] = x + (i % w); out[i * 4 + 3] = 255; }
            };
            const all = three.readPixels(renderer, target);
            check(all.length === 32 && all[0] === 11 && all[16] === 10, "rows top first");
            const one = three.readPixels(renderer, target, 3, 0, 1, 1);
            check(one.length === 4 && one[0] === 11 && one[1] === 3, "a pixel of the top row");
            throws(() => three.readPixels(renderer, target, 3, 0, 2, 1), /rectangle 3,0 2x1 is outside the 4x2 target/, "bounds");
            three.setTargetSize(target, 8, 8);
            check(target.width === 8, "resize");
            check(three.screenshot(renderer, scene, camera, "image/jpeg") === "data:image/jpeg;base64,AAAA" && renderer.lastRender.target === null, "screenshot renders to the canvas");

            const boxes = new real.InstancedMesh(new real.BoxGeometry(), new real.MeshStandardMaterial(), 5);
            three.add(scene, boxes);
            const hidden = three.createMesh(new real.BoxGeometry(), three.createStandardMaterial({}));
            hidden.visible = false;
            three.add(scene, hidden);
            const originalMaterial = mesh.material;
            let seen = null;
            const answer = (id) => {
              renderer.readRenderTargetPixels = (t, x, y, w, h, out) => {
                seen = { mesh: mesh.material, boxes: boxes.instanceColor, view: Object.assign({}, camera.view), clear: renderer.clearColor };
                out[0] = (id >> 16) & 255; out[1] = (id >> 8) & 255; out[2] = id & 255; out[3] = 255;
              };
            };
            answer(1);
            const first = three.pickId(renderer, camera, scene, 100, 50);
            check(first.object === mesh && first.instanceId === null, "id 1 is the first object");
            check(seen.mesh.isMeshBasicMaterial && seen.mesh.color.b * 255 === 1 && seen.mesh.color.r === 0 && seen.mesh.toneMapped === false, "flat id color");
            check(seen.view.enabled && seen.view.offsetX === 100 && seen.view.offsetY === 50 && seen.view.width === 1, "1x1 view at the pixel");
            check(seen.clear[0] === 0 && seen.boxes.count === 5, "black clear, id colors per instance");
            check(mesh.material === originalMaterial && boxes.instanceColor === null && camera.view.enabled === false, "everything restored");
            check(renderer.clearColor[0].getHex() === 0x112233, "clear color restored");
            answer(4);
            const box = three.pickId(renderer, camera, scene, 0, 0);
            check(box.object === boxes && box.instanceId === 2, "instance id from the id range");
            answer(0);
            check(three.pickId(renderer, camera, scene, 0, 0) === null, "background is nothing");
            answer(7);
            check(three.pickId(renderer, camera, scene, 0, 0) === null, "hidden objects get no id");
            """);
    }

    [Fact]
    public void GlslSource_GivesShaderFunctionsAsRuntimeStrings()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            @shader()
            function wave(x: float) -> float {
                return sin(x * 3.0);
            }

            @shader()
            function tint(c: vec3) -> vec3 {
                return c * 0.5;
            }

            var level = 2;
            var shader = glsl.source("wave", "tint") + "void main() { gl_FragColor = vec4(tint(vec3(wave(" + string(level) + ".0))), 1.0); }";
            var library = glsl.functions();
            io.print(shader);
            io.print(library.keys());
            io.print(library["wave"]);
            """);

        var output = Tier0Run(js);

        Assert.Equal(
            "float wave(float x) {\n    return sin((x * 3.0));\n}\n\nvec3 tint(vec3 c) {\n    return (c * 0.5);\n}\nvoid main() { gl_FragColor = vec4(tint(vec3(wave(2.0))), 1.0); }\n"
            + "[tint, wave]\n"
            + "float wave(float x) {\n    return sin((x * 3.0));\n}",
            output);
    }

    [Theory]
    [InlineData("glsl.source(\"missing\")", "glsl.source references 'missing', which is not an @shader() function in this file.")]
    [InlineData("glsl.source()", "glsl.source expects the names of @shader() functions")]
    [InlineData("glsl.functions(1)", "glsl.functions() takes no arguments.")]
    [InlineData("glsl.nope()", "Unknown glsl helper 'glsl.nope'")]
    public void GlslHelpers_RejectBadArguments(string call, string message)
    {
        var error = Assert.ThrowsAny<Exception>(() => new Compiler.Compiler().TranspileToJavaScriptFromSource($"var s = {call};"));
        Assert.Contains(message, error.ToString());
    }

    [Fact]
    public void TerrainExample_CompilesToJavaScript()
    {
        var path = PlanningPaths.ResolveRepoFile("Examples", "Games", "three_terrain.malda");
        var js = new Compiler.Compiler().TranspileToJavaScript(path);

        foreach (var api in new[] { "createBufferGeometry", "updateGeometry", "createLines", "createPoints", "pick(", "pickId(", "createOrbitControls", "fitToBounds", "setProjection", "createRenderTarget", "renderToTarget", "readPixels" })
            Assert.Contains("mlRuntime.three." + api, js);
        Assert.Contains("float ripple(vec2 uv, float time)", js);
    }

    private static string Tier0Run(string js) =>
        Conformance.Tier0.Tier0JavaScriptRunner.RunCompiledJavaScriptAsync(js).GetAwaiter().GetResult().Replace("\r", string.Empty).TrimEnd('\n');

    private static void RunScript(string body)
    {
        InterpretJsPair.SkipIfJavaScriptUnavailable();
        var directory = Path.Combine(Path.GetTempPath(), "malda_three_j5", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var scriptPath = Path.Combine(directory, "test.js");
            File.WriteAllText(scriptPath, Prelude + body + "\nprocess.stdout.write(\"ok\\n\");\n");
            var node = Environment.GetEnvironmentVariable("MALDA_NODE_PATH");
            var startInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(node) ? "node" : node,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js"));
            startInfo.ArgumentList.Add(PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "vendor", "three.min.js"));
            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(30_000), "Node timed out.");
            // three.min.js warns on stderr that the UMD build is deprecated; only the exit code matters.
            Assert.True(process.ExitCode == 0, stderr.Result);
            Assert.Equal("ok", stdout.Result.Trim());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Compile_CopiesThreeBundle_AndLoadsItBeforeTheRuntime()
    {
        var folder = Path.Combine(Path.GetTempPath(), "malda_three_host", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "app.malda");
            File.WriteAllText(source, """
                var renderer = three.createRenderer(320, 180, "#app");
                """);
            var result = new Compiler.Compiler().CompileToJavaScript(source, Path.Combine(folder, "out", "app.js"));
            Assert.True(result.Success, result.ErrorMessage);

            var output = Path.Combine(folder, "out");
            var vendor = PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "vendor", "three.min.js");
            Assert.Equal(File.ReadAllText(vendor), File.ReadAllText(Path.Combine(output, "three.min.js")));
            var html = File.ReadAllText(Path.Combine(output, "index.html"));
            Assert.Contains("<script src=\"./three.min.js\"></script>", html, StringComparison.Ordinal);
            Assert.True(html.IndexOf("./three.min.js", StringComparison.Ordinal) < html.IndexOf("./malda-js-runtime.js", StringComparison.Ordinal));

            File.WriteAllText(source, "io.print(1);");
            var plain = Path.Combine(folder, "plain");
            Assert.True(new Compiler.Compiler().CompileToJavaScript(source, Path.Combine(plain, "app.js")).Success);
            Assert.False(File.Exists(Path.Combine(plain, "three.min.js")));
            Assert.DoesNotContain("three.min.js", File.ReadAllText(Path.Combine(plain, "index.html")), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }
}
