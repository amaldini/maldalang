# Generates conformance/tier0/cases/*.malda and *.expect from embedded definitions.
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
$casesDir = Join-Path $RepoRoot "conformance\tier0\cases"
New-Item -ItemType Directory -Force -Path $casesDir | Out-Null

$utf8 = New-Object System.Text.UTF8Encoding $false

function Write-Case([string]$Name, [string]$Malda, [string]$Expect) {
    [System.IO.File]::WriteAllText((Join-Path $casesDir "$Name.malda"), $Malda.Trim() + "`n", $utf8)
    [System.IO.File]::WriteAllText((Join-Path $casesDir "$Name.expect"), $Expect.TrimEnd(), $utf8)
}

Write-Case "match-literal" @'
var x = 42;
var result = match x {
    case 42: "ok";
    default: "no";
};
print(result);
'@ "ok"

Write-Case "dict-missing-null" @'
var d = dict { "a": 1 };
print(d.missing == null);
'@ "true"

Write-Case "typeof-int" 'print(typeOf(42));' "int"
Write-Case "typeof-dict" @'
var d = dict { "k": 1 };
print(typeOf(d));
'@ "dict"
Write-Case "typeof-bool" 'print(typeOf(true));' "bool"
Write-Case "typeof-variant" @'
type Result = Ok(value) | Err(message);
print(typeOf(Ok(1)));
'@ "variant"
Write-Case "typeof-task" @'
function f() { return 1; }
print(typeOf(async f()));
'@ "task"
Write-Case "is-tag-legacy" @'
print(isTag(42, "integer"));
print(isTag(42, "int"));
'@ "true`ntrue"

Write-Case "sum-type-match" @'
type Result = Ok(value) | Err(message);
var r = Ok(7);
var out = match r {
    case Ok(v): v;
    case Err(m): -1;
};
print(out);
'@ "7"

Write-Case "is-number" @'
print(isNumber(1));
print(isNumber(3.14));
print(isNumber("x"));
'@ "true`ntrue`nfalse"

Write-Case "async-await" @'
function compute() {
    return 99;
}
var t = async compute();
var v = await t;
print(v);
'@ "99"

Write-Case "all-variadic" @'
var t1 = async 1;
var t2 = async 2;
var allTask = all(t1, t2);
var results = await allTask;
print(results[0] + results[1]);
'@ "3"

Write-Case "all-array" @'
var t1 = async 1;
var t2 = async 2;
var tasks = [t1, t2];
var allTask = all(tasks);
var results = await allTask;
print(results[0] + results[1]);
'@ "3"

Write-Case "result-map-unwrap" @'
var r = result.ok(10);
var doubled = result.map(r, (x) => x * 2);
print(result.unwrapOr(doubled, 0));
print(result.isOk(r));
print(result.isErr(r));
'@ "20`ntrue`nfalse"

Write-Case "result-err-unwrapor" @'
var r = result.err("bad");
var mapped = result.map(r, (x) => x);
print(result.isErr(mapped));
print(result.unwrapOr(mapped, 99));
'@ "true`n99"

Write-Case "option-some-map" @'
var o = option.some(3);
var next = option.map(o, (n) => n + 1);
print(option.unwrapOr(next, 0));
print(option.isNone(option.none()));
'@ "4`ntrue"

Write-Case "null-conditional-member" @'
var d = null;
var x = d?.missing;
print(x == null);
'@ "true"

Write-Case "null-conditional-index" @'
var d = null;
print(d?["key"] == null);
'@ "true"

Write-Case "null-conditional-present" @'
var d = dict { "a": 7 };
print(d?.a);
'@ "7"

Write-Case "catch-io-filter" @'
try {
    throw dict { "kind": "IO", "message": "disk full" };
} catch (e if e.kind == "IO") {
    print("io:" + e.message);
} catch (e) {
    print("other");
}
'@ "io:disk full"

Write-Case "catch-fallback-generic" @'
try {
    throw dict { "kind": "Parse", "message": "bad token" };
} catch (e if e.kind == "IO") {
    print("io");
} catch (e) {
    print("generic:" + e.message);
}
'@ "generic:bad token"

Write-Case "catch-rethrow-nested" @'
var handled = false;
try {
    try {
        throw dict { "kind": "Parse", "message": "x" };
    } catch (e if e.kind == "IO") {
        handled = true;
    }
} catch (e) {
    handled = true;
}
print(handled);
'@ "true"

Write-Case "catch-plain-string" @'
try {
    throw "plain";
} catch (e if e == "plain") {
    print("matched");
}
'@ "matched"

Write-Case "ternary-true-branch" @'
var n = 2;
print(n > 1 ? "yes" : "no");
'@ "yes"

Write-Case "foreach-sum" @'
var items = [1, 2, 3];
var sum = 0;
foreach (var x in items) {
    sum = sum + x;
}
print(sum);
'@ "6"

Write-Case "try-catch-string" @'
try {
    throw "boom";
} catch (e) {
    print("caught:" + e);
}
'@ "caught:boom"

Write-Case "match-default-fallback" @'
var x = 0;
print(match x { case 1: "one"; default: "other"; });
'@ "other"

Write-Case "dict-bracket-access" @'
var d = dict { "k": 5 };
print(d["k"]);
'@ "5"

Write-Case "arithmetic-print" 'print(2 + 3 * 4);' "14"

Write-Case "sum-type-err-branch" @'
type Result = Ok(value) | Err(message);
var r = Err("nope");
var out = match r {
    case Ok(v): v;
    case Err(m): 0;
};
print(out);
'@ "0"

Write-Case "for-loop-count" @'
var n = 0;
for (var i = 0; i < 3; i = i + 1) {
    n = n + 1;
}
print(n);
'@ "3"

Write-Case "while-loop-count" @'
var n = 0;
while (n < 3) {
    n = n + 1;
}
print(n);
'@ "3"

Write-Case "string-concat" 'print("a" + "b");' "ab"
Write-Case "equality-primitives" @'
print(1 == 1);
print(1 == 2);
'@ "true`nfalse"

Write-Case "logical-not" @'
print(!false);
print(!true);
'@ "true`nfalse"

Write-Case "array-length" @'
var a = [10, 20];
print(length(a));
'@ "2"

Write-Case "function-return" @'
function twice(n) {
    return n * 2;
}
print(twice(4));
'@ "8"

Write-Case "compare-null" @'
var x = null;
print(x == null);
print(x != null);
'@ "true`nfalse"

Write-Case "nested-match" @'
var code = 404;
var label = match code {
    case 404: "missing";
    default: "unknown";
};
print(label);
'@ "missing"

Write-Case "actor-send-order" @'
actor Worker {
    on work(label) {
        print(label);
    }
}

actor Controller {
    on start() {
        var w = spawn Worker();
        sleep(100);
        send w.work("first");
        send w.work("second");
        sleep(300);
    }
}

var c = spawn Controller();
sleep(100);
send c.start();
sleep(500);
'@ "first`nsecond"

& "$PSScriptRoot\generate-tier0-cases-batch2.ps1" -RepoRoot $RepoRoot
& "$PSScriptRoot\generate-tier0-cases-batch3.ps1" -RepoRoot $RepoRoot
& "$PSScriptRoot\sync-tier0-manifest.ps1" -RepoRoot $RepoRoot
Write-Host "Wrote $((Get-ChildItem $casesDir -Filter *.malda).Count) Tier 0 cases to $casesDir"
