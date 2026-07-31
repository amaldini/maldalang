// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;

namespace MaldaLang.Compiler;

internal static class TemplatePreprocessor
{
    private const string ExpressionOpen = "{{";
    private const string ExpressionClose = "}}";
    private const string StatementOpen = "{%";
    private const string StatementClose = "%}";
    private const string HtmlBufferName = "__maldaTemplateHtml";

    public static bool IsTemplatePath(string? sourcePath)
    {
        return !string.IsNullOrWhiteSpace(sourcePath) &&
               sourcePath.EndsWith(".malda.html", StringComparison.OrdinalIgnoreCase);
    }

    public static string Preprocess(string templateSource, string? sourcePath)
    {
        var output = new StringBuilder();
        output.AppendLine("function renderRoot(rootSelector) {");
        output.AppendLine($"    var {HtmlBufferName} = \"\";");

        var index = 0;
        while (index < templateSource.Length)
        {
            var nextExpression = templateSource.IndexOf(ExpressionOpen, index, StringComparison.Ordinal);
            var nextStatement = templateSource.IndexOf(StatementOpen, index, StringComparison.Ordinal);

            var nextTokenIndex = MinPositive(nextExpression, nextStatement);
            if (nextTokenIndex < 0)
            {
                AppendHtmlSegment(output, templateSource.Substring(index));
                index = templateSource.Length;
                continue;
            }

            if (nextTokenIndex > index)
            {
                AppendHtmlSegment(output, templateSource.Substring(index, nextTokenIndex - index));
            }

            if (nextTokenIndex == nextExpression)
            {
                var expressionStart = nextTokenIndex + ExpressionOpen.Length;
                var expressionEnd = templateSource.IndexOf(ExpressionClose, expressionStart, StringComparison.Ordinal);
                if (expressionEnd < 0)
                {
                    throw BuildTemplateParseException("Unclosed interpolation block '{{ ... }}'.", templateSource, sourcePath, nextTokenIndex);
                }

                var expression = templateSource.Substring(expressionStart, expressionEnd - expressionStart).Trim();
                if (expression.Length == 0)
                {
                    throw BuildTemplateParseException("Empty interpolation block '{{ }}' is not allowed.", templateSource, sourcePath, nextTokenIndex);
                }

                output.Append("    ");
                output.Append(HtmlBufferName);
                output.Append(" = ");
                output.Append(HtmlBufferName);
                output.Append(" + (");
                output.Append(expression);
                output.AppendLine(");");

                index = expressionEnd + ExpressionClose.Length;
                continue;
            }

            var statementStart = nextTokenIndex + StatementOpen.Length;
            var statementEnd = templateSource.IndexOf(StatementClose, statementStart, StringComparison.Ordinal);
            if (statementEnd < 0)
            {
                throw BuildTemplateParseException("Unclosed statement block '{% ... %}'.", templateSource, sourcePath, nextTokenIndex);
            }

            var statementBlock = templateSource.Substring(statementStart, statementEnd - statementStart).Trim();
            if (statementBlock.Length == 0)
            {
                throw BuildTemplateParseException("Empty statement block '{% %}' is not allowed.", templateSource, sourcePath, nextTokenIndex);
            }

            AppendStatementBlock(output, statementBlock);
            index = statementEnd + StatementClose.Length;
        }

        output.Append("    dom.html(rootSelector, ");
        output.Append(HtmlBufferName);
        output.AppendLine(");");
        output.AppendLine("}");
        output.AppendLine();
        output.AppendLine("function bootstrap(rootSelector) {");
        output.AppendLine("    if (rootSelector == null || rootSelector == \"\") {");
        output.AppendLine("        rootSelector = \"#app\";");
        output.AppendLine("    }");
        output.AppendLine("    renderRoot(rootSelector);");
        output.AppendLine("}");

        return output.ToString();
    }

    private static void AppendHtmlSegment(StringBuilder output, string htmlSegment)
    {
        if (htmlSegment.Length == 0)
        {
            return;
        }

        output.Append("    ");
        output.Append(HtmlBufferName);
        output.Append(" = ");
        output.Append(HtmlBufferName);
        output.Append(" + \"");
        output.Append(EscapeMaldaString(htmlSegment));
        output.AppendLine("\";");
    }

    private static void AppendStatementBlock(StringBuilder output, string statementBlock)
    {
        var lines = statementBlock.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            output.Append("    ");
            output.AppendLine(line);
        }
    }

    private static string EscapeMaldaString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    private static Exception BuildTemplateParseException(string message, string templateSource, string? sourcePath, int errorIndex)
    {
        var (line, column) = ComputeLineColumn(templateSource, errorIndex);
        var context = ExtractContext(templateSource, errorIndex);
        var pathPart = string.IsNullOrWhiteSpace(sourcePath) ? "<template>" : sourcePath;
        return new Exception($"{message} At {pathPart}:{line}:{column}. Context: \"{context}\"");
    }

    private static (int Line, int Column) ComputeLineColumn(string source, int index)
    {
        var line = 1;
        var column = 1;

        for (var i = 0; i < source.Length && i < index; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private static string ExtractContext(string source, int index)
    {
        const int window = 30;
        var start = Math.Max(0, index - window);
        var length = Math.Min(source.Length - start, window * 2);
        var rawContext = source.Substring(start, length);
        return rawContext
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static int MinPositive(int a, int b)
    {
        if (a < 0) return b;
        if (b < 0) return a;
        return Math.Min(a, b);
    }
}
