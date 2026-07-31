# Second batch: expands Tier 0 suite toward 80 cases (run after batch 1).
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
$casesDir = Join-Path $RepoRoot "conformance\tier0\cases"
$utf8 = New-Object System.Text.UTF8Encoding $false

function Write-Case([string]$Name, [string]$Malda, [string]$Expect) {
    [System.IO.File]::WriteAllText((Join-Path $casesDir "$Name.malda"), $Malda.Trim() + "`n", $utf8)
    [System.IO.File]::WriteAllText((Join-Path $casesDir "$Name.expect"), $Expect.TrimEnd(), $utf8)
}

Write-Case "match-string-literal" @'
var msg = "hello";
var result = match msg {
    case "hello": "greeting";
    case "goodbye": "farewell";
    default: "unknown";
};
print(result);
'@ "greeting"

Write-Case "match-bool-literal" @'
var flag = true;
var result = match flag {
    case true: "yes";
    case false: "no";
};
print(result);
'@ "yes"

Write-Case "match-null-literal" @'
var x = null;
var result = match x {
    case null: "is null";
    default: "not null";
};
print(result);
'@ "is null"

Write-Case "match-wildcard" @'
var x = 42;
var result = match x {
    case 10: "ten";
    case _: "other";
};
print(result);
'@ "other"

Write-Case "match-identifier-bind" @'
var x = 42;
var result = match x {
    case y: y + 10;
};
print(result);
'@ "52"

Write-Case "match-array-exact" @'
var arr = [1, 2, 3];
var result = match arr {
    case [1, 2, 3]: "exact match";
    default: "no match";
};
print(result);
'@ "exact match"

Write-Case "match-array-bind-sum" @'
var arr = [10, 20];
var result = match arr {
    case [x, y]: x + y;
    default: 0;
};
print(result);
'@ "30"

Write-Case "match-array-rest" @'
var arr = [1, 2, 3, 4, 5];
var result = match arr {
    case [first, second, ...rest]: first + second + length(rest);
    default: 0;
};
print(result);
'@ "6"

Write-Case "match-array-two" @'
var arr = [1, 2];
var result = match arr {
    case [x, y, z]: "three";
    case [x, y]: "two";
    default: "other";
};
print(result);
'@ "two"

Write-Case "typeof-string" 'print(typeOf("hi"));' "string"

Write-Case "typeof-float" 'print(typeOf(3.14));' "float"

Write-Case "typeof-null" 'print(typeOf(null));' "null"

Write-Case "typeof-array" @'
var a = [1, 2];
print(typeOf(a));
'@ "array"

Write-Case "is-tag-bool-legacy" @'
print(isTag(true, "boolean"));
print(isTag(true, "bool"));
'@ "true`ntrue"

Write-Case "is-tag-dict-legacy" @'
var d = dict { "k": 1 };
print(isTag(d, "dictionary"));
print(isTag(d, "dict"));
'@ "true`ntrue"

Write-Case "comparison-operators" @'
print(3 < 5);
print(3 > 5);
print(3 <= 3);
print(4 >= 5);
'@ "true`nfalse`ntrue`nfalse"

Write-Case "modulo-op" 'print(10 % 3);' "1"

Write-Case "unary-minus" 'print(-(2 + 3));' "-5"

Write-Case "float-literal-print" 'print(3.14);' "3.14"

Write-Case "string-not-equal" @'
print("a" != "b");
print("a" != "a");
'@ "true`nfalse"

Write-Case "array-index-access" @'
var a = [10, 20, 30];
print(a[1]);
'@ "20"

Write-Case "array-append-length" @'
var a = [];
a.append(1);
a.append(2);
print(length(a));
print(a[0]);
'@ "2`n1"

Write-Case "dict-dot-assign" @'
var d = dict { "a": 1 };
d.b = 2;
print(d.b);
'@ "2"

Write-Case "break-while" @'
var i = 0;
while (true) {
    if (i >= 3) {
        break;
    }
    print(i);
    i = i + 1;
}
'@ "0`n1`n2"

Write-Case "continue-while" @'
var i = 0;
while (i < 5) {
    i = i + 1;
    if (i == 3) {
        continue;
    }
    print(i);
}
'@ "1`n2`n4`n5"

Write-Case "for-continue-skip" @'
var results = [];
for (var i = 0; i < 5; i = i + 1) {
    if (i == 2) {
        continue;
    }
    results.append(i);
}
print(length(results));
print(results[0]);
print(results[1]);
'@ "4`n0`n1"

Write-Case "and-or-bool" @'
print(true && false);
print(true || false);
print(!false);
'@ "false`ntrue`ntrue"

Write-Case "fibonacci-small" @'
function fib(n) {
    if (n <= 1) {
        return n;
    }
    return fib(n - 1) + fib(n - 2);
}
print(fib(7));
'@ "13"

Write-Case "option-unwrap-none" @'
var o = option.none();
print(option.unwrapOr(o, 42));
print(option.isNone(o));
'@ "42`ntrue"

Write-Case "result-is-err-true" @'
var r = result.err("x");
print(result.isErr(r));
print(result.isOk(r));
'@ "true`nfalse"

Write-Case "is-tag-rejects-wrong" @'
print(isTag(1, "bool"));
print(isTag("x", "int"));
'@ "false`nfalse"

Write-Case "match-first-branch" @'
var n = 1;
print(match n { case 1: "one"; case 2: "two"; default: "other"; });
'@ "one"

Write-Case "run-property-stable" @'
property stableIdentity(x) {
    return (x + 0) == x;
}
var result = runProperty("stableIdentity", 10, 99);
print(string(result.seed));
print(string(result.iterations));
print(string(result.passed));
'@ "99`n10`ntrue"

Write-Case "empty-array-length" @'
var a = [];
print(length(a));
'@ "0"

Write-Case "if-else-chain" @'
var n = 2;
if (n == 1) {
    print("one");
} else if (n == 2) {
    print("two");
} else {
    print("other");
}
'@ "two"

Write-Case "operator-precedence" 'print(2 + 3 * 4);' "14"

Write-Case "string-length-builtin" 'print(length("abc"));' "3"

Write-Case "await-literal-task" @'
var t = async 7;
var v = await t;
print(v);
'@ "7"

Write-Case "sum-ok-payload-field" @'
type Result = Ok(value) | Err(message);
var r = Ok(99);
var out = match r {
    case Ok(v): v;
    case Err(m): 0;
};
print(out);
'@ "99"

Write-Case "variant-err-message" @'
type Result = Ok(value) | Err(message);
var r = Err("fail");
var out = match r {
    case Ok(v): "";
    case Err(m): m;
};
print(out);
'@ "fail"

Write-Case "dict-keys-dot" @'
var d = dict { "x": 1, "y": 2 };
print(d.x + d.y);
'@ "3"

Write-Case "greater-equals-int" @'
print(5 >= 5);
print(4 >= 5);
'@ "true`nfalse"

Write-Host "Wrote batch-2 Tier 0 cases to $casesDir"
