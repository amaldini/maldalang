// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

public class SchemaHoverAndSymbolTests
{
    private readonly LanguageService _language = new();
    private readonly SymbolNavigationService _symbols = new();

    [Fact]
    public void GetHoverInformation_SchemaName_ShowsFields()
    {
        const string source = """
schema Person {
    name: string;
    age: number?;
}
""";

        // Hover over "Person" in the schema declaration name (line 0, after "schema ").
        var hover = _language.GetHoverInformation(source, 0, 7);

        Assert.NotNull(hover);
        Assert.Contains("schema Person", hover);
        Assert.Contains("name: string", hover);
        Assert.Contains("age: number?", hover);
    }

    [Fact]
    public void GetHoverInformation_SumType_ShowsTypedPayloads()
    {
        const string source = """
type Intent = Search(query: string) | Buy(sku, qty);
""";

        var hover = _language.GetHoverInformation(source, 0, 5);

        Assert.NotNull(hover);
        Assert.Contains("type Intent", hover);
        Assert.Contains("Search(query: string)", hover);
        Assert.Contains("Buy(sku, qty)", hover);
    }

    [Fact]
    public void GetHoverInformation_TypeHintUsesSchemaName_ShowsSchema()
    {
        const string source = """
schema Person {
    name: string;
}

var p: Person = null;
""";

        // "Person" in `var p: Person` — line index 4.
        var hover = _language.GetHoverInformation(source, 4, 7);

        Assert.NotNull(hover);
        Assert.Contains("schema Person", hover);
    }

    [Fact]
    public void GetHoverInformation_ImportedSchema_ResolvesViaSourceFileName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_schema_hover_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var libPath = Path.Combine(tempDir, "lib.malda");
            File.WriteAllText(libPath, """
schema Person {
    name: string;
}
""");
            var mainPath = Path.Combine(tempDir, "main.malda");
            const string mainSource = """
import "./lib.malda";

var p: Person = null;
""";
            File.WriteAllText(mainPath, mainSource);

            var hover = _language.GetHoverInformation(mainSource, 2, 7, mainPath);

            Assert.NotNull(hover);
            Assert.Contains("schema Person", hover!);
            Assert.Contains("name: string", hover);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void GetDocumentSymbols_IncludesSchemaAndFields()
    {
        const string source = """
schema Person {
    name: string;
    age: number?;
}

function helper() {
    return 1;
}
""";

        var symbols = _symbols.GetDocumentSymbols(source, "person.malda");

        var schema = Assert.Single(symbols, s => s.Name == "Person");
        Assert.Equal(SymbolItemKind.Schema, schema.Kind);
        Assert.Contains(schema.Children, c => c.Name == "name" && c.Kind == SymbolItemKind.Field);
        Assert.Contains(schema.Children, c => c.Name == "age");
        Assert.Contains(symbols, s => s.Name == "helper" && s.Kind == SymbolItemKind.Function);
    }

    [Fact]
    public void GetWorkspaceSymbols_IncludesSchema()
    {
        const string source = """
schema Order {
    id: string;
}
""";
        var documents = new[]
        {
            new WorkspaceDocumentInfo { SourceKey = "order.malda", Text = source }
        };

        var symbols = _symbols.GetWorkspaceSymbols(documents, "Ord");

        var schema = Assert.Single(symbols);
        Assert.Equal("Order", schema.Name);
        Assert.Equal(SymbolItemKind.Schema, schema.Kind);
        Assert.Equal("order.malda", schema.Location.SourceKey);
    }
}
