// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.LanguageServer;
using MaldaLang.Linting;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// I6 <c>malda lint</c>: one positive and one negative fixture per rule, suppression comments,
/// the <c>--json</c> shape, the CLI exit codes, the LSP bridge, the gotchas-doc markers, and a
/// sweep over every <c>Examples/**/*.malda</c> for false positives.
/// </summary>
public class MaldaLinterTests
{
    private static List<LintFinding> Lint(string source) => MaldaLinter.Lint(source).Findings;

    private static List<string> Codes(string source) => Lint(source).Select(f => f.Code).ToList();

    public static TheoryData<string, string, string> RuleFixtures => new()
    {
        {
            LintRules.PlainStringInterpolation,
            "var user = { name: \"Ada\" };\nio.print(\"Hello {user.name}\");\n",
            "var user = { name: \"Ada\" };\nio.print($\"Hello {user.name}\");\nio.print(\"{other.name} {{user.name}} body{display:block;}\");\n"
        },
        {
            LintRules.UncheckedModelJson,
            "prompt ask(q) {\n    user: q\n}\nfunction run() {\n    var reply = await ask(\"x\");\n    var data = parseJSON(reply);\n    return data.answer;\n}\n",
            "schema Answer { answer: string; }\nprompt ask(q) {\n    user: q\n}\nfunction run() {\n    var reply = await ask(\"x\");\n    var data = parseJSON(reply);\n    var checked = validate(\"Answer\", data);\n    var config = parseJSON(io.readFile(\"c.json\"));\n    return checked;\n}\n"
        },
        {
            LintRules.ModeBPrompt,
            "schema Note { s: string; }\nprompt p(t) -> Note {\n    user: \"{t}\",\n    tools: [\"grep\"]\n}\n",
            "schema Note { s: string; }\nprompt p(t) -> Note {\n    gather: [\"grep\"];\n    user: \"{t}\"\n}\nprompt q(t) {\n    user: \"{t}\",\n    tools: [\"grep\"]\n}\n"
        },
        {
            LintRules.WorkflowEffectOutsideStep,
            "function load(p) {\n    return io.readFile(p);\n}\nworkflow W(input) {\n    var text = load(input);\n    return text;\n}\n",
            "function load(p) {\n    return io.readFile(p);\n}\nworkflow W(input) {\n    step text = load(input);\n    return text;\n}\n"
        },
        {
            LintRules.LambdaAssignmentDropped,
            "var items = [{ done: false }];\nitems.forEach(x => x.done = true);\n",
            "var items = [{ done: false }];\nitems.forEach(x => { x.done = true; });\nvar same = items.map(x => x.done == true);\n"
        },
        {
            LintRules.StepInLoop,
            "function work(i) {\n    return i;\n}\nworkflow W(input) {\n    for (var i = 0; i < 3; i = i + 1) {\n        step r = work(i);\n    }\n    return 0;\n}\n",
            "function work(n) {\n    var t = 0;\n    for (var i = 0; i < n; i = i + 1) { t = t + i; }\n    return t;\n}\nworkflow W(input) {\n    step r = work(3);\n    return r;\n}\n"
        },
        {
            LintRules.CaseBindsVar,
            "var BUY = 1;\nmatch 2 {\n    case BUY: io.print(\"buy\");\n    default: io.print(\"other\");\n}\n",
            "const BUY = 1;\nmatch 2 {\n    case BUY: io.print(\"buy\");\n    default: io.print(\"other\");\n}\n"
        },
        {
            LintRules.AlwaysTruthyCondition,
            "var items = [];\nif (items.length) {\n    io.print(\"non-empty\");\n}\n",
            "var items = [];\nif (items.length > 0) {\n    io.print(\"non-empty\");\n}\nwhile (true) { break; }\n"
        },
        {
            LintRules.MapReturnsWrapper,
            "var r = result.ok(1);\nvar r2 = result.map(r, (x) => result.ok(x + 1));\n",
            "var r = result.ok(1);\nvar r2 = result.andThen(r, (x) => result.ok(x + 1));\nvar r3 = result.map(r, (x) => x + 1);\n"
        },
        {
            LintRules.StringFunctionOnGetEnv,
            "var home = str.trim(io.getEnv(\"HOME\"));\n",
            "var home = str.trim(io.getEnvOr(\"HOME\", \"\"));\nvar other = str.trimText(io.getEnv(\"HOME\"));\n"
        },
        {
            LintRules.RepeatFractionalCount,
            "var n = 5;\nio.print(str.repeat(\"-\", n / 2));\n",
            "var n = 5;\nio.print(str.repeat(\"-\", int(n / 2)));\n"
        },
        {
            LintRules.DispatchEventNumericSession,
            "ui.dispatchEvent({ type: \"click\" }, 1);\n",
            "ui.dispatchEvent({ type: \"click\" }, \"s1\", 1);\n"
        },
        {
            LintRules.UnreachableCaseAfterCatchAll,
            "var v = 5;\nmatch v {\n    case x: io.print(\"any\");\n    case 5: io.print(\"five\");\n}\n",
            "var v = 5;\nmatch v {\n    case x if x > 10: io.print(\"big\");\n    case 5: io.print(\"five\");\n    case y: io.print(\"any\");\n}\n"
        },
    };

