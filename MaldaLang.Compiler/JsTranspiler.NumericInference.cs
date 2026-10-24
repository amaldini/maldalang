// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

/// <summary>
/// Numeric variables (J3): names that hold a JavaScript number wherever they are visible, so
/// arithmetic and comparisons on them are plain JavaScript instead of
/// <c>mlRuntime.coerceToFloat</c> calls (which return a number unchanged).
/// <para>
/// The analysis is by name over the whole program, which keeps it simple and conservative: a
/// name is numeric only if every <c>var</c> and every assignment of that name anywhere has a
/// value that is a number by construction (a numeric literal, <c>- * / %</c>, <c>+</c> of two
/// numbers, <c>int()</c> / <c>float()</c>, a <c>math</c> function that always returns a number,
/// another numeric name), and the name is never a parameter, a loop or catch variable, a
/// pattern binding, a field, a declaration or an import. Iterated to a fixpoint.
/// </para>
/// </summary>
public partial class JsTranspiler
{
    private readonly HashSet<string> _numericNames = new(StringComparer.Ordinal);

    /// <summary>
    /// Names that always hold a <c>buf.*</c> buffer (every definition is a buffer constructor,
    /// <c>buf.from</c>, <c>buf.slice</c> or <c>buf.fill</c>): <c>b[i]</c> on them is a number.
    /// </summary>
    private readonly HashSet<string> _bufferNames = new(StringComparer.Ordinal);

    private static readonly HashSet<string> BufferProducingMembers = new(StringComparer.Ordinal)
    {
        "float64", "float32", "int32", "bytes", "from", "slice", "fill", "fromUtf8", "fromBase64"
    };

    /// <summary><c>math.*</c> members that return a number for any argument (they coerce first).</summary>
    private static readonly HashSet<string> NumericMathMembers = new(StringComparer.Ordinal)
    {
        "pow", "sqrt", "floor", "ceil", "trunc", "sign", "exp", "log", "log10", "log2",
        "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "round"
    };

    private void InferNumericNames(IReadOnlyList<Statement> statements)
    {
        _numericNames.Clear();
        _bufferNames.Clear();
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new Dictionary<string, List<Expression?>>(StringComparer.Ordinal);

        void Define(string name, Expression? value)
        {
            if (!definitions.TryGetValue(name, out var values))
                definitions[name] = values = new List<Expression?>();
            values.Add(value);
        }

        foreach (var node in statements.SelectMany(statement => AstNodeWalker.Descendants(statement)))
        {
            switch (node)
            {
                case VarDeclStatement variable:
                    Define(variable.Name, variable.Initializer);
                    break;
                case AssignmentStatement { Target: IdentifierExpression target } assignment:
                    // x -= v, x *= v, x /= v are numbers whatever v is; x += v only for a numeric v.
                    Define(target.Name, assignment.Operator switch
                    {
                        TokenType.Assign or TokenType.PlusAssign => assignment.Value,
                        _ => new LiteralExpression(0)
                    });
                    break;
                case FunctionDeclaration function:
                    excluded.Add(function.Name);
                    excluded.UnionWith(function.Parameters);
                    break;
                case LambdaExpression lambda:
                    excluded.UnionWith(lambda.Parameters);
                    break;
                case PropertyDeclaration property:
                    excluded.UnionWith(property.Parameters);
                    break;
                case ClassDeclaration classDeclaration:
                    excluded.Add(classDeclaration.Name);
                    excluded.UnionWith(classDeclaration.Members.Select(member => member.Name));
                    break;
                case ActorDeclaration actor:
                    excluded.Add(actor.Name);
                    excluded.UnionWith(actor.Members.Select(member => member.Name));
                    break;
                case TypeDeclaration type:
                    excluded.UnionWith(type.Constructors.Select(constructor => constructor.Name));
                    break;
                case ForInStatement forIn:
                    excluded.Add(forIn.VariableName);
                    break;
                case CatchClause { ExceptionVariable: { } exceptionVariable }:
                    excluded.Add(exceptionVariable);
                    break;
                case IdentifierPattern pattern:
                    excluded.Add(pattern.Name);
                    break;
                case ListComprehensionExpression list:
                    excluded.Add(list.Variable);
                    break;
                case DictComprehensionExpression dict:
                    excluded.Add(dict.Variable);
                    break;
                case UsingResourceStatement usingResource:
                    excluded.Add(usingResource.VariableName);
                    break;
                case ImportStatement import:
                    if (import.Alias != null)
                        excluded.Add(import.Alias);
                    if (import.SelectedNames != null)
                        excluded.UnionWith(import.SelectedNames);
                    break;
                case JsImportStatement jsImport:
                    excluded.UnionWith(jsImport.Names);
                    break;
            }
        }

        var mathIsBuiltIn = !excluded.Contains(StdLibNamespaces.MathModule) && !definitions.ContainsKey(StdLibNamespaces.MathModule);
        var bufIsBuiltIn = !excluded.Contains(StdLibNamespaces.BufModule) && !definitions.ContainsKey(StdLibNamespaces.BufModule);
        if (bufIsBuiltIn)
        {
            // Fixpoint like the numeric names: `next = swap` keeps a buffer if `swap` is one.
            _bufferNames.UnionWith(definitions.Keys.Where(name => !excluded.Contains(name)));
            bool bufferChanged;
            do
            {
                bufferChanged = false;
                foreach (var name in _bufferNames.ToList())
                {
                    if (definitions[name].All(value => value != null && IsBufferValue(value, _bufferNames)))
                        continue;
                    _bufferNames.Remove(name);
                    bufferChanged = true;
                }
            } while (bufferChanged);
        }

        var candidates = new HashSet<string>(definitions.Keys.Where(name => !excluded.Contains(name)), StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var name in candidates.ToList())
            {
                if (definitions[name].All(value => value != null && IsNumberByConstruction(value, candidates, mathIsBuiltIn, bufIsBuiltIn, _bufferNames)))
                    continue;
                candidates.Remove(name);
                changed = true;
            }
        } while (changed);

