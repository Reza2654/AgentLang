using System.Globalization;
using System.Text;
using AgentLang.AST;

namespace AgentLang.Lexer;

public sealed class Lexer
{
    private readonly SourceText _source;
    private readonly DiagnosticBag _diagnostics;
    private int _position;
    private int _line = 1;
    private int _column = 1;

    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
    {
        { "main", TokenType.Main },
        { "agent", TokenType.Agent },
        { "multiagent", TokenType.MultiAgent },
        { "task", TokenType.Task },
        { "context", TokenType.Context },
        { "permission", TokenType.Permission },
        { "tool", TokenType.Tool },
        { "memory", TokenType.Memory },
        { "model", TokenType.Model },
        { "tools", TokenType.Tools },
        { "event", TokenType.Event },
        { "parallel", TokenType.Parallel },
        { "if", TokenType.If },
        { "else", TokenType.Else },
        { "for", TokenType.For },
        { "in", TokenType.In },
        { "while", TokenType.While },
        { "repeat", TokenType.Repeat },
        { "return", TokenType.Return },
        { "try", TokenType.Try },
        { "catch", TokenType.Catch },
        { "retry", TokenType.Retry },
        { "allow", TokenType.Allow },
        { "ask", TokenType.Ask },
        { "cannot", TokenType.Cannot },
        { "think", TokenType.Think },
        { "research", TokenType.Research },
        { "print", TokenType.Print },
        { "input", TokenType.Input },
        { "dependencies", TokenType.Dependencies },
        { "function", TokenType.Function },
        { "send", TokenType.Send },
        { "to", TokenType.To },
        { "description", TokenType.Description },
        { "execute", TokenType.Execute },
        { "type", TokenType.Type },
        { "true", TokenType.BooleanLiteral },
        { "false", TokenType.BooleanLiteral },
        { "null", TokenType.NullLiteral },
        { "and", TokenType.And },
        { "or", TokenType.Or },
        { "not", TokenType.Not }
    };

