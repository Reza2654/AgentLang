using AgentLang.AST;
using AgentLang.Lexer;
using Xunit;

namespace AgentLang.Tests;

public class LexerTests
{
    [Fact]
    public void TokenizesKeywords()
    {
        string source = "agent multiagent task main context permission tool memory model tools event parallel if else for in while repeat return try catch retry allow ask cannot think research print input dependencies true false null and or not";
        var sourceText = new SourceText(source);
        var lexer = new Lexer.Lexer(sourceText);
        var tokens = lexer.TokenizeAll();

        Assert.False(lexer.Diagnostics.HasErrors);
        Assert.Equal(TokenType.Agent, tokens[0].Type);
        Assert.Equal(TokenType.MultiAgent, tokens[1].Type);
        Assert.Equal(TokenType.Task, tokens[2].Type);
        Assert.Equal(TokenType.Main, tokens[3].Type);
        Assert.Equal(TokenType.Context, tokens[4].Type);
        Assert.Equal(TokenType.Permission, tokens[5].Type);
        Assert.Equal(TokenType.Tool, tokens[6].Type);
        Assert.Equal(TokenType.Memory, tokens[7].Type);
        Assert.Equal(TokenType.Model, tokens[8].Type);
        Assert.Equal(TokenType.Tools, tokens[9].Type);
        Assert.Equal(TokenType.Event, tokens[10].Type);
        Assert.Equal(TokenType.Parallel, tokens[11].Type);
        Assert.Equal(TokenType.If, tokens[12].Type);
        Assert.Equal(TokenType.Else, tokens[13].Type);
        Assert.Equal(TokenType.For, tokens[14].Type);
        Assert.Equal(TokenType.In, tokens[15].Type);
        Assert.Equal(TokenType.While, tokens[16].Type);
        Assert.Equal(TokenType.Repeat, tokens[17].Type);
        Assert.Equal(TokenType.Return, tokens[18].Type);
        Assert.Equal(TokenType.Try, tokens[19].Type);
        Assert.Equal(TokenType.Catch, tokens[20].Type);
        Assert.Equal(TokenType.Retry, tokens[21].Type);
        Assert.Equal(TokenType.Allow, tokens[22].Type);
        Assert.Equal(TokenType.Ask, tokens[23].Type);
        Assert.Equal(TokenType.Cannot, tokens[24].Type);
        Assert.Equal(TokenType.Think, tokens[25].Type);
        Assert.Equal(TokenType.Research, tokens[26].Type);
        Assert.Equal(TokenType.Print, tokens[27].Type);
        Assert.Equal(TokenType.Input, tokens[28].Type);
        Assert.Equal(TokenType.Dependencies, tokens[29].Type);
        Assert.Equal(TokenType.BooleanLiteral, tokens[30].Type);
        Assert.Equal(true, tokens[30].Value);
        Assert.Equal(TokenType.BooleanLiteral, tokens[31].Type);
        Assert.Equal(false, tokens[31].Value);
        Assert.Equal(TokenType.NullLiteral, tokens[32].Type);
        Assert.Null(tokens[32].Value);
    }

    [Fact]
    public void TokenizesNumbersAndStrings()
    {
        string source = "123 45.67 \"Hello, \\\"AgentLang\\\"!\\n\"";
        var sourceText = new SourceText(source);
        var lexer = new Lexer.Lexer(sourceText);
        var tokens = lexer.TokenizeAll();

        Assert.False(lexer.Diagnostics.HasErrors);
        Assert.Equal(TokenType.NumberLiteral, tokens[0].Type);
        Assert.Equal(123.0, tokens[0].Value);

        Assert.Equal(TokenType.NumberLiteral, tokens[1].Type);
        Assert.Equal(45.67, tokens[1].Value);

        Assert.Equal(TokenType.StringLiteral, tokens[2].Type);
        Assert.Equal("Hello, \"AgentLang\"!\n", tokens[2].Value);
    }

    [Fact]
    public void SkipsComments()
    {
        string source = """
            // This is a single line comment
            # This is another comment
            agent Researcher /* multi-line
            comment */ { }
            """;
        var sourceText = new SourceText(source);
        var lexer = new Lexer.Lexer(sourceText);
        var tokens = lexer.TokenizeAll();

        Assert.False(lexer.Diagnostics.HasErrors);
        Assert.Equal(TokenType.Agent, tokens[0].Type);
        Assert.Equal(TokenType.Identifier, tokens[1].Type);
        Assert.Equal("Researcher", tokens[1].Text);
        Assert.Equal(TokenType.OpenBrace, tokens[2].Type);
        Assert.Equal(TokenType.CloseBrace, tokens[3].Type);
    }

    [Fact]
    public void TracksLineAndColumn()
    {
        string source = "name\n  = \"Agent\"";
        var sourceText = new SourceText(source);
        var lexer = new Lexer.Lexer(sourceText);
        var tokens = lexer.TokenizeAll();

        Assert.Equal(1, tokens[0].Span.Start.Line);
        Assert.Equal(1, tokens[0].Span.Start.Column);

        Assert.Equal(2, tokens[1].Span.Start.Line);
        Assert.Equal(3, tokens[1].Span.Start.Column);
    }
}
