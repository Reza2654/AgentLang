using AgentLang.AST;

namespace AgentLang.Lexer;

public enum TokenType
{
    EndOfFile,
    BadToken,

    // Identifiers and Literals
    Identifier,
    StringLiteral,
    NumberLiteral,
    BooleanLiteral,
    NullLiteral,

    // Keywords
    Main,
    Agent,
    MultiAgent,
    Task,
    Context,
    Permission,
    Tool,
    Memory,
    Model,
    Tools,
    Event,
    Parallel,
    If,
    Else,
    For,
    In,
    While,
    Repeat,
    Return,
    Try,
    Catch,
    Retry,
    Allow,
    Ask,
    Cannot,
    Think,
    Research,
    Print,
    Input,
    Dependencies,
    Function,
    Send,
    To,
    Description,
    Execute,
    Type,

    // Logical Operators
    And,
    Or,
    Not,

    // Punctuation and Operators
    OpenParen,        // (
    CloseParen,       // )
    OpenBrace,        // {
    CloseBrace,       // }
    OpenBracket,      // [
    CloseBracket,     // ]
    Comma,            // ,
    Dot,              // .
    Colon,            // :
    Semicolon,        // ;
    Equals,           // =
    EqualsEquals,     // ==
    ExclamationEquals,// !=
    LessThan,         // <
    LessThanEquals,   // <=
    GreaterThan,      // >
    GreaterThanEquals,// >=
    Plus,             // +
    Minus,            // -
    Asterisk,         // *
    Slash,            // /
    Percent,          // %
    Exclamation       // !
}

public sealed record Token(
    TokenType Type,
    string Text,
    object? Value,
    SourceSpan Span)
{
    public override string ToString() => $"{Type} '{Text}' at {Span}";
}
