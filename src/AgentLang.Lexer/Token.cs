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
    ImportApi,
    Swarm,
    Broadcast,
    Delegate,
    Wait,
    Await,
    Plan,
    Until,

    // AI Training, MCP & Custom APIs (v0.4.0)
    Dataset,
    Train,
    Validate,
    Mcp,
    Api,
    Learn,
    Preference,
    Pair,
    Image,
    Vision,

    // Agent Scripting, Workflows & Budgeting (v0.6.0)
    Goal,
    Pipeline,
    Workflow,
    State,
    Decide,
    Reasoning,
    Action,
    Loop,
    Budget,
    Guardrails,
    Strategy,
    Persona,
    Rules,
    Case,
    Default,
    MaxRetries,
    On,
    Init,
    From,
    Break,
    Continue,

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
    Exclamation,      // !
    Arrow             // ->
}

public sealed record Token(
    TokenType Type,
    string Text,
    object? Value,
    SourceSpan Span)
{
    public override string ToString() => $"{Type} '{Text}' at {Span}";
}