    [Theory]
    [MemberData(nameof(RuleFixtures))]
    public void Rule_ReportsPositiveFixture_AndNotNegative(string code, string positive, string negative)
    {
        var positiveCodes = Codes(positive);
        Assert.True(positiveCodes.Contains(code), $"{code} missing on positive fixture; got [{string.Join(", ", positiveCodes)}]");
        Assert.DoesNotContain(LintRules.ParseFailed, positiveCodes);

        var negativeCodes = Codes(negative);
        Assert.DoesNotContain(LintRules.ParseFailed, negativeCodes);
        Assert.True(!negativeCodes.Contains(code), $"{code} reported on negative fixture: [{string.Join(", ", negativeCodes)}]");
    }

    [Fact]
    public void EveryRule_HasAFixture()
    {
        var covered = RuleFixtures.Select(row => (string)row[0]).ToHashSet();
        var missing = LintRules.All.Select(r => r.Code).Where(c => c != LintRules.ParseFailed && !covered.Contains(c)).ToList();
        Assert.True(missing.Count == 0, "Rules without a fixture: " + string.Join(", ", missing));
    }

    [Fact]
    public void Rules_HaveUniqueSequentialCodes()
    {
        var codes = LintRules.All.Select(r => r.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        for (var i = 0; i < codes.Count; i++)
            Assert.Equal($"ML{i:D4}", codes[i]);
    }

    [Fact]
    public void Finding_HasOneBasedPosition()
    {
        var finding = Assert.Single(Lint("var n = 5;\nio.print(str.repeat(\"-\", n / 2));\n"));
        Assert.Equal(LintRules.RepeatFractionalCount, finding.Code);
        Assert.Equal(2, finding.Line);
        Assert.True(finding.Column > 1);
    }

    [Fact]
    public void WorkflowRule_FollowsHelpers_AndSkipsDenyListAndSteps()
    {
        const string source = """
            prompt solve(expr) {
                user: expr
            }
            function stage(x) {
                return helper(x);
            }
            function helper(x) {
                return solve(x);
            }
            workflow W(input) {
                var a = stage(input);
                var t = now();
                step b = stage(input);
                return a;
            }
            """;
        var findings = Lint(source).Where(f => f.Code == LintRules.WorkflowEffectOutsideStep).ToList();
        var finding = Assert.Single(findings);
        Assert.Contains("'stage' → 'helper'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("solve", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseFailure_IsReportedAsMl0000_AndRulesDoNotRun()
    {
        var result = MaldaLinter.Lint("var x = ;\nif (0) { }\n");
        Assert.False(result.Parsed);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(LintRules.ParseFailed, finding.Code);
        Assert.Equal(LintSeverity.Error, finding.Severity);
    }

    [Fact]
    public void Suppression_NextLine_Line_File_AndAllRules()
    {
        const string body = "var n = 5;\n";
        const string bad = "io.print(str.repeat(\"-\", n / 2));\n";

        Assert.Single(Lint(body + bad));

        var nextLine = MaldaLinter.Lint(body + "// malda-lint-disable-next-line ML0011 -- odd widths are fine here\n" + bad);
        Assert.Empty(nextLine.Findings);
        Assert.Equal(1, nextLine.SuppressedCount);

        Assert.Empty(Lint(body + bad.TrimEnd('\n') + " // malda-lint-disable-line ML0011\n"));
        Assert.Empty(Lint("// malda-lint-disable-file ML0011\n" + body + bad + bad));
        Assert.Empty(Lint(body + "/* malda-lint-disable-next-line */\n" + bad));
        Assert.Empty(Lint(body + "// malda-lint-disable-next-line ML0008, ML0011\n" + bad));

        // A different code, a directive two lines up, or one inside a string does not suppress.
        Assert.Single(Lint(body + "// malda-lint-disable-next-line ML0001\n" + bad));
        Assert.Single(Lint(body + "// malda-lint-disable-next-line ML0011\n\n" + bad));
        Assert.Single(Lint(body + "var s = \"// malda-lint-disable-file\";\n" + bad));
    }

    [Fact]
    public void Cli_Json_HasCheckShape()
    {
        var runner = new LintCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = runner.Run(new[] { "--stdin", "--json" }, output, error, new StringReader("var n = 5;\nio.print(str.repeat(\"-\", n / 2));\n"));

        Assert.Equal(LintCommandRunner.ExitFindings, exit);
        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.False(root.GetProperty("executed").GetBoolean());
        Assert.Equal("<stdin>", root.GetProperty("file").GetString());
        Assert.Equal(1, root.GetProperty("fileCount").GetInt32());
        Assert.Equal(0, root.GetProperty("errorCount").GetInt32());
        Assert.Equal(1, root.GetProperty("warningCount").GetInt32());
        Assert.Equal(0, root.GetProperty("infoCount").GetInt32());
        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("warning", diagnostic.GetProperty("severity").GetString());
        Assert.Equal("ML0011", diagnostic.GetProperty("code").GetString());
        Assert.Equal(2, diagnostic.GetProperty("line").GetInt32());
        Assert.True(diagnostic.GetProperty("column").GetInt32() >= 1);
        Assert.True(diagnostic.GetProperty("length").GetInt32() >= 1);
        Assert.False(string.IsNullOrEmpty(diagnostic.GetProperty("message").GetString()));
        Assert.Contains("Gotcha:", diagnostic.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(diagnostic.GetProperty("suggestedFix").GetString()));
    }

    [Fact]
    public void Cli_ExitCodes_InfoDoesNotFail_UsageIsTwo()
    {
        var runner = new LintCommandRunner();
        const string modeB = "schema Note { s: string; }\nprompt p(t) -> Note {\n    user: \"{t}\",\n    tools: [\"grep\"]\n}\n";
        var output = new StringWriter();
        Assert.Equal(LintCommandRunner.ExitOk, runner.Run(new[] { "--stdin" }, output, new StringWriter(), new StringReader(modeB)));
        Assert.Contains("info[ML0003]", output.ToString(), StringComparison.Ordinal);

        Assert.Equal(LintCommandRunner.ExitOk, runner.Run(new[] { "--stdin" }, new StringWriter(), new StringWriter(), new StringReader("io.print(1);\n")));
        Assert.Equal(LintCommandRunner.ExitFindings, runner.Run(new[] { "--stdin" }, new StringWriter(), new StringWriter(), new StringReader("var x = ;\n")));
        Assert.Equal(LintCommandRunner.ExitUsage, runner.Run(new[] { "--bogus" }, new StringWriter(), new StringWriter()));
        Assert.Equal(LintCommandRunner.ExitUsage, runner.Run(Array.Empty<string>(), new StringWriter(), new StringWriter()));
        Assert.Equal(LintCommandRunner.ExitUsage, runner.Run(new[] { "no/such/file.malda" }, new StringWriter(), new StringWriter()));

        var json = new StringWriter();
        Assert.Equal(LintCommandRunner.ExitUsage, runner.Run(new[] { "--bogus", "--json" }, json, new StringWriter()));
        using var doc = JsonDocument.Parse(json.ToString());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public void Cli_ListRules_PrintsEveryCode()
    {
        var output = new StringWriter();
        Assert.Equal(LintCommandRunner.ExitOk, new LintCommandRunner().Run(new[] { "--list-rules" }, output, new StringWriter()));
        foreach (var rule in LintRules.All)
            Assert.Contains(rule.Code, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_Directory_LintsEveryFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MaldaLint_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "a.malda"), "io.print(1);\n");
            File.WriteAllText(Path.Combine(dir, "sub", "b.malda"), "var n = 5;\nio.print(str.repeat(\"-\", n / 2));\n");
            var output = new StringWriter();
            var exit = new LintCommandRunner().Run(new[] { dir, "--json" }, output, new StringWriter());
            Assert.Equal(LintCommandRunner.ExitFindings, exit);
            using var doc = JsonDocument.Parse(output.ToString());
            Assert.Equal(2, doc.RootElement.GetProperty("fileCount").GetInt32());
            Assert.False(doc.RootElement.TryGetProperty("file", out _));
            var diagnostic = Assert.Single(doc.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.EndsWith("b.malda", diagnostic.GetProperty("file").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Lsp_PublishesLintFindingsWithCode_AndSkipsParseFailure()
    {
        var diagnostics = MaldaTextDocumentSyncHandler.LintDiagnostics("var n = 5;\nio.print(str.repeat(\"-\", n / 2));\n", null).ToList();
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("malda-lint", diagnostic.Source);
        Assert.Equal("ML0011", diagnostic.Code?.String);
        Assert.Equal(1, diagnostic.Range.Start.Line);

        Assert.Empty(MaldaTextDocumentSyncHandler.LintDiagnostics("var x = ;\n", null));
    }

    /// <summary>
    /// False-positive sweep. Files the parser rejects get ML0000 (malda check reports the same
    /// parser errors). Any other warning or error is a false positive and fails the test.
    /// Info findings are allowed only where listed with a reason.
    /// </summary>
    [Fact]
    public void Examples_HaveNoFalsePositives()
    {
        var justifiedInfo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // The canonical Mode B demo: ML0003 is the advisory this file exists to illustrate.
            ["Examples/Prompts/prompt_tools_mode.malda|ML0003"] = "Mode B demonstration",
        };

        var files = Directory.EnumerateFiles(Path.Combine(PlanningPaths.RepoRoot, "Examples"), "*.malda", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.True(files.Count > 200);

        var unexpected = new List<string>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(PlanningPaths.RepoRoot, file).Replace('\\', '/');
            foreach (var finding in MaldaLinter.Lint(File.ReadAllText(file), file).Findings)
            {
                if (finding.Code == LintRules.ParseFailed)
                    continue;
                if (finding.Severity == LintSeverity.Info && justifiedInfo.ContainsKey($"{relative}|{finding.Code}"))
                    continue;
                unexpected.Add($"{relative}:{finding.Line}:{finding.Column} {finding.Code} {finding.Message}");
            }
        }

        Assert.True(unexpected.Count == 0, "Lint findings on Examples/ (fix the rule):\n" + string.Join("\n", unexpected));
    }

    /// <summary>Roadmap I6 done-when: every gotcha row links to a rule or is marked docs-only.</summary>
    [Fact]
    public void GotchasDoc_EveryTableRowHasLintMarker_AndEveryRuleIsReferenced()
    {
        var path = PlanningPaths.ResolveRepoPath("docs", "llm", "malda-gotchas.md");
        var lines = File.ReadAllLines(path);
        var marker = new Regex(@"(ML\d{4}|docs-only|check: [A-Za-z0-9-]+|compile: JS\d{4})", RegexOptions.CultureInvariant);
        var missing = new List<string>();
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        string? header = null;
        foreach (var line in lines)
        {
            if (!line.StartsWith("|", StringComparison.Ordinal))
            {
                header = null;
                continue;
            }

            if (header == null)
            {
                header = line;
                Assert.True(line.TrimEnd().EndsWith("| Lint |", StringComparison.Ordinal), "Gotcha table without a Lint column: " + line);
                continue;
            }

            if (line.StartsWith("|---", StringComparison.Ordinal))
                continue;

            var cells = line.Trim().Trim('|').Split('|');
            var last = cells[^1];
            if (!marker.IsMatch(last))
                missing.Add(line.Length > 80 ? line[..80] : line);
            foreach (Match m in Regex.Matches(last, @"ML\d{4}"))
                referenced.Add(m.Value);
        }

        // Paragraph gotchas ("Half-truths") end with an italic *Lint: …* marker.
        var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (Match m in Regex.Matches(text, @"\*Lint: ([^*]+)\*"))
        {
            foreach (Match code in Regex.Matches(m.Groups[1].Value, @"ML\d{4}"))
                referenced.Add(code.Value);
        }

        var halfTruths = text[text.IndexOf("## Half-truths", StringComparison.Ordinal)..text.IndexOf("## Before you say it works", StringComparison.Ordinal)];
        foreach (var paragraph in halfTruths.Split("\n\n").Select(p => p.Trim()).Where(p => p.StartsWith("**", StringComparison.Ordinal)))
        {
            if (!paragraph.Contains("*Lint: ", StringComparison.Ordinal))
                missing.Add(paragraph.Length > 80 ? paragraph[..80] : paragraph);
        }

        Assert.True(missing.Count == 0, "Gotchas without a lint marker:\n" + string.Join("\n", missing));
        foreach (var unknown in referenced.Where(c => !LintRules.IsKnown(c)))
            Assert.Fail($"malda-gotchas.md references unknown rule {unknown}");

        var unreferenced = LintRules.All.Select(r => r.Code).Where(c => c != LintRules.ParseFailed && !referenced.Contains(c)).ToList();
        Assert.True(unreferenced.Count == 0, "Rules not linked from malda-gotchas.md: " + string.Join(", ", unreferenced));
    }
}
