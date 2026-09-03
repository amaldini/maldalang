const MaldaApp = (() => {
    if (typeof globalThis.mlRuntime === "undefined") {
        throw new Error("mlRuntime is not available. Include malda-js-runtime.js before running generated MALDA JavaScript.");
    }
    const mlRuntime = globalThis.mlRuntime;

    function main() {
        let vertexShader = "varying vec2 vUv;\n\nvoid main() {\n    vUv = uv;\n    gl_Position = vec4(position.xy, 0.0, 1.0);\n}\n";
        let fragmentShader = "varying vec2 vUv;\nuniform vec2 uResolution;\nuniform vec3 uCamPos;\nuniform vec3 uCamForward;\nuniform vec3 uCamRight;\nuniform vec3 uCamUp;\nuniform vec3 uLightPos;\nuniform float uTanHalf;\nuniform float uAspect;\nuniform vec3 uBall0;\nuniform vec3 uBall1;\nuniform vec3 uBall2;\nuniform vec3 uBall3;\nuniform vec3 uBall4;\nuniform vec3 uBall5;\nuniform vec3 uBall6;\nuniform vec3 uBall7;\nuniform vec3 uBall8;\nuniform vec3 uBall9;\nuniform vec3 uBall10;\nuniform vec3 uBall11;\nuniform vec3 uBall12;\nuniform vec3 uBall13;\nuniform vec3 uBall14;\nuniform vec3 uBall15;\nuniform vec3 uCueCenter;\nuniform vec3 uCueHalf;\nuniform float uCueAngle;\nuniform float uCueOn;\nuniform vec3 uAimDir;\nuniform vec3 uAimMark;\nuniform float uAimOn;\nconst float EPSILON = 0.0015;\nconst float BALL_R = 0.075;\nconst float TABLE_HX = 1.85;\nconst float TABLE_HZ = 0.925;\nconst float RAIL_T = 0.13;\nconst float POCKET_R = 0.12;\n\nfloat hitSphere(vec3 center, float radius, vec3 origin, vec3 dir) {\n    vec3 oc = (origin - center);\n    float a = dot(dir, dir);\n    float b = dot(oc, dir);\n    float c = (dot(oc, oc) - (radius * radius));\n    float disc = ((b * b) - (a * c));\n    if ((disc < 0.0))\n    {\n        return (-1.0);\n    }\n    float root = sqrt(disc);\n    float t = (((-b) - root) / a);\n    if ((t > EPSILON))\n    {\n        return t;\n    }\n    t = (((-b) + root) / a);\n    if ((t > EPSILON))\n    {\n        return t;\n    }\n    return (-1.0);\n}\n\nfloat hitBox(vec3 bmin, vec3 bmax, vec3 origin, vec3 dir) {\n    vec3 inv = (vec3(1.0, 1.0, 1.0) / dir);\n    vec3 t0 = ((bmin - origin) * inv);\n    vec3 t1 = ((bmax - origin) * inv);\n    vec3 tmin = min(t0, t1);\n    vec3 tmax = max(t0, t1);\n    float tEnter = max(max(tmin.x, tmin.y), tmin.z);\n    float tExit = min(min(tmax.x, tmax.y), tmax.z);\n    if ((tExit < tEnter))\n    {\n        return (-1.0);\n    }\n    if ((tEnter > EPSILON))\n    {\n        return tEnter;\n    }\n    if ((tExit > EPSILON))\n    {\n        return tExit;\n    }\n    return (-1.0);\n}\n\nvec3 boxNormal(vec3 bmin, vec3 bmax, vec3 p) {\n    vec3 c = ((bmin + bmax) * 0.5);\n    vec3 d = ((p - c) / max((bmax - bmin), vec3(EPSILON, EPSILON, EPSILON)));\n    vec3 ad = abs(d);\n    if (((ad.x > ad.y) && (ad.x > ad.z)))\n    {\n        return vec3(sign(d.x), 0.0, 0.0);\n    }\n    if ((ad.y > ad.z))\n    {\n        return vec3(0.0, sign(d.y), 0.0);\n    }\n    return vec3(0.0, 0.0, sign(d.z));\n}\n\nfloat hitBoxRotated(vec3 center, vec3 halfSize, float angle, vec3 origin, vec3 dir) {\n    float ca = cos(angle);\n    float sa = sin(angle);\n    vec3 o = (origin - center);\n    vec3 oLocal = vec3(((ca * o.x) + (sa * o.z)), o.y, (((-sa) * o.x) + (ca * o.z)));\n    vec3 dLocal = vec3(((ca * dir.x) + (sa * dir.z)), dir.y, (((-sa) * dir.x) + (ca * dir.z)));\n    return hitBox((-halfSize), halfSize, oLocal, dLocal);\n}\n\nvec3 boxNormalRotated(vec3 center, vec3 halfSize, float angle, vec3 p) {\n    float ca = cos(angle);\n    float sa = sin(angle);\n    vec3 o = (p - center);\n    vec3 pLocal = vec3(((ca * o.x) + (sa * o.z)), o.y, (((-sa) * o.x) + (ca * o.z)));\n    vec3 nLocal = boxNormal((-halfSize), halfSize, pLocal);\n    return vec3(((ca * nLocal.x) - (sa * nLocal.z)), nLocal.y, ((sa * nLocal.x) + (ca * nLocal.z)));\n}\n\nfloat hitCylinder(vec3 center, float radius, float y0, float y1, vec3 origin, vec3 dir) {\n    float ocx = (origin.x - center.x);\n    float ocz = (origin.z - center.z);\n    float a = ((dir.x * dir.x) + (dir.z * dir.z));\n    float b = ((ocx * dir.x) + (ocz * dir.z));\n    float c = (((ocx * ocx) + (ocz * ocz)) - (radius * radius));\n    float tBest = (-1.0);\n    if ((abs(a) > EPSILON))\n    {\n        float disc = ((b * b) - (a * c));\n        if ((disc >= 0.0))\n        {\n            float root = sqrt(disc);\n            float t = (((-b) - root) / a);\n            float y = (origin.y + (dir.y * t));\n            if ((((t > EPSILON) && (y >= y0)) && (y <= y1)))\n            {\n                tBest = t;\n            }\n            t = (((-b) + root) / a);\n            y = (origin.y + (dir.y * t));\n            if (((((t > EPSILON) && (y >= y0)) && (y <= y1)) && ((tBest < 0.0) || (t < tBest))))\n            {\n                tBest = t;\n            }\n        }\n    }\n    if ((abs(dir.y) > EPSILON))\n    {\n        float tCap = ((y0 - origin.y) / dir.y);\n        float qx = ((origin.x + (dir.x * tCap)) - center.x);\n        float qz = ((origin.z + (dir.z * tCap)) - center.z);\n        if ((((tCap > EPSILON) && (((qx * qx) + (qz * qz)) <= (radius * radius))) && ((tBest < 0.0) || (tCap < tBest))))\n        {\n            tBest = tCap;\n        }\n        tCap = ((y1 - origin.y) / dir.y);\n        qx = ((origin.x + (dir.x * tCap)) - center.x);\n        qz = ((origin.z + (dir.z * tCap)) - center.z);\n        if ((((tCap > EPSILON) && (((qx * qx) + (qz * qz)) <= (radius * radius))) && ((tBest < 0.0) || (tCap < tBest))))\n        {\n            tBest = tCap;\n        }\n    }\n    return tBest;\n}\n\nvec3 cylinderNormal(vec3 center, float radius, float y0, float y1, vec3 p) {\n    vec3 radial = vec3((p.x - center.x), 0.0, (p.z - center.z));\n    float dr = abs((length(radial) - radius));\n    float dy0 = abs((p.y - y0));\n    float dy1 = abs((p.y - y1));\n    if (((dy0 < dr) && (dy0 <= dy1)))\n    {\n        return vec3(0.0, (-1.0), 0.0);\n    }\n    if ((dy1 < dr))\n    {\n        return vec3(0.0, 1.0, 0.0);\n    }\n    return normalize(radial);\n}\n\nvec3 pocketCenter(int index) {\n    if ((index == 0))\n    {\n        return vec3((-TABLE_HX), 0.0, (-TABLE_HZ));\n    }\n    if ((index == 1))\n    {\n        return vec3((-TABLE_HX), 0.0, TABLE_HZ);\n    }\n    if ((index == 2))\n    {\n        return vec3(TABLE_HX, 0.0, (-TABLE_HZ));\n    }\n    if ((index == 3))\n    {\n        return vec3(TABLE_HX, 0.0, TABLE_HZ);\n    }\n    if ((index == 4))\n    {\n        return vec3(0.0, 0.0, (-TABLE_HZ));\n    }\n    return vec3(0.0, 0.0, TABLE_HZ);\n}\n\nbool inPocketXZ(vec3 p) {\n    int i = 0;\n    while ((i < 6))\n    {\n        vec3 c = pocketCenter(i);\n        float dx = (p.x - c.x);\n        float dz = (p.z - c.z);\n        if ((((dx * dx) + (dz * dz)) <= (POCKET_R * POCKET_R)))\n        {\n            return true;\n        }\n        i = (i + 1);\n    }\n    return false;\n}\n\nvec3 ballCenter(int index) {\n    if ((index == 0))\n    {\n        return uBall0;\n    }\n    if ((index == 1))\n    {\n        return uBall1;\n    }\n    if ((index == 2))\n    {\n        return uBall2;\n    }\n    if ((index == 3))\n    {\n        return uBall3;\n    }\n    if ((index == 4))\n    {\n        return uBall4;\n    }\n    if ((index == 5))\n    {\n        return uBall5;\n    }\n    if ((index == 6))\n    {\n        return uBall6;\n    }\n    if ((index == 7))\n    {\n        return uBall7;\n    }\n    if ((index == 8))\n    {\n        return uBall8;\n    }\n    if ((index == 9))\n    {\n        return uBall9;\n    }\n    if ((index == 10))\n    {\n        return uBall10;\n    }\n    if ((index == 11))\n    {\n        return uBall11;\n    }\n    if ((index == 12))\n    {\n        return uBall12;\n    }\n    if ((index == 13))\n    {\n        return uBall13;\n    }\n    if ((index == 14))\n    {\n        return uBall14;\n    }\n    return uBall15;\n}\n\nvec3 ballSolidColor(int index) {\n    if ((index == 0))\n    {\n        return vec3(0.93000000000000005, 0.93000000000000005, 0.90000000000000002);\n    }\n    int hue = index;\n    if ((index >= 9))\n    {\n        hue = (index - 8);\n    }\n    if ((hue == 8))\n    {\n        return vec3(0.050000000000000003, 0.050000000000000003, 0.059999999999999998);\n    }\n    if ((hue == 1))\n    {\n        return vec3(0.94999999999999996, 0.78000000000000003, 0.12);\n    }\n    if ((hue == 2))\n    {\n        return vec3(0.12, 0.28000000000000003, 0.81999999999999995);\n    }\n    if ((hue == 3))\n    {\n        return vec3(0.83999999999999997, 0.10000000000000001, 0.12);\n    }\n    if ((hue == 4))\n    {\n        return vec3(0.41999999999999998, 0.16, 0.62);\n    }\n    if ((hue == 5))\n    {\n        return vec3(0.92000000000000004, 0.41999999999999998, 0.080000000000000002);\n    }\n    if ((hue == 6))\n    {\n        return vec3(0.080000000000000002, 0.47999999999999998, 0.17999999999999999);\n    }\n    return vec3(0.52000000000000002, 0.080000000000000002, 0.16);\n}\n\nfloat sdBox2(vec2 p, vec2 halfSize) {\n    vec2 q = (abs(p) - halfSize);\n    return (length(max(q, vec2(0.0, 0.0))) + min(max(q.x, q.y), 0.0));\n}\n\nfloat digitSdf(vec2 p, int d) {\n    float a = sdBox2((p - vec2(0.0, 1.1200000000000001)), vec2(0.35999999999999999, 0.11));\n    float b = sdBox2((p - vec2(0.40000000000000002, 0.56000000000000005)), vec2(0.11, 0.38));\n    float c = sdBox2((p - vec2(0.40000000000000002, (-0.56000000000000005))), vec2(0.11, 0.38));\n    float e = sdBox2((p - vec2((-0.40000000000000002), (-0.56000000000000005))), vec2(0.11, 0.38));\n    float f = sdBox2((p - vec2((-0.40000000000000002), 0.56000000000000005)), vec2(0.11, 0.38));\n    float g = sdBox2((p - vec2(0.0, 0.0)), vec2(0.35999999999999999, 0.11));\n    float bottom = sdBox2((p - vec2(0.0, (-1.1200000000000001))), vec2(0.35999999999999999, 0.11));\n    float dist = 100.0;\n    if (((((((((d == 0) || (d == 2)) || (d == 3)) || (d == 5)) || (d == 6)) || (d == 7)) || (d == 8)) || (d == 9)))\n    {\n        dist = min(dist, a);\n    }\n    if (((((((((d == 0) || (d == 1)) || (d == 2)) || (d == 3)) || (d == 4)) || (d == 7)) || (d == 8)) || (d == 9)))\n    {\n        dist = min(dist, b);\n    }\n    if ((((((((((d == 0) || (d == 1)) || (d == 3)) || (d == 4)) || (d == 5)) || (d == 6)) || (d == 7)) || (d == 8)) || (d == 9)))\n    {\n        dist = min(dist, c);\n    }\n    if ((((((((d == 0) || (d == 2)) || (d == 3)) || (d == 5)) || (d == 6)) || (d == 8)) || (d == 9)))\n    {\n        dist = min(dist, bottom);\n    }\n    if (((((d == 0) || (d == 2)) || (d == 6)) || (d == 8)))\n    {\n        dist = min(dist, e);\n    }\n    if (((((((d == 0) || (d == 4)) || (d == 5)) || (d == 6)) || (d == 8)) || (d == 9)))\n    {\n        dist = min(dist, f);\n    }\n    if ((((((((d == 2) || (d == 3)) || (d == 4)) || (d == 5)) || (d == 6)) || (d == 8)) || (d == 9)))\n    {\n        dist = min(dist, g);\n    }\n    return dist;\n}\n\nfloat numberSdf(vec2 p, int value) {\n    if ((value < 10))\n    {\n        return digitSdf((p * vec2(2.3500000000000001, 1.95)), value);\n    }\n    int ones = (value - 10);\n    float left = digitSdf(((p - vec2((-0.14000000000000001), 0.0)) * vec2(3.3999999999999999, 2.6000000000000001)), 1);\n    float right = digitSdf(((p - vec2(0.14999999999999999, 0.0)) * vec2(3.3999999999999999, 2.6000000000000001)), ones);\n    return min(left, right);\n}\n\nvec2 stampUv(vec3 normal, vec3 stampDir) {\n    vec3 n = normalize(stampDir);\n    float d = dot(normal, n);\n    if ((d < 0.40000000000000002))\n    {\n        return vec2(100.0, 100.0);\n    }\n    vec3 up = vec3(0.0, 1.0, 0.0);\n    if ((abs(dot(n, up)) > 0.92000000000000004))\n    {\n        up = vec3(1.0, 0.0, 0.0);\n    }\n    vec3 tangent = normalize(cross(up, n));\n    vec3 bitangent = cross(n, tangent);\n    vec3 offset = (normal - (n * d));\n    return vec2(dot(offset, tangent), dot(offset, bitangent));\n}\n\nvec3 applyBallNumber(vec3 base, int index, vec3 normal) {\n    if ((index <= 0))\n    {\n        return base;\n    }\n    vec3 toCam = normalize((uCamPos - ballCenter(index)));\n    vec2 uv = stampUv(normal, toCam);\n    float r = length(uv);\n    if ((r > 0.57999999999999996))\n    {\n        return base;\n    }\n    vec3 badge = vec3(0.95999999999999996, 0.95999999999999996, 0.93999999999999995);\n    vec3 ink = vec3(0.02, 0.02, 0.029999999999999999);\n    if ((r > 0.5))\n    {\n        return mix(badge, base, clamp(((r - 0.5) / 0.080000000000000002), 0.0, 1.0));\n    }\n    float dgt = numberSdf(uv, index);\n    if ((dgt < 0.0))\n    {\n        return ink;\n    }\n    if ((dgt < 0.012))\n    {\n        return mix(ink, badge, (dgt / 0.012));\n    }\n    return badge;\n}\n\nvec3 ballAlbedo(int index, vec3 normal) {\n    vec3 solid = ballSolidColor(index);\n    vec3 base = solid;\n    if (((index >= 9) && (abs(normal.y) > 0.41999999999999998)))\n    {\n        base = vec3(0.93000000000000005, 0.93000000000000005, 0.90000000000000002);\n    }\n    return applyBallNumber(base, index, normal);\n}\n\nbool considerHit(float t, float tHit) {\n    return ((t > 0.0) && (t < tHit));\n}\n\nbool closestHit(vec3 origin, vec3 dir, float tMax, out float tHit, out int kind, out int index, out int material, out vec3 normal, out vec3 albedo) {\n    tHit = tMax;\n    kind = 0;\n    index = (-1);\n    material = 0;\n    normal = vec3(0.0, 1.0, 0.0);\n    albedo = vec3(0.0);\n    int i = 0;\n    while ((i < 16))\n    {\n        vec3 center = ballCenter(i);\n        if ((center.y > 0.0))\n        {\n            float tBall = hitSphere(center, BALL_R, origin, dir);\n            if (considerHit(tBall, tHit))\n            {\n                tHit = tBall;\n                kind = 1;\n                index = i;\n            }\n        }\n        i = (i + 1);\n    }\n    if ((uCueOn > 0.5))\n    {\n        float tCue = hitBoxRotated(uCueCenter, uCueHalf, uCueAngle, origin, dir);\n        if (considerHit(tCue, tHit))\n        {\n            tHit = tCue;\n            kind = 7;\n            index = 0;\n        }\n    }\n    int p = 0;\n    while ((p < 6))\n    {\n        vec3 pocket = pocketCenter(p);\n        float tPocket = hitCylinder(pocket, POCKET_R, (-0.22), 0.01, origin, dir);\n        if (considerHit(tPocket, tHit))\n        {\n            tHit = tPocket;\n            kind = 4;\n            index = p;\n        }\n        p = (p + 1);\n    }\n    float railY0 = 0.0;\n    float railY1 = 0.11;\n    float gap = 0.16;\n    float tRail = hitBox(vec3(((-TABLE_HX) + gap), railY0, TABLE_HZ), vec3((-gap), railY1, (TABLE_HZ + RAIL_T)), origin, dir);\n    if (considerHit(tRail, tHit))\n    {\n        tHit = tRail;\n        kind = 3;\n        index = 0;\n    }\n    tRail = hitBox(vec3(gap, railY0, TABLE_HZ), vec3((TABLE_HX - gap), railY1, (TABLE_HZ + RAIL_T)), origin, dir);\n    if (considerHit(tRail, tHit))\n    {\n        tHit = tRail;\n        kind = 3;\n        index = 1;\n    }\n    tRail = hitBox(vec3(((-TABLE_HX) + gap), railY0, ((-TABLE_HZ) - RAIL_T)), vec3((-gap), railY1, (-TABLE_HZ)), origin, dir);\n    if (considerHit(tRail, tHit))\n    {\n        tHit = tRail;\n        kind = 3;\n        index = 2;\n    }\n    tRail = hitBox(vec3(gap, railY0, ((-TABLE_HZ) - RAIL_T)), vec3((TABLE_HX - gap), railY1, (-TABLE_HZ)), origin, dir);\n    if (considerHit(tRail, tHit))\n    {\n        tHit = tRail;\n        kind = 3;\n        index = 3;\n    }\n    tRail = hitBox(vec3(TABLE_HX, railY0, ((-TABLE_HZ) + gap)), vec3((TABLE_HX + RAIL_T), railY1, (TABLE_HZ - gap)), origin, dir);\n    if (considerHit(tRail, tHit))\n    {\n        tHit = tRail;\n        kind = 3;\n        index = 4;\n    }\n    tRail = hitBox(vec3(((-TABLE_HX) - RAIL_T), railY0, ((-TABLE_HZ) + gap)), vec3((-TABLE_HX), railY1, (TABLE_HZ - gap)), origin, dir);\n    if (considerHit(tRail, tHit))\n    {\n        tHit = tRail;\n        kind = 3;\n        index = 5;\n    }\n    vec3 bodyMin = vec3(((-TABLE_HX) - RAIL_T), (-0.41999999999999998), ((-TABLE_HZ) - RAIL_T));\n    vec3 bodyMax = vec3((TABLE_HX + RAIL_T), (-0.002), (TABLE_HZ + RAIL_T));\n    float tBody = hitBox(bodyMin, bodyMax, origin, dir);\n    if (considerHit(tBody, tHit))\n    {\n        vec3 bodyHit = (origin + (dir * tBody));\n        if ((!inPocketXZ(bodyHit)))\n        {\n            tHit = tBody;\n            kind = 5;\n            index = 0;\n        }\n    }\n    if ((abs(dir.y) > EPSILON))\n    {\n        float tFelt = ((0.0 - origin.y) / dir.y);\n        if (((tFelt > EPSILON) && (tFelt < tHit)))\n        {\n            vec3 feltHit = (origin + (dir * tFelt));\n            if ((((abs(feltHit.x) <= TABLE_HX) && (abs(feltHit.z) <= TABLE_HZ)) && (!inPocketXZ(feltHit))))\n            {\n                tHit = tFelt;\n                kind = 2;\n                index = (-1);\n            }\n        }\n        float tFloor = (((-0.71999999999999997) - origin.y) / dir.y);\n        if (((tFloor > EPSILON) && (tFloor < tHit)))\n        {\n            tHit = tFloor;\n            kind = 6;\n            index = (-1);\n        }\n    }\n    if ((kind == 0))\n    {\n        return false;\n    }\n    vec3 hit = (origin + (dir * tHit));\n    if ((kind == 1))\n    {\n        vec3 center = ballCenter(index);\n        normal = ((hit - center) / BALL_R);\n        albedo = ballAlbedo(index, normal);\n        material = 1;\n    }\n    else\n    {\n        if ((kind == 7))\n        {\n            normal = boxNormalRotated(uCueCenter, uCueHalf, uCueAngle, hit);\n            albedo = vec3(0.78000000000000003, 0.52000000000000002, 0.23999999999999999);\n            material = 0;\n        }\n        else\n        {\n            if ((kind == 3))\n            {\n                if ((index == 0))\n                {\n                    normal = boxNormal(vec3(((-TABLE_HX) + gap), railY0, TABLE_HZ), vec3((-gap), railY1, (TABLE_HZ + RAIL_T)), hit);\n                }\n                else\n                {\n                    if ((index == 1))\n                    {\n                        normal = boxNormal(vec3(gap, railY0, TABLE_HZ), vec3((TABLE_HX - gap), railY1, (TABLE_HZ + RAIL_T)), hit);\n                    }\n                    else\n                    {\n                        if ((index == 2))\n                        {\n                            normal = boxNormal(vec3(((-TABLE_HX) + gap), railY0, ((-TABLE_HZ) - RAIL_T)), vec3((-gap), railY1, (-TABLE_HZ)), hit);\n                        }\n                        else\n                        {\n                            if ((index == 3))\n                            {\n                                normal = boxNormal(vec3(gap, railY0, ((-TABLE_HZ) - RAIL_T)), vec3((TABLE_HX - gap), railY1, (-TABLE_HZ)), hit);\n                            }\n                            else\n                            {\n                                if ((index == 4))\n                                {\n                                    normal = boxNormal(vec3(TABLE_HX, railY0, ((-TABLE_HZ) + gap)), vec3((TABLE_HX + RAIL_T), railY1, (TABLE_HZ - gap)), hit);\n                                }\n                                else\n                                {\n                                    normal = boxNormal(vec3(((-TABLE_HX) - RAIL_T), railY0, ((-TABLE_HZ) + gap)), vec3((-TABLE_HX), railY1, (TABLE_HZ - gap)), hit);\n                                }\n                            }\n                        }\n                    }\n                }\n                albedo = vec3(0.38, 0.17999999999999999, 0.080000000000000002);\n                material = 0;\n            }\n            else\n            {\n                if ((kind == 4))\n                {\n                    normal = cylinderNormal(pocketCenter(index), POCKET_R, (-0.22), 0.01, hit);\n                    albedo = vec3(0.029999999999999999, 0.029999999999999999, 0.035000000000000003);\n                    material = 0;\n                }\n                else\n                {\n                    if ((kind == 5))\n                    {\n                        normal = boxNormal(bodyMin, bodyMax, hit);\n                        albedo = vec3(0.28000000000000003, 0.13, 0.059999999999999998);\n                        material = 0;\n                    }\n                    else\n                    {\n                        if ((kind == 6))\n                        {\n                            normal = vec3(0.0, 1.0, 0.0);\n                            if ((dot(normal, dir) > 0.0))\n                            {\n                                normal = (-normal);\n                            }\n                            float check = mod((floor((hit.x * 1.3999999999999999)) + floor((hit.z * 1.3999999999999999))), 2.0);\n                            if ((check < 0.5))\n                            {\n                                albedo = vec3(0.10000000000000001, 0.089999999999999997, 0.080000000000000002);\n                            }\n                            else\n                            {\n                                albedo = vec3(0.059999999999999998, 0.055, 0.050000000000000003);\n                            }\n                            material = 0;\n                        }\n                        else\n                        {\n                            normal = vec3(0.0, 1.0, 0.0);\n                            if ((dot(normal, dir) > 0.0))\n                            {\n                                normal = (-normal);\n                            }\n                            albedo = vec3(0.070000000000000007, 0.35999999999999999, 0.16);\n                            float hx = abs(hit.x);\n                            float hz = abs(hit.z);\n                            if ((mod((floor(((hit.x + TABLE_HX) * 2.0)) + floor(((hit.z + TABLE_HZ) * 2.0))), 14.0) < 0.5))\n                            {\n                                albedo = (albedo * 0.81999999999999995);\n                            }\n                            if (((abs((hit.x + 0.62)) < 0.0080000000000000002) && (hz < (TABLE_HZ - 0.080000000000000002))))\n                            {\n                                albedo = vec3(0.78000000000000003, 0.78000000000000003, 0.62);\n                            }\n                            if ((length(vec2(hit.x, hit.z)) < 0.035000000000000003))\n                            {\n                                albedo = vec3(0.78000000000000003, 0.78000000000000003, 0.62);\n                            }\n                            if (((hx > (TABLE_HX - 0.029999999999999999)) || (hz > (TABLE_HZ - 0.029999999999999999))))\n                            {\n                                albedo = (albedo * 0.55000000000000004);\n                            }\n                            if ((uAimOn > 0.5))\n                            {\n                                vec2 toP = vec2((hit.x - uBall0.x), (hit.z - uBall0.z));\n                                float along = ((toP.x * uAimDir.x) + (toP.y * uAimDir.z));\n                                float perp = abs(((toP.x * uAimDir.z) - (toP.y * uAimDir.x)));\n                                if ((((along > (BALL_R + 0.01)) && (along < 2.3500000000000001)) && (perp < 0.010999999999999999)))\n                                {\n                                    albedo = mix(albedo, vec3(0.93999999999999995, 0.85999999999999999, 0.28000000000000003), 0.62);\n                                }\n                                float markR = length(vec2((hit.x - uAimMark.x), (hit.z - uAimMark.z)));\n                                if (((markR < 0.050000000000000003) && (markR > 0.028000000000000001)))\n                                {\n                                    albedo = mix(albedo, vec3(0.94999999999999996, 0.90000000000000002, 0.40000000000000002), 0.75);\n                                }\n                            }\n                            material = 0;\n                        }\n                    }\n                }\n            }\n        }\n    }\n    normal = normalize(normal);\n    return true;\n}\n\nbool inShadow(vec3 hit, vec3 normal) {\n    vec3 origin = (hit + (normal * 0.02));\n    vec3 toLight = (uLightPos - origin);\n    float lightDist = length(toLight);\n    if ((lightDist <= EPSILON))\n    {\n        return false;\n    }\n    vec3 dir = (toLight / lightDist);\n    float tHit = 0.0;\n    int kind = 0;\n    int index = 0;\n    int material = 0;\n    vec3 n = vec3(0.0);\n    vec3 albedo = vec3(0.0);\n    return closestHit(origin, dir, (lightDist - 0.050000000000000003), tHit, kind, index, material, n, albedo);\n}\n\nvec3 localShade(vec3 hit, vec3 normal, vec3 albedo, vec3 viewDir, bool glossy) {\n    vec3 toLight = (uLightPos - hit);\n    vec3 lightDir = normalize(toLight);\n    float diff = max(dot(normal, lightDir), 0.0);\n    float light = 0.16;\n    if ((!inShadow(hit, normal)))\n    {\n        light = (0.16 + (diff * 0.83999999999999997));\n        if (glossy)\n        {\n            vec3 halfV = normalize((lightDir + viewDir));\n            float spec = pow(max(dot(normal, halfV), 0.0), 72.0);\n            return ((albedo * light) + (vec3(spec, spec, spec) * 0.55000000000000004));\n        }\n    }\n    return (albedo * light);\n}\n\nvec3 shadeSky(vec3 dir) {\n    float t = clamp(((dir.y + 1.0) * 0.5), 0.0, 1.0);\n    return mix(vec3(0.16, 0.12, 0.10000000000000001), vec3(0.050000000000000003, 0.059999999999999998, 0.080000000000000002), t);\n}\n\nvec3 shadePrimary(vec3 origin, vec3 dir) {\n    float tHit = 0.0;\n    int kind = 0;\n    int index = 0;\n    int material = 0;\n    vec3 normal = vec3(0.0);\n    vec3 albedo = vec3(0.0);\n    if ((!closestHit(origin, dir, 1000000.0, tHit, kind, index, material, normal, albedo)))\n    {\n        return shadeSky(dir);\n    }\n    vec3 hit = (origin + (dir * tHit));\n    vec3 viewDir = normalize((uCamPos - hit));\n    return localShade(hit, normal, albedo, viewDir, (material == 1));\n}\n\nvec3 traceScene(vec3 origin, vec3 dir) {\n    float tHit = 0.0;\n    int kind = 0;\n    int index = 0;\n    int material = 0;\n    vec3 normal = vec3(0.0);\n    vec3 albedo = vec3(0.0);\n    if ((!closestHit(origin, dir, 1000000.0, tHit, kind, index, material, normal, albedo)))\n    {\n        return shadeSky(dir);\n    }\n    vec3 hit = (origin + (dir * tHit));\n    vec3 viewDir = normalize((origin - hit));\n    vec3 local = localShade(hit, normal, albedo, viewDir, (material == 1));\n    if ((material != 1))\n    {\n        return local;\n    }\n    float fresnel = (0.080000000000000002 + (0.62 * pow((1.0 - max((-dot(dir, normal)), 0.0)), 5.0)));\n    vec3 reflDir = reflect(dir, normal);\n    vec3 bounce = shadePrimary((hit + (normal * 0.02)), reflDir);\n    return mix(local, bounce, fresnel);\n}\n\nvoid main() {\n    vec2 ndc = ((vUv * 2.0) - 1.0);\n    vec3 dir = normalize(((uCamForward + (uCamRight * ((ndc.x * uAspect) * uTanHalf))) + (uCamUp * (ndc.y * uTanHalf))));\n    vec3 color = traceScene(uCamPos, dir);\n    gl_FragColor = vec4(sqrt(color), 1.0);\n}\n";
        let root = mlRuntime.dom.query("#app");
        if (mlRuntime.isTruthy(mlRuntime.equals(root, null)))
        {
            mlRuntime.builtins.println("No #app container found.");
        }
        else
        {
            mlRuntime.dom.clear(root);
            let heading = mlRuntime.dom.create("h1");
            mlRuntime.dom.setText(heading, "MALDA GPU Billiards");
            mlRuntime.dom.append(root, heading);
            let hint = mlRuntime.dom.create("p");
            mlRuntime.dom.setText(hint, "The cue stays locked on the white ball. Move the mouse around the cue ball to aim (A/D for fine aim). Hold click or Space on the table to charge, release to shoot. R reracks. Arrows orbit, C or Stop camera zeros orbit speed, [ ] or +/− zoom. Sliders set cushion e, ball e, and felt friction. Host MALDA steps 2D circle physics; the kernel only traces the uniforms.");
            mlRuntime.dom.append(root, hint);
            let status = mlRuntime.dom.create("p");
            mlRuntime.dom.setText(status, "Move the mouse around the white ball, then hold click/Space to charge.");
            mlRuntime.dom.append(root, status);
            let powerLine = mlRuntime.dom.create("p");
            mlRuntime.dom.setText(powerLine, "Power  [------------]   0%");
            mlRuntime.dom.append(root, powerLine);
            let controls = mlRuntime.dom.create("div");
            mlRuntime.dom.html(controls, "<p><button type='button' id='stopOrbitBtn'>Stop camera</button> Zoom <input id='zoomRange' type='range' min='24' max='80' step='1' value='48'> Cushion e <input id='railRange' type='range' min='5' max='100' step='1' value='68'> Ball e <input id='ballRange' type='range' min='5' max='100' step='1' value='92'> Felt friction <input id='frictionRange' type='range' min='5' max='400' step='5' value='165'></p>");
            mlRuntime.dom.append(root, controls);
            let physLine = mlRuntime.dom.create("p");
            mlRuntime.dom.setText(physLine, "Zoom 4.8   Cushion e 0.68   Ball e 0.92   Friction 1.65   Orbit 0.18");
            mlRuntime.dom.append(root, physLine);
            let stopOrbitBtn = mlRuntime.dom.query("#stopOrbitBtn");
            let zoomRange = mlRuntime.dom.query("#zoomRange");
            let railRange = mlRuntime.dom.query("#railRange");
            let ballRange = mlRuntime.dom.query("#ballRange");
            let frictionRange = mlRuntime.dom.query("#frictionRange");
            let width = 960;
            let height = 540;
            let aspect = (mlRuntime.coerceToFloat(width) / mlRuntime.coerceToFloat(height));
            let tanHalf = mlRuntime.math.tan(mlRuntime.math.degToRad(26));
            let camRadius = 4.85;
            let camAngle = 0.72;
            let camSpeed = 0.18;
            let camX = 0;
            let camY = 3.05;
            let camZ = 0;
            let forwardX = 0;
            let forwardY = 0;
            let forwardZ = 1;
            let rightX = 1;
            let rightY = 0;
            let rightZ = 0;
            let upX = 0;
            let upY = 1;
            let upZ = 0;
            let lightX = 0.35;
            let lightY = 3.8;
            let lightZ = 0.55;
            let lastMouseX = (-mlRuntime.coerceToFloat(1));
            let lastMouseY = (-mlRuntime.coerceToFloat(1));
            let tableHX = 1.85;
            let tableHZ = 0.925;
            let ballR = 0.075;
            let pocketR = 0.12;
            let ballX = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            let ballZ = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            let ballVx = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            let ballVz = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            let ballLive = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1];
            let aimAngle = 0;
            let shotPower = 0.12;
            let charging = false;
            let aimMarkX = (-mlRuntime.coerceToFloat(1.7));
            let aimMarkZ = 0;
            let aimMarkValid = false;
            let rWasDown = false;
            let cWasDown = false;
            let pottedCount = 0;
            let railRest = 0.68;
            let ballRest = 0.92;
            let feltFriction = 1.65;
            let zoomAcc = 0;
            function rangeInt(el, fallback) {
                if (mlRuntime.isTruthy(mlRuntime.equals(el, null)))
                {
                    return fallback;
                }
                return mlRuntime.coerceToInt(el.value);
            }
            function setRangeInt(el, n, lo, hi) {
                if (mlRuntime.isTruthy(mlRuntime.equals(el, null)))
                {
                    return;
                }
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(n) < mlRuntime.coerceToFloat(lo))))
                {
                    n = lo;
                }
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(n) > mlRuntime.coerceToFloat(hi))))
                {
                    n = hi;
                }
                el.value = mlRuntime.coerceToString(mlRuntime.coerceToInt(n));
            }
            function nudgeRange(el, delta, lo, hi) {
                setRangeInt(el, (rangeInt(el, lo) + delta), lo, hi);
            }
            function syncPlaySettings() {
                camRadius = (mlRuntime.coerceToFloat(rangeInt(zoomRange, 48)) / mlRuntime.coerceToFloat(10));
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(camRadius) < mlRuntime.coerceToFloat(2.4))))
                {
                    camRadius = 2.4;
                }
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(camRadius) > mlRuntime.coerceToFloat(8))))
                {
                    camRadius = 8;
                }
                railRest = (mlRuntime.coerceToFloat(rangeInt(railRange, 68)) / mlRuntime.coerceToFloat(100));
                ballRest = (mlRuntime.coerceToFloat(rangeInt(ballRange, 92)) / mlRuntime.coerceToFloat(100));
                feltFriction = (mlRuntime.coerceToFloat(rangeInt(frictionRange, 165)) / mlRuntime.coerceToFloat(100));
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(railRest) < mlRuntime.coerceToFloat(0.05))))
                {
                    railRest = 0.05;
                }
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(railRest) > mlRuntime.coerceToFloat(1))))
                {
                    railRest = 1;
                }
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(ballRest) < mlRuntime.coerceToFloat(0.05))))
                {
                    ballRest = 0.05;
                }
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(ballRest) > mlRuntime.coerceToFloat(1))))
                {
                    ballRest = 1;
                }
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(feltFriction) < mlRuntime.coerceToFloat(0.05))))
                {
                    feltFriction = 0.05;
                }
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(feltFriction) > mlRuntime.coerceToFloat(4))))
                {
                    feltFriction = 4;
                }
            }
            function onStopOrbit() {
                camSpeed = 0;
            }
            if (mlRuntime.isTruthy((!mlRuntime.equals(stopOrbitBtn, null))))
            {
                mlRuntime.dom.on(stopOrbitBtn, "click", onStopOrbit);
            }
            function rackX(index) {
                if (mlRuntime.isTruthy(mlRuntime.equals(index, 0)))
                {
                    return (-mlRuntime.coerceToFloat(1.08));
                }
                let n = (mlRuntime.coerceToFloat(index) - mlRuntime.coerceToFloat(1));
                let row = 0;
                let start = 0;
                let count = 1;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(row) < mlRuntime.coerceToFloat(5))))
                {
                    if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(n) < mlRuntime.coerceToFloat((start + count)))))
                    {
                        return (0.58 + (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(row) * mlRuntime.coerceToFloat(ballR))) * mlRuntime.coerceToFloat(1.7320508)));
                    }
                    start = (start + count);
                    count = (count + 1);
                    row = (row + 1);
                }
                return 0.58;
            }
            function rackZ(index) {
                if (mlRuntime.isTruthy(mlRuntime.equals(index, 0)))
                {
                    return 0;
                }
                let n = (mlRuntime.coerceToFloat(index) - mlRuntime.coerceToFloat(1));
                let row = 0;
                let start = 0;
                let count = 1;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(row) < mlRuntime.coerceToFloat(5))))
                {
                    if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(n) < mlRuntime.coerceToFloat((start + count)))))
                    {
                        let col = (mlRuntime.coerceToFloat(n) - mlRuntime.coerceToFloat(start));
                        return (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(col) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(row) * mlRuntime.coerceToFloat(0.5))))) * mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ballR) * mlRuntime.coerceToFloat(2.02))));
                    }
                    start = (start + count);
                    count = (count + 1);
                    row = (row + 1);
                }
                return 0;
            }
            function resetRack() {
                let i = 0;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(i) < mlRuntime.coerceToFloat(16))))
                {
                    ballX[i] = rackX(i);
                    ballZ[i] = rackZ(i);
                    ballVx[i] = 0;
                    ballVz[i] = 0;
                    ballLive[i] = 1;
                    i = (i + 1);
                }
                aimAngle = 0;
                shotPower = 0.12;
                charging = false;
                aimMarkValid = false;
                pottedCount = 0;
            }
            function pocketHit(x, z) {
                let spots = [[(-mlRuntime.coerceToFloat(tableHX)), (-mlRuntime.coerceToFloat(tableHZ))], [(-mlRuntime.coerceToFloat(tableHX)), tableHZ], [tableHX, (-mlRuntime.coerceToFloat(tableHZ))], [tableHX, tableHZ], [0, (-mlRuntime.coerceToFloat(tableHZ))], [0, tableHZ]];
                let p = 0;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(p) < mlRuntime.coerceToFloat(6))))
                {
                    let dx = (mlRuntime.coerceToFloat(x) - mlRuntime.coerceToFloat(spots[p][0]));
                    let dz = (mlRuntime.coerceToFloat(z) - mlRuntime.coerceToFloat(spots[p][1]));
                    if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(((mlRuntime.coerceToFloat(dx) * mlRuntime.coerceToFloat(dx)) + (mlRuntime.coerceToFloat(dz) * mlRuntime.coerceToFloat(dz)))) < mlRuntime.coerceToFloat((mlRuntime.coerceToFloat((pocketR + 0.02)) * mlRuntime.coerceToFloat((pocketR + 0.02)))))))
                    {
                        return true;
                    }
                    p = (p + 1);
                }
                return false;
            }
            function ballsAreStill() {
                let i = 0;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(i) < mlRuntime.coerceToFloat(16))))
                {
                    if (mlRuntime.isTruthy(mlRuntime.equals(ballLive[i], 1)))
                    {
                        let speed = mlRuntime.math.sqrt(((mlRuntime.coerceToFloat(ballVx[i]) * mlRuntime.coerceToFloat(ballVx[i])) + (mlRuntime.coerceToFloat(ballVz[i]) * mlRuntime.coerceToFloat(ballVz[i]))));
                        if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(speed) > mlRuntime.coerceToFloat(0.04))))
                        {
                            return false;
                        }
                    }
                    i = (i + 1);
                }
                return true;
            }
            function potBall(i) {
                if (mlRuntime.isTruthy(mlRuntime.equals(ballLive[i], 0)))
                {
                    return;
                }
                ballLive[i] = 0;
                ballVx[i] = 0;
                ballVz[i] = 0;
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(i) > mlRuntime.coerceToFloat(0))))
                {
                    pottedCount = (pottedCount + 1);
                }
            }
            function respawnCueIfNeeded() {
                if (mlRuntime.isTruthy(mlRuntime.equals(ballLive[0], 1)))
                {
                    return;
                }
                let cx = (-mlRuntime.coerceToFloat(1.08));
                let cz = 0;
                let blocked = false;
                let i = 1;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(i) < mlRuntime.coerceToFloat(16))))
                {
                    if (mlRuntime.isTruthy(mlRuntime.equals(ballLive[i], 1)))
                    {
                        let dx = (mlRuntime.coerceToFloat(ballX[i]) - mlRuntime.coerceToFloat(cx));
                        let dz = (mlRuntime.coerceToFloat(ballZ[i]) - mlRuntime.coerceToFloat(cz));
                        if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(((mlRuntime.coerceToFloat(dx) * mlRuntime.coerceToFloat(dx)) + (mlRuntime.coerceToFloat(dz) * mlRuntime.coerceToFloat(dz)))) < mlRuntime.coerceToFloat((mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ballR) * mlRuntime.coerceToFloat(2.2))) * mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ballR) * mlRuntime.coerceToFloat(2.2))))))))
                        {
                            blocked = true;
                        }
                    }
                    i = (i + 1);
                }
                if (mlRuntime.isTruthy(blocked))
                {
                    cx = (-mlRuntime.coerceToFloat(0.7));
                }
                ballX[0] = cx;
                ballZ[0] = cz;
                ballVx[0] = 0;
                ballVz[0] = 0;
                ballLive[0] = 1;
            }
            function collideBalls() {
                let i = 0;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(i) < mlRuntime.coerceToFloat(16))))
                {
                    if (mlRuntime.isTruthy(mlRuntime.equals(ballLive[i], 1)))
                    {
                        let j = (i + 1);
                        while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(j) < mlRuntime.coerceToFloat(16))))
                        {
                            if (mlRuntime.isTruthy(mlRuntime.equals(ballLive[j], 1)))
                            {
                                let dx = (mlRuntime.coerceToFloat(ballX[j]) - mlRuntime.coerceToFloat(ballX[i]));
                                let dz = (mlRuntime.coerceToFloat(ballZ[j]) - mlRuntime.coerceToFloat(ballZ[i]));
                                let dist = mlRuntime.math.sqrt(((mlRuntime.coerceToFloat(dx) * mlRuntime.coerceToFloat(dx)) + (mlRuntime.coerceToFloat(dz) * mlRuntime.coerceToFloat(dz))));
                                let minDist = (mlRuntime.coerceToFloat(ballR) * mlRuntime.coerceToFloat(2));
                                if (mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.coerceToFloat(dist) < mlRuntime.coerceToFloat(minDist))) && mlRuntime.isTruthy((mlRuntime.coerceToFloat(dist) > mlRuntime.coerceToFloat(0.0001))))))
                                {
                                    let nx = (mlRuntime.coerceToFloat(dx) / mlRuntime.coerceToFloat(dist));
                                    let nz = (mlRuntime.coerceToFloat(dz) / mlRuntime.coerceToFloat(dist));
                                    let overlap = (mlRuntime.coerceToFloat(minDist) - mlRuntime.coerceToFloat(dist));
                                    ballX[i] = (mlRuntime.coerceToFloat(ballX[i]) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(nx) * mlRuntime.coerceToFloat(overlap))) * mlRuntime.coerceToFloat(0.5))));
                                    ballZ[i] = (mlRuntime.coerceToFloat(ballZ[i]) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(nz) * mlRuntime.coerceToFloat(overlap))) * mlRuntime.coerceToFloat(0.5))));
                                    ballX[j] = (ballX[j] + (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(nx) * mlRuntime.coerceToFloat(overlap))) * mlRuntime.coerceToFloat(0.5)));
                                    ballZ[j] = (ballZ[j] + (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(nz) * mlRuntime.coerceToFloat(overlap))) * mlRuntime.coerceToFloat(0.5)));
                                    let rel = ((mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ballVx[i]) - mlRuntime.coerceToFloat(ballVx[j]))) * mlRuntime.coerceToFloat(nx)) + (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ballVz[i]) - mlRuntime.coerceToFloat(ballVz[j]))) * mlRuntime.coerceToFloat(nz)));
                                    if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(rel) > mlRuntime.coerceToFloat(0))))
                                    {
                                        let impulse = (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat((-mlRuntime.coerceToFloat((1 + ballRest)))) * mlRuntime.coerceToFloat(rel))) * mlRuntime.coerceToFloat(0.5));
                                        ballVx[i] = (ballVx[i] + (mlRuntime.coerceToFloat(nx) * mlRuntime.coerceToFloat(impulse)));
                                        ballVz[i] = (ballVz[i] + (mlRuntime.coerceToFloat(nz) * mlRuntime.coerceToFloat(impulse)));
                                        ballVx[j] = (mlRuntime.coerceToFloat(ballVx[j]) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(nx) * mlRuntime.coerceToFloat(impulse))));
                                        ballVz[j] = (mlRuntime.coerceToFloat(ballVz[j]) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(nz) * mlRuntime.coerceToFloat(impulse))));
                                    }
                                }
                            }
                            j = (j + 1);
                        }
                    }
                    i = (i + 1);
                }
            }
            function physicsStep(dt) {
                let i = 0;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(i) < mlRuntime.coerceToFloat(16))))
                {
                    if (mlRuntime.isTruthy(mlRuntime.equals(ballLive[i], 1)))
                    {
                        ballX[i] = (ballX[i] + (mlRuntime.coerceToFloat(ballVx[i]) * mlRuntime.coerceToFloat(dt)));
                        ballZ[i] = (ballZ[i] + (mlRuntime.coerceToFloat(ballVz[i]) * mlRuntime.coerceToFloat(dt)));
                        let damp = (mlRuntime.coerceToFloat(1) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(feltFriction) * mlRuntime.coerceToFloat(dt))));
                        if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(damp) < mlRuntime.coerceToFloat(0))))
                        {
                            damp = 0;
                        }
                        ballVx[i] = (mlRuntime.coerceToFloat(ballVx[i]) * mlRuntime.coerceToFloat(damp));
                        ballVz[i] = (mlRuntime.coerceToFloat(ballVz[i]) * mlRuntime.coerceToFloat(damp));
                        let speed = mlRuntime.math.sqrt(((mlRuntime.coerceToFloat(ballVx[i]) * mlRuntime.coerceToFloat(ballVx[i])) + (mlRuntime.coerceToFloat(ballVz[i]) * mlRuntime.coerceToFloat(ballVz[i]))));
                        if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(speed) < mlRuntime.coerceToFloat(0.035))))
                        {
                            ballVx[i] = 0;
                            ballVz[i] = 0;
                        }
                        if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(speed) > mlRuntime.coerceToFloat(5.4))))
                        {
                            ballVx[i] = (mlRuntime.coerceToFloat(ballVx[i]) * mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(5.4) / mlRuntime.coerceToFloat(speed))));
                            ballVz[i] = (mlRuntime.coerceToFloat(ballVz[i]) * mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(5.4) / mlRuntime.coerceToFloat(speed))));
                        }
                        if (mlRuntime.isTruthy(pocketHit(ballX[i], ballZ[i])))
                        {
                            let spots = [[(-mlRuntime.coerceToFloat(tableHX)), (-mlRuntime.coerceToFloat(tableHZ))], [(-mlRuntime.coerceToFloat(tableHX)), tableHZ], [tableHX, (-mlRuntime.coerceToFloat(tableHZ))], [tableHX, tableHZ], [0, (-mlRuntime.coerceToFloat(tableHZ))], [0, tableHZ]];
                            let p = 0;
                            while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(p) < mlRuntime.coerceToFloat(6))))
                            {
                                let pdx = (mlRuntime.coerceToFloat(ballX[i]) - mlRuntime.coerceToFloat(spots[p][0]));
                                let pdz = (mlRuntime.coerceToFloat(ballZ[i]) - mlRuntime.coerceToFloat(spots[p][1]));
                                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(((mlRuntime.coerceToFloat(pdx) * mlRuntime.coerceToFloat(pdx)) + (mlRuntime.coerceToFloat(pdz) * mlRuntime.coerceToFloat(pdz)))) < mlRuntime.coerceToFloat((mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(pocketR) - mlRuntime.coerceToFloat(0.01))) * mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(pocketR) - mlRuntime.coerceToFloat(0.01))))))))
                                {
                                    potBall(i);
                                    p = 6;
                                }
                                p = (p + 1);
                            }
                        }
                        if (mlRuntime.isTruthy((mlRuntime.isTruthy(mlRuntime.equals(ballLive[i], 1)) && mlRuntime.isTruthy((!mlRuntime.isTruthy(pocketHit(ballX[i], ballZ[i])))))))
                        {
                            let limitX = (mlRuntime.coerceToFloat(tableHX) - mlRuntime.coerceToFloat(ballR));
                            let limitZ = (mlRuntime.coerceToFloat(tableHZ) - mlRuntime.coerceToFloat(ballR));
                            if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(ballX[i]) > mlRuntime.coerceToFloat(limitX))))
                            {
                                ballX[i] = limitX;
                                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(ballVx[i]) > mlRuntime.coerceToFloat(0))))
                                {
                                    ballVx[i] = (mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ballVx[i]) * mlRuntime.coerceToFloat(railRest))));
                                }
                            }
                            if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(ballX[i]) < mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat(limitX))))))
                            {
                                ballX[i] = (mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat(limitX));
                                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(ballVx[i]) < mlRuntime.coerceToFloat(0))))
                                {
                                    ballVx[i] = (mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ballVx[i]) * mlRuntime.coerceToFloat(railRest))));
                                }
                            }
                            if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(ballZ[i]) > mlRuntime.coerceToFloat(limitZ))))
                            {
                                ballZ[i] = limitZ;
                                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(ballVz[i]) > mlRuntime.coerceToFloat(0))))
                                {
                                    ballVz[i] = (mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ballVz[i]) * mlRuntime.coerceToFloat(railRest))));
                                }
                            }
                            if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(ballZ[i]) < mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat(limitZ))))))
                            {
                                ballZ[i] = (mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat(limitZ));
                                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(ballVz[i]) < mlRuntime.coerceToFloat(0))))
                                {
                                    ballVz[i] = (mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ballVz[i]) * mlRuntime.coerceToFloat(railRest))));
                                }
                            }
                        }
                    }
                    i = (i + 1);
                }
                collideBalls();
            }
            function shootCue() {
                if (mlRuntime.isTruthy((!mlRuntime.isTruthy(ballsAreStill()))))
                {
                    return;
                }
                if (mlRuntime.isTruthy(mlRuntime.equals(ballLive[0], 0)))
                {
                    respawnCueIfNeeded();
                }
                let speed = (0.9 + (mlRuntime.coerceToFloat(shotPower) * mlRuntime.coerceToFloat(4.4)));
                ballVx[0] = (mlRuntime.coerceToFloat(mlRuntime.math.cos(aimAngle)) * mlRuntime.coerceToFloat(speed));
                ballVz[0] = (mlRuntime.coerceToFloat(mlRuntime.math.sin(aimAngle)) * mlRuntime.coerceToFloat(speed));
                charging = false;
                shotPower = 0.12;
            }
            function powerBarText() {
                let filled = mlRuntime.coerceToInt((mlRuntime.coerceToFloat(shotPower) * mlRuntime.coerceToFloat(16)));
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(filled) < mlRuntime.coerceToFloat(0))))
                {
                    filled = 0;
                }
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(filled) > mlRuntime.coerceToFloat(16))))
                {
                    filled = 16;
                }
                let bar = "";
                let i = 0;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(i) < mlRuntime.coerceToFloat(16))))
                {
                    if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(i) < mlRuntime.coerceToFloat(filled))))
                    {
                        bar = (bar + "#");
                    }
                    else
                    {
                        bar = (bar + "-");
                    }
                    i = (i + 1);
                }
                return (((("[" + bar) + "]   ") + mlRuntime.coerceToString(mlRuntime.coerceToInt((mlRuntime.coerceToFloat(shotPower) * mlRuntime.coerceToFloat(100))))) + "%");
            }
            function aimFromMouse() {
                let mx = mlRuntime.three.getMouseX();
                let my = mlRuntime.three.getMouseY();
                let ndcX = (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(mx) / mlRuntime.coerceToFloat(width))) * mlRuntime.coerceToFloat(2))) - mlRuntime.coerceToFloat(1));
                let ndcY = (mlRuntime.coerceToFloat(1) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(my) / mlRuntime.coerceToFloat(height))) * mlRuntime.coerceToFloat(2))));
                let hx = (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ndcX) * mlRuntime.coerceToFloat(aspect))) * mlRuntime.coerceToFloat(tanHalf));
                let hy = (mlRuntime.coerceToFloat(ndcY) * mlRuntime.coerceToFloat(tanHalf));
                let dx = ((forwardX + (mlRuntime.coerceToFloat(rightX) * mlRuntime.coerceToFloat(hx))) + (mlRuntime.coerceToFloat(upX) * mlRuntime.coerceToFloat(hy)));
                let dy = ((forwardY + (mlRuntime.coerceToFloat(rightY) * mlRuntime.coerceToFloat(hx))) + (mlRuntime.coerceToFloat(upY) * mlRuntime.coerceToFloat(hy)));
                let dz = ((forwardZ + (mlRuntime.coerceToFloat(rightZ) * mlRuntime.coerceToFloat(hx))) + (mlRuntime.coerceToFloat(upZ) * mlRuntime.coerceToFloat(hy)));
                let dl = mlRuntime.math.sqrt((((mlRuntime.coerceToFloat(dx) * mlRuntime.coerceToFloat(dx)) + (mlRuntime.coerceToFloat(dy) * mlRuntime.coerceToFloat(dy))) + (mlRuntime.coerceToFloat(dz) * mlRuntime.coerceToFloat(dz))));
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(dl) < mlRuntime.coerceToFloat(0.0001))))
                {
                    return false;
                }
                dx = (mlRuntime.coerceToFloat(dx) / mlRuntime.coerceToFloat(dl));
                dy = (mlRuntime.coerceToFloat(dy) / mlRuntime.coerceToFloat(dl));
                dz = (mlRuntime.coerceToFloat(dz) / mlRuntime.coerceToFloat(dl));
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(mlRuntime.math.abs(dy)) < mlRuntime.coerceToFloat(0.0008))))
                {
                    return false;
                }
                let tHit = (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat(camY))) / mlRuntime.coerceToFloat(dy));
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(tHit) < mlRuntime.coerceToFloat(0.15))))
                {
                    return false;
                }
                let hitX = (camX + (mlRuntime.coerceToFloat(dx) * mlRuntime.coerceToFloat(tHit)));
                let hitZ = (camZ + (mlRuntime.coerceToFloat(dz) * mlRuntime.coerceToFloat(tHit)));
                let ax = (mlRuntime.coerceToFloat(ballX[0]) - mlRuntime.coerceToFloat(hitX));
                let az = (mlRuntime.coerceToFloat(ballZ[0]) - mlRuntime.coerceToFloat(hitZ));
                let al = mlRuntime.math.sqrt(((mlRuntime.coerceToFloat(ax) * mlRuntime.coerceToFloat(ax)) + (mlRuntime.coerceToFloat(az) * mlRuntime.coerceToFloat(az))));
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(al) < mlRuntime.coerceToFloat(0.05))))
                {
                    return false;
                }
                aimAngle = mlRuntime.math.atan2(az, ax);
                aimMarkX = hitX;
                aimMarkZ = hitZ;
                aimMarkValid = true;
                return true;
            }
            resetRack();
            function updateCamera() {
                camX = (mlRuntime.coerceToFloat(mlRuntime.math.sin(camAngle)) * mlRuntime.coerceToFloat(camRadius));
                camY = (mlRuntime.coerceToFloat(camRadius) * mlRuntime.coerceToFloat(0.629));
                camZ = (mlRuntime.coerceToFloat(mlRuntime.math.cos(camAngle)) * mlRuntime.coerceToFloat(camRadius));
                let fx = (mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat(camX));
                let fy = (mlRuntime.coerceToFloat(0.12) - mlRuntime.coerceToFloat(camY));
                let fz = (mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat(camZ));
                let fl = mlRuntime.math.sqrt((((mlRuntime.coerceToFloat(fx) * mlRuntime.coerceToFloat(fx)) + (mlRuntime.coerceToFloat(fy) * mlRuntime.coerceToFloat(fy))) + (mlRuntime.coerceToFloat(fz) * mlRuntime.coerceToFloat(fz))));
                fx = (mlRuntime.coerceToFloat(fx) / mlRuntime.coerceToFloat(fl));
                fy = (mlRuntime.coerceToFloat(fy) / mlRuntime.coerceToFloat(fl));
                fz = (mlRuntime.coerceToFloat(fz) / mlRuntime.coerceToFloat(fl));
                let rx = (mlRuntime.coerceToFloat(0) - mlRuntime.coerceToFloat(fz));
                let ry = 0;
                let rz = fx;
                let rl = mlRuntime.math.sqrt((((mlRuntime.coerceToFloat(rx) * mlRuntime.coerceToFloat(rx)) + (mlRuntime.coerceToFloat(ry) * mlRuntime.coerceToFloat(ry))) + (mlRuntime.coerceToFloat(rz) * mlRuntime.coerceToFloat(rz))));
                rx = (mlRuntime.coerceToFloat(rx) / mlRuntime.coerceToFloat(rl));
                ry = (mlRuntime.coerceToFloat(ry) / mlRuntime.coerceToFloat(rl));
                rz = (mlRuntime.coerceToFloat(rz) / mlRuntime.coerceToFloat(rl));
                forwardX = fx;
                forwardY = fy;
                forwardZ = fz;
                rightX = rx;
                rightY = ry;
                rightZ = rz;
                upX = (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ry) * mlRuntime.coerceToFloat(fz))) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(rz) * mlRuntime.coerceToFloat(fy))));
                upY = (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(rz) * mlRuntime.coerceToFloat(fx))) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(rx) * mlRuntime.coerceToFloat(fz))));
                upZ = (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(rx) * mlRuntime.coerceToFloat(fy))) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(ry) * mlRuntime.coerceToFloat(fx))));
            }
            let renderer = mlRuntime.three.createRenderer(width, height, "#app");
            mlRuntime.three.setClearColor(renderer, "#0b0a09");
            mlRuntime.three.setRendererSize(renderer, width, height);
            let scene = mlRuntime.three.createScene();
            let camera = mlRuntime.three.createOrthographicCamera((-mlRuntime.coerceToFloat(1)), 1, 1, (-mlRuntime.coerceToFloat(1)), 0, 1);
            let material = mlRuntime.three.createShaderMaterial({ ["vertexShader"]: vertexShader, ["fragmentShader"]: fragmentShader, ["depthWrite"]: false, ["uniforms"]: { ["uResolution"]: [width, height], ["uCamPos"]: [0, 3.05, 4.85], ["uCamForward"]: [0, 0, (-mlRuntime.coerceToFloat(1))], ["uCamRight"]: [1, 0, 0], ["uCamUp"]: [0, 1, 0], ["uLightPos"]: [lightX, lightY, lightZ], ["uTanHalf"]: tanHalf, ["uAspect"]: aspect, ["uBall0"]: [(-mlRuntime.coerceToFloat(1.08)), 0.075, 0], ["uBall1"]: [0.58, 0.075, 0], ["uBall2"]: [0.58, 0.075, 0], ["uBall3"]: [0.58, 0.075, 0], ["uBall4"]: [0.58, 0.075, 0], ["uBall5"]: [0.58, 0.075, 0], ["uBall6"]: [0.58, 0.075, 0], ["uBall7"]: [0.58, 0.075, 0], ["uBall8"]: [0.58, 0.075, 0], ["uBall9"]: [0.58, 0.075, 0], ["uBall10"]: [0.58, 0.075, 0], ["uBall11"]: [0.58, 0.075, 0], ["uBall12"]: [0.58, 0.075, 0], ["uBall13"]: [0.58, 0.075, 0], ["uBall14"]: [0.58, 0.075, 0], ["uBall15"]: [0.58, 0.075, 0], ["uCueCenter"]: [(-mlRuntime.coerceToFloat(1.7)), 0.075, 0], ["uCueHalf"]: [0.55, 0.012, 0.012], ["uCueAngle"]: 0, ["uCueOn"]: 1, ["uAimDir"]: [1, 0, 0], ["uAimMark"]: [(-mlRuntime.coerceToFloat(1.7)), 0, 0], ["uAimOn"]: 1 } });
            let quad = mlRuntime.three.createMesh(mlRuntime.three.createPlaneGeometry(2, 2), material);
            mlRuntime.three.add(scene, quad);
            function ballUniform(i) {
                if (mlRuntime.isTruthy(mlRuntime.equals(ballLive[i], 1)))
                {
                    return [ballX[i], ballR, ballZ[i]];
                }
                return [8, (-mlRuntime.coerceToFloat(2)), 8];
            }
            function pushPlayUniforms() {
                mlRuntime.three.setUniform(material, "uCamPos", [camX, camY, camZ]);
                mlRuntime.three.setUniform(material, "uCamForward", [forwardX, forwardY, forwardZ]);
                mlRuntime.three.setUniform(material, "uCamRight", [rightX, rightY, rightZ]);
                mlRuntime.three.setUniform(material, "uCamUp", [upX, upY, upZ]);
                mlRuntime.three.setUniform(material, "uLightPos", [lightX, lightY, lightZ]);
                mlRuntime.three.setUniform(material, "uBall0", ballUniform(0));
                mlRuntime.three.setUniform(material, "uBall1", ballUniform(1));
                mlRuntime.three.setUniform(material, "uBall2", ballUniform(2));
                mlRuntime.three.setUniform(material, "uBall3", ballUniform(3));
                mlRuntime.three.setUniform(material, "uBall4", ballUniform(4));
                mlRuntime.three.setUniform(material, "uBall5", ballUniform(5));
                mlRuntime.three.setUniform(material, "uBall6", ballUniform(6));
                mlRuntime.three.setUniform(material, "uBall7", ballUniform(7));
                mlRuntime.three.setUniform(material, "uBall8", ballUniform(8));
                mlRuntime.three.setUniform(material, "uBall9", ballUniform(9));
                mlRuntime.three.setUniform(material, "uBall10", ballUniform(10));
                mlRuntime.three.setUniform(material, "uBall11", ballUniform(11));
                mlRuntime.three.setUniform(material, "uBall12", ballUniform(12));
                mlRuntime.three.setUniform(material, "uBall13", ballUniform(13));
                mlRuntime.three.setUniform(material, "uBall14", ballUniform(14));
                mlRuntime.three.setUniform(material, "uBall15", ballUniform(15));
                let still = ballsAreStill();
                if (mlRuntime.isTruthy((mlRuntime.isTruthy(still) && mlRuntime.isTruthy(mlRuntime.equals(ballLive[0], 1)))))
                {
                    let tipGap = 0.08;
                    if (mlRuntime.isTruthy(charging))
                    {
                        tipGap = (0.08 + (mlRuntime.coerceToFloat(shotPower) * mlRuntime.coerceToFloat(0.5)));
                    }
                    let back = (0.55 + tipGap);
                    let cx = (mlRuntime.coerceToFloat(ballX[0]) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(mlRuntime.math.cos(aimAngle)) * mlRuntime.coerceToFloat(back))));
                    let cz = (mlRuntime.coerceToFloat(ballZ[0]) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(mlRuntime.math.sin(aimAngle)) * mlRuntime.coerceToFloat(back))));
                    mlRuntime.three.setUniform(material, "uCueCenter", [cx, 0.075, cz]);
                    mlRuntime.three.setUniform(material, "uCueHalf", [0.55, 0.012, 0.012]);
                    mlRuntime.three.setUniform(material, "uCueAngle", aimAngle);
                    mlRuntime.three.setUniform(material, "uCueOn", 1);
                    mlRuntime.three.setUniform(material, "uAimDir", [mlRuntime.math.cos(aimAngle), 0, mlRuntime.math.sin(aimAngle)]);
                    if (mlRuntime.isTruthy(aimMarkValid))
                    {
                        mlRuntime.three.setUniform(material, "uAimMark", [aimMarkX, 0, aimMarkZ]);
                    }
                    else
                    {
                        mlRuntime.three.setUniform(material, "uAimMark", [8, 0, 8]);
                    }
                    mlRuntime.three.setUniform(material, "uAimOn", 1);
                }
                else
                {
                    mlRuntime.three.setUniform(material, "uCueOn", 0);
                    mlRuntime.three.setUniform(material, "uAimOn", 0);
                }
            }
            updateCamera();
            pushPlayUniforms();
            function update(dtMs) {
                let dt = dtMs;
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(dt) > mlRuntime.coerceToFloat(50))))
                {
                    dt = 50;
                }
                let dtSec = (mlRuntime.coerceToFloat(dt) / mlRuntime.coerceToFloat(1000));
                if (mlRuntime.isTruthy(mlRuntime.three.isKeyDown("arrowleft")))
                {
                    camSpeed = (mlRuntime.coerceToFloat(camSpeed) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(1.2) * mlRuntime.coerceToFloat(dtSec))));
                }
                if (mlRuntime.isTruthy(mlRuntime.three.isKeyDown("arrowright")))
                {
                    camSpeed = (camSpeed + (mlRuntime.coerceToFloat(1.2) * mlRuntime.coerceToFloat(dtSec)));
                }
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(camSpeed) < mlRuntime.coerceToFloat((-mlRuntime.coerceToFloat(2.4))))))
                {
                    camSpeed = (-mlRuntime.coerceToFloat(2.4));
                }
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(camSpeed) > mlRuntime.coerceToFloat(2.4))))
                {
                    camSpeed = 2.4;
                }
                let cDown = mlRuntime.three.isKeyDown("c");
                if (mlRuntime.isTruthy((mlRuntime.isTruthy(cDown) && mlRuntime.isTruthy((!mlRuntime.isTruthy(cWasDown))))))
                {
                    camSpeed = 0;
                }
                cWasDown = cDown;
                camAngle = (camAngle + (mlRuntime.coerceToFloat(camSpeed) * mlRuntime.coerceToFloat(dtSec)));
                if (mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.isTruthy(mlRuntime.three.isKeyDown("]")) || mlRuntime.isTruthy(mlRuntime.three.isKeyDown("=")))) || mlRuntime.isTruthy(mlRuntime.three.isKeyDown("+")))))
                {
                    zoomAcc = (mlRuntime.coerceToFloat(zoomAcc) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(18) * mlRuntime.coerceToFloat(dtSec))));
                }
                if (mlRuntime.isTruthy((mlRuntime.isTruthy(mlRuntime.three.isKeyDown("[")) || mlRuntime.isTruthy(mlRuntime.three.isKeyDown("-")))))
                {
                    zoomAcc = (zoomAcc + (mlRuntime.coerceToFloat(18) * mlRuntime.coerceToFloat(dtSec)));
                }
                if (mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.coerceToFloat(zoomAcc) <= mlRuntime.coerceToFloat((-mlRuntime.coerceToFloat(1))))) || mlRuntime.isTruthy((mlRuntime.coerceToFloat(zoomAcc) >= mlRuntime.coerceToFloat(1))))))
                {
                    let zoomStep = mlRuntime.coerceToInt(zoomAcc);
                    nudgeRange(zoomRange, zoomStep, 24, 80);
                    zoomAcc = (mlRuntime.coerceToFloat(zoomAcc) - mlRuntime.coerceToFloat(zoomStep));
                }
                syncPlaySettings();
                let mx = mlRuntime.coerceToInt(mlRuntime.three.getMouseX());
                let my = mlRuntime.coerceToInt(mlRuntime.three.getMouseY());
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(lastMouseX) < mlRuntime.coerceToFloat(0))))
                {
                    lastMouseX = mx;
                    lastMouseY = my;
                }
                let still = ballsAreStill();
                let mouseMoved = (mlRuntime.isTruthy((!mlRuntime.equals(mx, lastMouseX))) || mlRuntime.isTruthy((!mlRuntime.equals(my, lastMouseY))));
                let mouseOnTable = (mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.coerceToFloat(mx) >= mlRuntime.coerceToFloat(0))) && mlRuntime.isTruthy((mlRuntime.coerceToFloat(mx) < mlRuntime.coerceToFloat(width))))) && mlRuntime.isTruthy((mlRuntime.coerceToFloat(my) >= mlRuntime.coerceToFloat(0))))) && mlRuntime.isTruthy((mlRuntime.coerceToFloat(my) < mlRuntime.coerceToFloat(height))));
                if (mlRuntime.isTruthy((mlRuntime.isTruthy(still) && mlRuntime.isTruthy(mlRuntime.equals(ballLive[0], 1)))))
                {
                    if (mlRuntime.isTruthy((mlRuntime.isTruthy(mouseMoved) && mlRuntime.isTruthy(mouseOnTable))))
                    {
                        aimFromMouse();
                    }
                    if (mlRuntime.isTruthy(mlRuntime.three.isKeyDown("a")))
                    {
                        aimAngle = (mlRuntime.coerceToFloat(aimAngle) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(1.6) * mlRuntime.coerceToFloat(dtSec))));
                    }
                    if (mlRuntime.isTruthy(mlRuntime.three.isKeyDown("d")))
                    {
                        aimAngle = (aimAngle + (mlRuntime.coerceToFloat(1.6) * mlRuntime.coerceToFloat(dtSec)));
                    }
                    let holdShot = (mlRuntime.isTruthy(mlRuntime.three.isKeyDown(" ")) || mlRuntime.isTruthy((mlRuntime.isTruthy(mlRuntime.three.isMouseDown(0)) && mlRuntime.isTruthy(mouseOnTable))));
                    if (mlRuntime.isTruthy(holdShot))
                    {
                        if (mlRuntime.isTruthy((!mlRuntime.isTruthy(charging))))
                        {
                            charging = true;
                            shotPower = 0.12;
                        }
                        else
                        {
                            shotPower = (shotPower + (mlRuntime.coerceToFloat(0.85) * mlRuntime.coerceToFloat(dtSec)));
                            if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(shotPower) > mlRuntime.coerceToFloat(1))))
                            {
                                shotPower = 1;
                            }
                        }
                    }
                    else
                    {
                        if (mlRuntime.isTruthy(charging))
                        {
                            shootCue();
                        }
                    }
                }
                else
                {
                    charging = false;
                    if (mlRuntime.isTruthy(mouseMoved))
                    {
                        lightX = (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(mx) / mlRuntime.coerceToFloat(width))) - mlRuntime.coerceToFloat(0.5))) * mlRuntime.coerceToFloat(4.2));
                        lightZ = (mlRuntime.coerceToFloat(0.4) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat((mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(my) / mlRuntime.coerceToFloat(height))) - mlRuntime.coerceToFloat(0.5))) * mlRuntime.coerceToFloat(3.2))));
                    }
                }
                lastMouseX = mx;
                lastMouseY = my;
                let rDown = mlRuntime.three.isKeyDown("r");
                if (mlRuntime.isTruthy((mlRuntime.isTruthy(rDown) && mlRuntime.isTruthy((!mlRuntime.isTruthy(rWasDown))))))
                {
                    resetRack();
                }
                rWasDown = rDown;
                let step = 0;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(step) < mlRuntime.coerceToFloat(3))))
                {
                    physicsStep((mlRuntime.coerceToFloat(dtSec) / mlRuntime.coerceToFloat(3)));
                    step = (step + 1);
                }
                if (mlRuntime.isTruthy(ballsAreStill()))
                {
                    respawnCueIfNeeded();
                }
                still = ballsAreStill();
                let orbitTxt = mlRuntime.coerceToString((mlRuntime.coerceToFloat(mlRuntime.coerceToInt((mlRuntime.coerceToFloat(camSpeed) * mlRuntime.coerceToFloat(100)))) / mlRuntime.coerceToFloat(100)));
                let zoomTxt = mlRuntime.coerceToString((mlRuntime.coerceToFloat(mlRuntime.coerceToInt((mlRuntime.coerceToFloat(camRadius) * mlRuntime.coerceToFloat(10)))) / mlRuntime.coerceToFloat(10)));
                let railTxt = mlRuntime.coerceToString((mlRuntime.coerceToFloat(mlRuntime.coerceToInt((mlRuntime.coerceToFloat(railRest) * mlRuntime.coerceToFloat(100)))) / mlRuntime.coerceToFloat(100)));
                let ballTxt = mlRuntime.coerceToString((mlRuntime.coerceToFloat(mlRuntime.coerceToInt((mlRuntime.coerceToFloat(ballRest) * mlRuntime.coerceToFloat(100)))) / mlRuntime.coerceToFloat(100)));
                let fricTxt = mlRuntime.coerceToString((mlRuntime.coerceToFloat(mlRuntime.coerceToInt((mlRuntime.coerceToFloat(feltFriction) * mlRuntime.coerceToFloat(100)))) / mlRuntime.coerceToFloat(100)));
                mlRuntime.dom.setText(physLine, ((((((((("Zoom " + zoomTxt) + "   Cushion e ") + railTxt) + "   Ball e ") + ballTxt) + "   Friction ") + fricTxt) + "   Orbit ") + orbitTxt));
                if (mlRuntime.isTruthy(still))
                {
                    let deg = mlRuntime.coerceToInt((mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(aimAngle) * mlRuntime.coerceToFloat(180))) / mlRuntime.coerceToFloat(3.14159265)));
                    if (mlRuntime.isTruthy(charging))
                    {
                        mlRuntime.dom.setText(status, (((("Charging… release to shoot   Aim " + mlRuntime.coerceToString(deg)) + " deg   Potted ") + mlRuntime.coerceToString(pottedCount)) + "   C stop orbit   R reset"));
                    }
                    else
                    {
                        mlRuntime.dom.setText(status, (((("Aim " + mlRuntime.coerceToString(deg)) + " deg   Hold click/Space to charge   [ ] zoom   C stop orbit   Potted ") + mlRuntime.coerceToString(pottedCount)) + "   R reset"));
                    }
                    if (mlRuntime.isTruthy(charging))
                    {
                        mlRuntime.dom.setText(powerLine, ("Power  " + powerBarText()));
                    }
                    else
                    {
                        mlRuntime.dom.setText(powerLine, "Power  [----------------]   hold to charge");
                    }
                }
                else
                {
                    mlRuntime.dom.setText(status, (("Balls in motion…   Potted " + mlRuntime.coerceToString(pottedCount)) + "   C stop orbit   R reset"));
                    mlRuntime.dom.setText(powerLine, "Power  [----------------]");
                }
                updateCamera();
                pushPlayUniforms();
            }
            function render() {
                mlRuntime.three.render(renderer, scene, camera);
            }
            mlRuntime.three.start(update, render);
        }
    }

    return { main };
})();

if (typeof globalThis !== "undefined") {
    globalThis.MaldaApp = MaldaApp;
}

if (typeof module !== "undefined" && module.exports) {
    module.exports = MaldaApp;
}

async function __maldaRunMain() {
    try {
        await MaldaApp.main();
    } finally {
        if (mlRuntime.actors && typeof mlRuntime.actors.shutdownAsync === "function") {
            await mlRuntime.actors.shutdownAsync();
        }
    }
}

if (typeof require !== "undefined" && require.main === module) {
    __maldaRunMain().catch((error) => {
        throw error;
    });
}
