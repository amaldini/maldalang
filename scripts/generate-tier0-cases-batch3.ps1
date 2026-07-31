# Third batch: object/destructuring match patterns migrated from PatternMatchingTests.
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

Write-Case "match-object-simple" @'
var obj = { type: "Start", value: 42 };
var result = match obj {
    case { type: "Start", value: v }: v;
    case { type: "Stop" }: "stopped";
    default: "unknown";
};
print(result);
'@ "42"

Write-Case "match-object-shorthand" @'
var obj = { name: "Alice", age: 30 };
var result = match obj {
    case { name, age }: name + " is " + age;
    default: "unknown";
};
print(result);
'@ "Alice is 30"

Write-Case "match-object-nested" @'
var obj = { user: { name: "Bob", age: 25 } };
var result = match obj {
    case { user: { name, age } }: name + " is " + age;
    default: "unknown";
};
print(result);
'@ "Bob is 25"

Write-Case "match-object-missing-prop" @'
var obj = { name: "Alice" };
var result = match obj {
    case { name, age }: "has age";
    case { name }: "no age";
    default: "unknown";
};
print(result);
'@ "no age"

Write-Case "match-no-default-error" @'
var x = 99;
try {
    var result = match x {
        case 1: "one";
        case 2: "two";
    };
    print("should not reach here");
} catch (e) {
    print("error caught");
}
'@ "error caught"

Write-Case "match-statement-body" @'
var x = 42;
match x {
    case 42: print("matched 42");
    default: print("no match");
}
'@ "matched 42"

Write-Case "match-identifier-first" @'
var x = 42;
var result = match x {
    case y: "bound to y";
    case 42: "matched 42";
    default: "default";
};
print(result);
'@ "bound to y"

Write-Case "match-nested-array-object" @'
var data = [{ type: "A", value: 1 }, { type: "B", value: 2 }];
var result = match data {
    case [{ type: "A", value: v }, ...rest]: v;
    default: 0;
};
print(result);
'@ "1"

Write-Case "match-block-expression" @'
var x = 42;
var result = match x {
    case 42: {
        print("side effect");
        "result";
    }
    default: "no match";
};
print(result);
'@ "side effect`nresult"

Write-Case "match-block-null" @'
var x = 42;
var result = match x {
    case 42: {
        print("only side effect");
    }
    default: "no match";
};
print(result == null ? "null" : "not null");
'@ "only side effect`nnull"

Write-Case "match-default-block" @'
var x = 99;
var result = match x {
    case 1: "one";
    default: {
        print("fallback");
        "other";
    }
};
print(result);
'@ "fallback`nother"

Write-Case "sum-type-divide-ok" @'
type Result = Ok(value) | Err(message);
function divide(a, b) {
    if (b == 0) return Err("divide by zero");
    return Ok(a / b);
}
var r = divide(10, 2);
var result = match r {
    case Ok(v): "ok: " + v;
    case Err(msg): "error: " + msg;
};
print(result);
'@ "ok: 5"

Write-Case "sum-type-none-constructor" @'
type Option = Some(x) | None();
var n = None();
var result = match n {
    case Some(v): "some " + v;
    case None(): "none";
};
print(result);
'@ "none"

Write-Host "Wrote batch 3 pattern-matching cases to $casesDir"
