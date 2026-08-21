// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

/// <summary>
/// Phase 1.2 stdlib namespaces: math.*, str.*, io.* with deprecated flat aliases.
/// </summary>
public static class StdLibNamespaces
{
    public const string MathModule = "math";
    public const string StrModule = "str";
    public const string IoModule = "io";
    public const string PdfModule = "pdf";
    public const string DocModule = "doc";
    public const string ResultModule = "result";
    public const string OptionModule = "option";
    public const string GroundedModule = "grounded";
    public const string CapModule = "cap";
    public const string DeprecatedMathModuleAlias = "Math";

    public static readonly IReadOnlySet<string> MathMethodNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "abs", "sum", "average", "max", "min", "pow", "sqrt",
        "floor", "ceil", "round", "trunc", "sign",
        "exp", "log", "log10", "log2",
        "sin", "cos", "tan", "asin", "acos", "atan", "atan2",
        "hypot", "clamp", "degToRad", "radToDeg",
        "rsqrt", "randn", "argmax", "argmin", "logSumExp", "softmax", "crossEntropyFromLogits",
        "randomChoiceWeighted", "seed", "random", "randomInt", "randomFloat"
    };

    public static readonly IReadOnlySet<string> StrMethodNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "length", "upper", "lower", "trim", "text", "trimText", "substring", "indexOf", "replace", "split",
        "normalizeText", "tokenize", "tokenOverlap", "similarity", "extractNumbers",
        "regexMatch", "regexReplace", "regexFind",
        "startsWith", "endsWith", "padStart", "padEnd", "includes", "join", "repeat",
        "base64Encode", "base64Decode", "md5", "sha256"
    };

    public static readonly IReadOnlySet<string> ResultMethodNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "ok", "err", "map", "andThen", "unwrapOr", "isOk", "isErr"
    };

    public static readonly IReadOnlySet<string> OptionMethodNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "some", "none", "map", "andThen", "unwrapOr", "isSome", "isNone"
    };

    public static readonly IReadOnlySet<string> GroundedMethodNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "wrap"
    };

    public static readonly IReadOnlySet<string> CapMethodNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "fileRead", "fileWrite", "dirList",
        "is", "confine",
        "read", "write", "list"
    };

    public static readonly IReadOnlySet<string> IoMethodNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "print", "input",
        "readFile", "writeFile", "readFileBase64", "writeFileBase64", "readTextFileLines",
        "deleteFile", "hasFile", "hasDirectory", "ensureDir", "listDirectory",
        "glob", "grep", "replaceInFile",
        "pathExists", "pathJoin", "pathNormalize", "pathGetExtension", "isPathUnder",
        "getEnv", "getEnvOr", "hasEnv", "getFileName", "getDirectoryName",
        "hasEmbeddedFolder", "embeddedFolderRoot",
        "gitStatus", "gitAdd", "gitCommit", "gitDiff", "gitLog", "gitBranch", "gitCheckout", "gitPull", "gitPush"
    };

    /// <summary>
    /// pdf.* methods. CallBuiltIn uses <c>extractPdfText</c>; the module exposes <c>extractText</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> PdfMethodNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "extractText"
    };

    public static string ResolvePdfBuiltInName(string methodName) =>
        methodName == "extractText" ? "extractPdfText" : methodName;

    /// <summary>
    /// doc.* methods. CallBuiltIn uses <c>extractDocxText</c>; the module exposes <c>extractText</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> DocMethodNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "extractText"
    };

    public static string ResolveDocBuiltInName(string methodName) =>
        methodName == "extractText" ? "extractDocxText" : methodName;

    public static bool TryGetDeprecatedFlatAliasMessage(string flatName, out string message)
    {
        if (MathMethodNames.Contains(flatName))
        {
            message = $"Prefer 'math.{flatName}(...)' instead of '{flatName}(...)' (deprecated flat alias).";
            return true;
        }

        if (StrMethodNames.Contains(flatName))
        {
            message = $"Prefer 'str.{flatName}(...)' instead of '{flatName}(...)' (deprecated flat alias).";
            return true;
        }

        if (IoMethodNames.Contains(flatName))
        {
            message = $"Prefer 'io.{flatName}(...)' instead of '{flatName}(...)' (deprecated flat alias).";
            return true;
        }

        message = "";
        return false;
    }

    public static bool IsStdLibModuleMethod(string moduleName, string methodName) =>
        moduleName switch
        {
            MathModule or DeprecatedMathModuleAlias => MathMethodNames.Contains(methodName),
            StrModule => StrMethodNames.Contains(methodName),
            IoModule => IoMethodNames.Contains(methodName),
            PdfModule => PdfMethodNames.Contains(methodName),
            DocModule => DocMethodNames.Contains(methodName),
            ResultModule => ResultMethodNames.Contains(methodName),
            OptionModule => OptionMethodNames.Contains(methodName),
            GroundedModule => GroundedMethodNames.Contains(methodName),
            CapModule => CapMethodNames.Contains(methodName),
            _ => false
        };
}