    public Lexer(SourceText source, DiagnosticBag? diagnostics = null)
    {
        _source = source;
        _diagnostics = diagnostics ?? new DiagnosticBag();
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    private char Current => Peek(0);
    private char Lookahead => Peek(1);

    private char Peek(int offset)
    {
        int index = _position + offset;
        return index >= _source.Content.Length ? '\0' : _source.Content[index];
    }

    private char Advance()
    {
        if (_position >= _source.Content.Length)
            return '\0';

        char c = _source.Content[_position++];
        if (c == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        return c;
    }

    public IReadOnlyList<Token> TokenizeAll()
    {
        var tokens = new List<Token>();
        while (true)
        {
            var token = NextToken();
            tokens.Add(token);
            if (token.Type == TokenType.EndOfFile)
                break;
        }
        return tokens;
    }

    public Token NextToken()
    {
        while (true)
        {
            // Skip whitespace
            while (char.IsWhiteSpace(Current))
            {
                Advance();
            }

            // Single line comment: // or #
            if ((Current == '/' && Lookahead == '/') || Current == '#')
            {
                while (Current != '\0' && Current != '\n')
                {
                    Advance();
                }
                continue;
            }

            // Multi-line comment: /* ... */
            if (Current == '/' && Lookahead == '*')
            {
                var startLoc = CurrentLocation();
                Advance(); // /
                Advance(); // *
                bool closed = false;
                while (Current != '\0')
                {
                    if (Current == '*' && Lookahead == '/')
                    {
                        Advance(); // *
                        Advance(); // /
                        closed = true;
                        break;
                    }
                    Advance();
                }
                if (!closed)
                {
                    _diagnostics.ReportError(
                        "AL0001",
                        "Unterminated multi-line comment",
                        new SourceSpan(startLoc, CurrentLocation(), _source.FilePath));
                }
                continue;
            }

            break;
        }

        var startLocation = CurrentLocation();

        if (Current == '\0')
        {
            return new Token(TokenType.EndOfFile, "\0", null, new SourceSpan(startLocation, startLocation, _source.FilePath));
        }

        // Numbers
        if (char.IsDigit(Current))
        {
            return ReadNumberToken(startLocation);
        }

        // Strings
        if (Current == '"' || Current == '\'')
        {
            return ReadStringToken(startLocation);
        }

        // Identifiers and Keywords
        if (char.IsLetter(Current) || Current == '_')
        {
            return ReadIdentifierOrKeyword(startLocation);
        }

        // Symbols and Operators
        return ReadSymbolToken(startLocation);
    }

    private SourceLocation CurrentLocation() => new(_line, _column, _position);

    private Token ReadNumberToken(SourceLocation startLocation)
    {
        int startPos = _position;
        bool hasDecimal = false;

        while (char.IsDigit(Current) || (Current == '.' && char.IsDigit(Lookahead) && !hasDecimal))
        {
            if (Current == '.')
                hasDecimal = true;
            Advance();
        }

        string text = _source.Content[startPos.._position];
        var span = new SourceSpan(startLocation, CurrentLocation(), _source.FilePath);

        if (hasDecimal)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double dVal))
                return new Token(TokenType.NumberLiteral, text, dVal, span);
        }
        else
        {
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lVal))
                return new Token(TokenType.NumberLiteral, text, (double)lVal, span);
        }

        _diagnostics.ReportError("AL0002", $"Invalid number literal: '{text}'", span);
        return new Token(TokenType.BadToken, text, null, span);
    }

    private Token ReadStringToken(SourceLocation startLocation)
    {
        char quote = Advance(); // eat opening quote
        var sb = new StringBuilder();
        bool closed = false;

        while (Current != '\0')
        {
            if (Current == quote)
            {
                Advance(); // eat closing quote
                closed = true;
                break;
            }

            if (Current == '\\')
            {
                Advance(); // eat \
                char escape = Current;
                Advance();
                switch (escape)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '\'': sb.Append('\''); break;
                    default:
                        sb.Append(escape);
                        break;
                }
            }
            else
            {
                sb.Append(Advance());
            }
        }

        var span = new SourceSpan(startLocation, CurrentLocation(), _source.FilePath);

        if (!closed)
        {
            _diagnostics.ReportError("AL0003", "Unterminated string literal", span);
            return new Token(TokenType.BadToken, sb.ToString(), null, span);
        }

        return new Token(TokenType.StringLiteral, sb.ToString(), sb.ToString(), span);
    }

    private Token ReadIdentifierOrKeyword(SourceLocation startLocation)
    {
        int startPos = _position;
        while (char.IsLetterOrDigit(Current) || Current == '_')
        {
            Advance();
        }

        string text = _source.Content[startPos.._position];
        var span = new SourceSpan(startLocation, CurrentLocation(), _source.FilePath);

        if (Keywords.TryGetValue(text, out var keywordType))
        {
            object? value = keywordType switch
            {
                TokenType.BooleanLiteral => text == "true",
                TokenType.NullLiteral => null,
                _ => null
            };
            return new Token(keywordType, text, value, span);
        }

        return new Token(TokenType.Identifier, text, text, span);
    }

    private Token ReadSymbolToken(SourceLocation startLocation)
    {
        char c = Advance();
        var endLocation = CurrentLocation();
        var span = new SourceSpan(startLocation, endLocation, _source.FilePath);

        switch (c)
        {
            case '(': return new Token(TokenType.OpenParen, "(", null, span);
            case ')': return new Token(TokenType.CloseParen, ")", null, span);
            case '{': return new Token(TokenType.OpenBrace, "{", null, span);
            case '}': return new Token(TokenType.CloseBrace, "}", null, span);
            case '[': return new Token(TokenType.OpenBracket, "[", null, span);
            case ']': return new Token(TokenType.CloseBracket, "]", null, span);
            case ',': return new Token(TokenType.Comma, ",", null, span);
            case ':': return new Token(TokenType.Colon, ":", null, span);
            case ';': return new Token(TokenType.Semicolon, ";", null, span);
            case '+': return new Token(TokenType.Plus, "+", null, span);
            case '-': return new Token(TokenType.Minus, "-", null, span);
            case '*': return new Token(TokenType.Asterisk, "*", null, span);
            case '/': return new Token(TokenType.Slash, "/", null, span);
            case '%': return new Token(TokenType.Percent, "%", null, span);

            case '.': return new Token(TokenType.Dot, ".", null, span);

            case '=':
                if (Current == '=')
                {
                    Advance();
                    return new Token(TokenType.EqualsEquals, "==", null, new SourceSpan(startLocation, CurrentLocation(), _source.FilePath));
                }
                return new Token(TokenType.Equals, "=", null, span);

            case '!':
                if (Current == '=')
                {
                    Advance();
                    return new Token(TokenType.ExclamationEquals, "!=", null, new SourceSpan(startLocation, CurrentLocation(), _source.FilePath));
                }
                return new Token(TokenType.Exclamation, "!", null, span);

            case '<':
                if (Current == '=')
                {
                    Advance();
                    return new Token(TokenType.LessThanEquals, "<=", null, new SourceSpan(startLocation, CurrentLocation(), _source.FilePath));
                }
                return new Token(TokenType.LessThan, "<", null, span);

            case '>':
                if (Current == '=')
                {
                    Advance();
                    return new Token(TokenType.GreaterThanEquals, ">=", null, new SourceSpan(startLocation, CurrentLocation(), _source.FilePath));
                }
                return new Token(TokenType.GreaterThan, ">", null, span);

            case '&':
                if (Current == '&')
                {
                    Advance();
                    return new Token(TokenType.And, "&&", null, new SourceSpan(startLocation, CurrentLocation(), _source.FilePath));
                }
                break;

            case '|':
                if (Current == '|')
                {
                    Advance();
                    return new Token(TokenType.Or, "||", null, new SourceSpan(startLocation, CurrentLocation(), _source.FilePath));
                }
                break;
        }

        _diagnostics.ReportError("AL0004", $"Unexpected character: '{c}'", span);
        return new Token(TokenType.BadToken, c.ToString(), null, span);
    }
}
