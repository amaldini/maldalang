// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

/// <summary>Shape of the generated JavaScript file.</summary>
public enum JsModuleFormat
{
    /// <summary>
    /// A classic script: an IIFE that registers <c>globalThis.MaldaApp</c> (and
    /// <c>module.exports</c> under CommonJS). The host calls <c>MaldaApp.main()</c>.
    /// </summary>
    Script,

    /// <summary>
    /// An ES module (<c>--mode esm</c>): imports the runtime, runs the program's top-level code
    /// when the module is evaluated, and exports its declarations so JavaScript can
    /// <c>import { f } from "./lib.mjs"</c>.
    /// </summary>
    EsModule
}

/// <summary>ES module output (J2): which declarations a module exports, and its footer.</summary>
public partial class JsTranspiler
{
    public JsModuleFormat ModuleFormat { get; set; } = JsModuleFormat.Script;

    /// <summary>
    /// What an ES module imports for the runtime. Modules that resolve it to the same file share
    /// one runtime instance (module records are cached per URL).
    /// </summary>
    public string RuntimeModuleSpecifier { get; set; } = "./malda-js-runtime.js";

    /// <summary>
    /// The names an ES module exports, with the rule MALDA modules use for <c>import</c>: the
    /// <c>export</c>-marked declarations if the file has any, otherwise every top-level function,
    /// class, actor, variable and variant constructor. Only declarations of the compiled file
    /// count, not those inlined from the <c>.malda</c> files it imports.
    /// </summary>
    private List<string> CollectModuleExports(IReadOnlyList<Statement> statements, string? sourceFilePath)
    {
        var own = statements.Where(statement => IsFromFile(statement, sourceFilePath)).ToList();
        var hasExplicitExports = own.Any(statement => statement switch
        {
            FunctionDeclaration function => function.IsExported,
            VarDeclStatement variable => variable.IsExported,
            ClassDeclaration classDeclaration => classDeclaration.IsExported,
            TypeDeclaration type => type.IsExported,
            SchemaDeclaration schema => schema.IsExported,
            _ => false
        });

        var names = new List<string>();
        void Add(string name)
        {
            if (name != "main" && !_jsImportNames.Contains(name) && !names.Contains(name))
                names.Add(name);
        }

        foreach (var statement in own)
        {
            switch (statement)
            {
                case FunctionDeclaration function when !ShaderFunction.IsMarked(function) && (!hasExplicitExports || function.IsExported):
                    Add(function.Name);
                    break;
                case ClassDeclaration classDeclaration when !hasExplicitExports || classDeclaration.IsExported:
                    Add(classDeclaration.Name);
                    break;
                case VarDeclStatement variable when !hasExplicitExports || variable.IsExported:
                    Add(variable.Name);
                    break;
                case TypeDeclaration type when !hasExplicitExports || type.IsExported:
                    foreach (var constructor in type.Constructors)
                        Add(constructor.Name);
                    break;
                case ActorDeclaration actor when !hasExplicitExports:
                    Add(actor.Name);
                    break;
            }
        }

        return names;
    }

    private static bool IsFromFile(Node node, string? sourceFilePath)
    {
        if (string.IsNullOrEmpty(node.SourceFile) || string.IsNullOrEmpty(sourceFilePath))
            return true;
        return string.Equals(
            Path.GetFullPath(node.SourceFile),
            Path.GetFullPath(sourceFilePath),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs the program when the module is evaluated (an importer continues once it finished,
    /// so exported variables hold their values), then exports the declarations.
    /// </summary>
    private void EmitEsModuleFooter(List<string> exports)
    {
        EmitLine("await main();");
        if (exports.Count > 0)
        {
            EmitLine(string.Empty);
            EmitLine($"export {{ {string.Join(", ", exports.Select(EscapeIdentifier))} }};");
        }
    }
}