        _numericNames.UnionWith(candidates);
    }

    private static bool IsBufferValue(Expression expression, IReadOnlySet<string> bufferNames) => expression switch
    {
        FunctionCallExpression
        {
            Callee: MemberAccessExpression { Object: IdentifierExpression { Name: StdLibNamespaces.BufModule }, Member: var member }
        } => BufferProducingMembers.Contains(member),
        IdentifierExpression identifier => bufferNames.Contains(identifier.Name),
        _ => false
    };

    private static bool IsNumberByConstruction(
        Expression expression,
        IReadOnlySet<string> numericNames,
        bool mathIsBuiltIn,
        bool bufIsBuiltIn,
        IReadOnlySet<string> bufferNames)
    {
        bool Numeric(Expression e) => IsNumberByConstruction(e, numericNames, mathIsBuiltIn, bufIsBuiltIn, bufferNames);
        return expression switch
        {
            LiteralExpression { Value: int or long or double } => true,
            IdentifierExpression identifier => numericNames.Contains(identifier.Name),
            UnaryExpression { Operator: TokenType.Minus or TokenType.Plus } => true,
            UnaryExpression { Operator: TokenType.Increment or TokenType.Decrement } unary => Numeric(unary.Right),
            PostfixExpression postfix => Numeric(postfix.Left),
            BinaryExpression binary => binary.Operator switch
            {
                TokenType.Minus or TokenType.Multiply or TokenType.Divide or TokenType.Modulo => true,
                TokenType.Plus => Numeric(binary.Left) && Numeric(binary.Right),
                _ => false
            },
            TernaryExpression ternary => Numeric(ternary.ThenBranch) && Numeric(ternary.ElseBranch),
            FunctionCallExpression { Callee: IdentifierExpression { Name: "int" or "float", ShadowsBuiltIn: false } } => true,
            FunctionCallExpression { Callee: MemberAccessExpression { Object: IdentifierExpression { Name: StdLibNamespaces.MathModule }, Member: var member } }
                => mathIsBuiltIn && NumericMathMembers.Contains(member),
            FunctionCallExpression { Callee: MemberAccessExpression { Object: IdentifierExpression { Name: StdLibNamespaces.BufModule }, Member: "readAt" } }
                => bufIsBuiltIn,
            ArrayAccessExpression { IsNullConditional: false, Array: IdentifierExpression buffer } => bufferNames.Contains(buffer.Name),
            _ => false
        };
    }
}
