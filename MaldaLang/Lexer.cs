// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang;

public class LexerInterpolatedStringSegment
{
    public bool IsExpression { get; }
    public string Content { get; }
    
    public LexerInterpolatedStringSegment(bool isExpression, string content)
    {
        IsExpression = isExpression;
        Content = content;
    }
}

public class Lexer
{
    private readonly string _source;
    private readonly string? _sourceFileName;
    private readonly int _lineOffset;
    private int _start = 0;
    private int _current = 0;
    private int _line = 1;
    private int _column = 1;
    
    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "if", TokenType.If },
        { "else", TokenType.Else },
        { "while", TokenType.While },
        { "for", TokenType.For },
        { "foreach", TokenType.Foreach },
        { "function", TokenType.Function },
        { "fn", TokenType.Function },
        { "def", TokenType.Function },
        { "component", TokenType.Component },
        { "return", TokenType.Return },
        { "var", TokenType.Var },
        { "const", TokenType.Const },
        { "print", TokenType.Print },
        { "input", TokenType.Input },
        { "true", TokenType.True },
        { "false", TokenType.False },
        { "and", TokenType.And },
        { "or", TokenType.Or },
        { "not", TokenType.Not },
        { "break", TokenType.Break },
        { "continue", TokenType.Continue },
        { "try", TokenType.Try },
        { "catch", TokenType.Catch },
        { "finally", TokenType.Finally },
        { "throw", TokenType.Throw },
        { "defer", TokenType.Defer },
        { "class", TokenType.Class },
        { "new", TokenType.New },
        { "this", TokenType.This },
        { "super", TokenType.Super },
        { "extends", TokenType.Extends },
        { "public", TokenType.Public },
        { "private", TokenType.Private },
        { "static", TokenType.Static },
        { "null", TokenType.Null },
        { "actor", TokenType.Actor },
        { "message", TokenType.Message },
        { "spawn", TokenType.Spawn },
        { "send", TokenType.Send },
        { "receive", TokenType.Receive },
        { "self", TokenType.Self },
        { "on", TokenType.On },
        { "then", TokenType.Then },
        { "timeout", TokenType.Timeout },
        { "dict", TokenType.Dict },
        { "graph", TokenType.Graph },
        { "directed", TokenType.Directed },
        { "undirected", TokenType.Undirected },
        { "in", TokenType.In },
        { "using", TokenType.Using },
        { "import", TokenType.Import },
        { "export", TokenType.Export },
        { "include", TokenType.Include },
        { "prompt", TokenType.Prompt },
        { "property", TokenType.Property },
        { "match", TokenType.Match },
        { "case", TokenType.Case },
        { "default", TokenType.Default },
        { "type", TokenType.Type },
        { "schema", TokenType.Schema },
        { "api", TokenType.Api },
        { "await", TokenType.Await },
        { "async", TokenType.Async },
        { "workflow", TokenType.Workflow },
        { "step", TokenType.Step },
        { "approval", TokenType.Approval },
        { "wait", TokenType.Wait },
        { "retry", TokenType.Retry },
        { "backoff", TokenType.Backoff },
        { "delay", TokenType.Delay },
        { "maxDelay", TokenType.MaxDelay },
        { "compensate", TokenType.Compensate },
        { "onReject", TokenType.OnReject }
    };
    
    public Lexer(string source, string? sourceFileName = null, int lineOffset = 0)
    {
        _source = source;
        _sourceFileName = sourceFileName;
        _lineOffset = lineOffset;
    }
    
    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        
        while (!IsAtEnd())
        {
            _start = _current;
            var token = ScanToken();
            if (token != null)
            {
                tokens.Add(token);
            }
        }
        
        tokens.Add(new Token(TokenType.EOF, "", null, _line, _column));
        return tokens;
    }
    
    private Token? ScanToken()
    {
        char c = Advance();
        
        switch (c)
        {
            // Single character tokens
            case '(': return CreateToken(TokenType.LeftParen);
            case ')': return CreateToken(TokenType.RightParen);
            case '{': return CreateToken(TokenType.LeftBrace);
            case '}': return CreateToken(TokenType.RightBrace);
            case '[': return CreateToken(TokenType.LeftBracket);
            case ']': return CreateToken(TokenType.RightBracket);
            case ',': return CreateToken(TokenType.Comma);
            case '.': return CreateToken(TokenType.Dot);
            case ';': return CreateToken(TokenType.Semicolon);
            case '@': return CreateToken(TokenType.At);
            case '+': return HandlePlus();
            case '*': return HandleMultiply();
            case '/': return HandleSlash();
            case '%': return CreateToken(TokenType.Modulo);
            case '?':
                return Match('?')
                    ? CreateToken(TokenType.NullCoalesce)
                    : CreateToken(TokenType.QuestionMark);
            case ':': return CreateToken(TokenType.Colon);
            case '_':
                // Keep standalone '_' for wildcard patterns, but allow identifiers like '__add__'.
                if (IsAlphaNumeric(Peek()))
                    return Identifier();
                return CreateToken(TokenType.Underscore);
            
            // One or two character tokens
            case '-': return HandleMinus();
            case '=': return HandleAssign();
            case '!': return Match('=') ? CreateToken(TokenType.NotEqual) : CreateToken(TokenType.Not);
            case '<': 
                return Match('=') ? CreateToken(TokenType.LessThanOrEqual) : CreateToken(TokenType.LessThan);
            case '>': return Match('=') ? CreateToken(TokenType.GreaterThanOrEqual) : CreateToken(TokenType.GreaterThan);
            case '&': return Match('&') ? CreateToken(TokenType.And) : throw Error("Unexpected character: &. Use && for logical AND.");
            case '|':
                if (Match('|'))
                    return CreateToken(TokenType.Or);
                if (Match('>'))
                    return CreateToken(TokenType.PipeForward);
                return CreateToken(TokenType.Pipe);
            
            // Whitespace
            case ' ':
            case '\r':
            case '\t':
                return null; // Ignore whitespace
            
            case '\n':
                _line++;
                _column = 1;
                return null;
            
            // Interpolated string literals (must check before regular strings)
            case '$':
                if (Peek() == '"')
                {
                    // Check for triple quotes: $"""
                    if (PeekNext() == '"')
                    {
                        // Check one more character ahead
                        if (_current + 2 < _source.Length && _source[_current + 2] == '"')
                        {
                            return InterpolatedTripleQuotedString();
                        }
                    }
                    return InterpolatedString();
                }
                // $ not followed by " is an error
                throw Error("Expected '\"' after '$' for interpolated string");
            
            // String literals
            case '"':
                // Check for triple quotes: """
                if (Peek() == '"' && PeekNext() == '"')
                {
                    return TripleQuotedString();
                }
                return String();
            case '\'': return SingleQuoteString();
            
            // Comments and numbers
            default:
                if (IsDigit(c))
                    return Number();
                if (IsAlpha(c))
                    return Identifier();
                throw Error($"Unexpected character: {c}");
        }
    }
    
    private Token? HandleSlash()
    {
        if (Match('/'))
        {
            // Single-line comment
            while (Peek() != '\n' && !IsAtEnd())
                Advance();
            return null;
        }
        else if (Match('*'))
        {
            // Multi-line comment
            while (!IsAtEnd())
            {
                if (Peek() == '*' && PeekNext() == '/')
                {
                    Advance(); // consume '*'
                    Advance(); // consume '/'
                    break;
                }
                if (Peek() == '\n')
                {
                    _line++;
                    _column = 1;
                }
                Advance();
            }
            return null;
        }
        else if (Match('='))
        {
            return CreateToken(TokenType.DivideAssign);
        }
        else
        {
            return CreateToken(TokenType.Divide);
        }
    }
    
    private Token HandlePlus()
    {
        if (Match('+'))
        {
            return CreateToken(TokenType.Increment);
        }
        else if (Match('='))
        {
            return CreateToken(TokenType.PlusAssign);
        }
        else
        {
            return CreateToken(TokenType.Plus);
        }
    }
    
    private Token HandleAssign()
    {
        if (Match('>'))
        {
            return CreateToken(TokenType.Arrow);
        }
        else if (Match('='))
        {
            return CreateToken(TokenType.Equal);
        }
        else
        {
            return CreateToken(TokenType.Assign);
        }
    }
    
    private Token HandleMinus()
    {
        if (Match('-'))
        {
            return CreateToken(TokenType.Decrement);
        }
        else if (Match('>'))
        {
            return CreateToken(TokenType.Arrow);
        }
        else if (Match('='))
        {
            return CreateToken(TokenType.MinusAssign);
        }
        else
        {
            return CreateToken(TokenType.Minus);
        }
    }
    
    private Token HandleMultiply()
    {
        if (Match('='))
        {
            return CreateToken(TokenType.MultiplyAssign);
        }
        else
        {
            return CreateToken(TokenType.Multiply);
        }
    }
    
    private Token String()
    {
        var value = "";
        while (Peek() != '"' && !IsAtEnd())
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 1;
            }
            
            if (Peek() == '\\')
            {
                Advance(); // consume '\'
                value += DecodeStringEscape(allowBraceEscapes: false);
            }
            else
            {
                value += Advance();
            }
        }
        
        if (IsAtEnd())
            throw Error("Unterminated string");
        
        Advance(); // consume closing "
        return CreateToken(TokenType.String, value);
    }
    
    private Token SingleQuoteString()
    {
        var value = "";
        while (Peek() != '\'' && !IsAtEnd())
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 1;
            }
            
            if (Peek() == '\\')
            {
                Advance(); // consume '\'
                value += DecodeStringEscape(allowBraceEscapes: false);
            }
            else
            {
                value += Advance();
            }
        }
        
        if (IsAtEnd())
            throw Error("Unterminated string");
        
        Advance(); // consume closing '
        return CreateToken(TokenType.String, value);
    }
    
    private Token InterpolatedString()
    {
        // We've already consumed '$', now consume '"'
        Advance(); // consume '"'
        
        var segments = new List<LexerInterpolatedStringSegment>();
        var currentText = new System.Text.StringBuilder();
        int braceDepth = 0;
        var expressionStart = -1;
        
        while (!IsAtEnd())
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 1;
            }
            
            if (Peek() == '\\')
            {
                Advance(); // consume '\'
                currentText.Append(DecodeStringEscape(allowBraceEscapes: true));
            }
            else if (Peek() == '{')
            {
                // {{ escape sequence: literal {
                if (PeekNext() == '{')
                {
                    Advance(); // consume first '{'
                    Advance(); // consume second '{'
                    currentText.Append('{');
                }
                else
                {
                    // Start of expression
                    if (currentText.Length > 0)
                    {
                        segments.Add(new LexerInterpolatedStringSegment(false, currentText.ToString()));
                        currentText.Clear();
                    }
                    Advance(); // consume '{'
                    braceDepth = 1;
                    expressionStart = _current;
                    
                    // Find matching closing brace
                    while (braceDepth > 0 && !IsAtEnd())
                    {
                        if (Peek() == '\n')
                        {
                            _line++;
                            _column = 1;
                        }
                        
                        if (Peek() == '\\')
                        {
                            Advance(); // consume '\'
                            Advance(); // consume escaped character (we'll include it in expression)
                        }
                        else if (Peek() == '{')
                        {
                            braceDepth++;
                            Advance();
                        }
                        else if (Peek() == '}')
                        {
                            braceDepth--;
                            if (braceDepth > 0)
                                Advance();
                        }
                        else
                        {
                            Advance();
                        }
                    }
                    
                    if (braceDepth > 0)
                        throw Error("Unterminated expression in interpolated string");
                    
                    // Extract expression content (without the closing '}')
                    // _current is pointing at the closing '}', so we extract from expressionStart to _current
                    // Substring(startIndex, length) extracts 'length' characters starting from startIndex
                    // So if expressionStart points to 'i' and _current points to '}', we extract 1 char = "i"
                    string expressionContent = _source.Substring(expressionStart, _current - expressionStart);
                    
                    // Trim whitespace that might have been included
                    expressionContent = expressionContent.Trim();
                    
                    segments.Add(new LexerInterpolatedStringSegment(true, expressionContent));
                    
                    Advance(); // consume closing '}'
                }
            }
            else if (Peek() == '}')
            {
                // }} escape sequence: literal }
                if (PeekNext() == '}')
                {
                    Advance(); // consume first '}'
                    Advance(); // consume second '}'
                    currentText.Append('}');
                }
                else
                {
                    currentText.Append(Advance());
                }
            }
            else if (Peek() == '"')
            {
                // End of string
                break;
            }
            else
            {
                currentText.Append(Advance());
            }
        }
        
        if (IsAtEnd())
            throw Error("Unterminated interpolated string");
        
        // Add any remaining text
        if (currentText.Length > 0)
        {
            segments.Add(new LexerInterpolatedStringSegment(false, currentText.ToString()));
        }
        
        Advance(); // consume closing "
        return CreateToken(TokenType.InterpolatedString, segments);
    }
    
    private Token TripleQuotedString()
    {
        // We've already consumed the first '"', now consume the next two '"'
        Advance(); // consume second "
        Advance(); // consume third "
        
        var value = new System.Text.StringBuilder();
        
        while (!IsAtEnd())
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 1;
            }
            
            // Check for closing triple quotes
            if (Peek() == '"' && PeekNext() == '"' && _current + 2 < _source.Length && _source[_current + 2] == '"')
            {
                Advance(); // consume first "
                Advance(); // consume second "
                Advance(); // consume third "
                return CreateToken(TokenType.String, value.ToString());
            }
            
            // No escaping needed in triple-quoted strings (except we need to handle the closing sequence)
            value.Append(Advance());
        }
        
        throw Error("Unterminated triple-quoted string");
    }
    
    private Token InterpolatedTripleQuotedString()
    {
        // We've already consumed '$', now consume the three '"'
        Advance(); // consume first "
        Advance(); // consume second "
        Advance(); // consume third "
        
        var segments = new List<LexerInterpolatedStringSegment>();
        var currentText = new System.Text.StringBuilder();
        int braceDepth = 0;
        var expressionStart = -1;
        
        while (!IsAtEnd())
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 1;
            }
            
            // Check for closing triple quotes
            if (Peek() == '"' && PeekNext() == '"' && _current + 2 < _source.Length && _source[_current + 2] == '"')
            {
                // Add any remaining text before closing
                if (currentText.Length > 0)
                {
                    segments.Add(new LexerInterpolatedStringSegment(false, currentText.ToString()));
                }
                
                Advance(); // consume first "
                Advance(); // consume second "
                Advance(); // consume third "
                return CreateToken(TokenType.InterpolatedString, segments);
            }
            
            if (Peek() == '\\')
            {
                Advance(); // consume '\'
                currentText.Append(DecodeStringEscape(allowBraceEscapes: true));
            }
            else if (Peek() == '{')
            {
                // {{ escape sequence: literal {
                if (PeekNext() == '{')
                {
                    Advance(); // consume first '{'
                    Advance(); // consume second '{'
                    currentText.Append('{');
                }
                else
                {
                    // Start of expression
                    if (currentText.Length > 0)
                    {
                        segments.Add(new LexerInterpolatedStringSegment(false, currentText.ToString()));
                        currentText.Clear();
                    }
                    Advance(); // consume '{'
                    braceDepth = 1;
                    expressionStart = _current;
                    
                    // Find matching closing brace
                    while (braceDepth > 0 && !IsAtEnd())
                    {
                        if (Peek() == '\n')
                        {
                            _line++;
                            _column = 1;
                        }
                        
                        if (Peek() == '\\')
                        {
                            Advance(); // consume '\'
                            Advance(); // consume escaped character (we'll include it in expression)
                        }
                        else if (Peek() == '{')
                        {
                            braceDepth++;
                            Advance();
                        }
                        else if (Peek() == '}')
                        {
                            braceDepth--;
                            if (braceDepth > 0)
                                Advance();
                        }
                        else
                        {
                            Advance();
                        }
                    }
                    
                    if (braceDepth > 0)
                        throw Error("Unterminated expression in interpolated triple-quoted string");
                    
                    // Extract expression content
                    string expressionContent = _source.Substring(expressionStart, _current - expressionStart);
                    expressionContent = expressionContent.Trim();
                    
                    segments.Add(new LexerInterpolatedStringSegment(true, expressionContent));
                    
                    Advance(); // consume closing '}'
                }
            }
            else if (Peek() == '}')
            {
                // }} escape sequence: literal }
                if (PeekNext() == '}')
                {
                    Advance(); // consume first '}'
                    Advance(); // consume second '}'
                    currentText.Append('}');
                }
                else
                {
                    currentText.Append(Advance());
                }
            }
            else
            {
                currentText.Append(Advance());
            }
        }
        
        throw Error("Unterminated interpolated triple-quoted string");
    }
    
    private Token Number()
    {
        while (IsDigit(Peek()))
            Advance();
        
        // Look for decimal point
        if (Peek() == '.' && IsDigit(PeekNext()))
        {
            Advance(); // consume '.'
            while (IsDigit(Peek()))
                Advance();
        }
        
        // Handle scientific notation (e.g., 1e-5)
        if (Peek() == 'e' || Peek() == 'E')
        {
            Advance(); // consume 'e' or 'E'
            if (Peek() == '+' || Peek() == '-')
                Advance(); // consume sign
            while (IsDigit(Peek()))
                Advance();
        }
        
        string text = _source.Substring(_start, _current - _start);
        if (text.Contains('.') || text.Contains('e') || text.Contains('E'))
        {
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                return CreateToken(TokenType.Float, value);
        }
        else
        {
            if (int.TryParse(text, out int value))
                return CreateToken(TokenType.Integer, value);
        }
        
        throw Error($"Invalid number: {text}");
    }
    
    private Token Identifier()
    {
        while (IsAlphaNumeric(Peek()))
            Advance();
        
        string text = _source.Substring(_start, _current - _start);
        TokenType type = Keywords.GetValueOrDefault(text, TokenType.Identifier);
        
        if (type == TokenType.True)
            return CreateToken(TokenType.Boolean, true);
        if (type == TokenType.False)
            return CreateToken(TokenType.Boolean, false);
        
        return CreateToken(type);
    }
    
    private char Advance()
    {
        _current++;
        _column++;
        return _source[_current - 1];
    }
    
    private bool Match(char expected)
    {
        if (IsAtEnd() || _source[_current] != expected)
            return false;
        _current++;
        _column++;
        return true;
    }
    
    private char Peek()
    {
        if (IsAtEnd())
            return '\0';
        return _source[_current];
    }
    
    private char PeekNext()
    {
        if (_current + 1 >= _source.Length)
            return '\0';
        return _source[_current + 1];
    }
    
    private bool IsAlpha(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }
    
    private bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }
    
    private bool IsAlphaNumeric(char c)
    {
        return IsAlpha(c) || IsDigit(c);
    }
    
    private bool IsAtEnd()
    {
        return _current >= _source.Length;
    }
    
    private Token CreateToken(TokenType type)
    {
        return CreateToken(type, null);
    }
    
    private Token CreateToken(TokenType type, object? literal)
    {
        string text = _source.Substring(_start, _current - _start);
        int column = _column - (text.Length);
        return new Token(type, text, literal, _line, column);
    }
    
    /// <summary>
    /// Decode a string escape after the backslash has already been consumed.
    /// Advances past the escape character. Unknown sequences are hard errors
    /// (previously plain strings silently dropped the backslash, e.g. <c>"\r"</c> → <c>"r"</c>).
    /// </summary>
    private string DecodeStringEscape(bool allowBraceEscapes)
    {
        if (IsAtEnd())
            throw Error("Unterminated escape sequence in string");

        char escape = Advance();
        switch (escape)
        {
            case 'n': return "\n";
            case 'r': return "\r";
            case 't': return "\t";
            case '"': return "\"";
            case '\'': return "'";
            case '\\': return "\\";
            case '{' when allowBraceEscapes: return "{";
            case '}' when allowBraceEscapes: return "}";
            default:
                var supported = allowBraceEscapes
                    ? "\\n, \\r, \\t, \\\", \\', \\\\, \\{, \\}"
                    : "\\n, \\r, \\t, \\\", \\', \\\\";
                throw Error($"Unknown escape sequence '\\{escape}'. Supported: {supported}.");
        }
    }

    private Exception Error(string message)
    {
        var actualLine = _line + _lineOffset;
        if (_sourceFileName != null)
        {
            var errorMsg = $"Lexer error in {_sourceFileName} at line {actualLine}, column {_column}: {message}";
            return new Exception(errorMsg);
        }
        var errorMsgNoFile = $"Lexer error at line {actualLine}, column {_column}: {message}";
        return new Exception(errorMsgNoFile);
    }
}
