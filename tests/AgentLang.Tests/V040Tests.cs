using AgentLang.AST;
using AgentLang.Errors;
using AgentLang.Lexer;
using AgentLang.Models;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Semantic;
using Xunit;

namespace AgentLang.Tests;

public class V040Tests
{
    [Fact]
    public void Lexer_Tokenizes_V040_Keywords()
    {
        string code = "dataset train validate mcp api learn preference pair image vision";
        var lexer = new Lexer.Lexer(new SourceText(code));
        var tokens = lexer.TokenizeAll();

        Assert.Equal(TokenType.Dataset, tokens[0].Type);
        Assert.Equal(TokenType.Train, tokens[1].Type);
        Assert.Equal(TokenType.Validate, tokens[2].Type);
        Assert.Equal(TokenType.Mcp, tokens[3].Type);
        Assert.Equal(TokenType.Api, tokens[4].Type);
        Assert.Equal(TokenType.Learn, tokens[5].Type);
        Assert.Equal(TokenType.Preference, tokens[6].Type);
        Assert.Equal(TokenType.Pair, tokens[7].Type);
        Assert.Equal(TokenType.Image, tokens[8].Type);
        Assert.Equal(TokenType.Vision, tokens[9].Type);
    }

    [Fact]
    public void Parser_Parses_Dataset_And_Train_Declarations()
    {
        string code = @"
dataset MathQA {
    mode: ""qa""
    pair(""What is 2+2?"", ""4"")
    pair(input: ""5+5"", output: ""10"")
}

dataset CodeReviews {
    mode: ""preference""
    preference(
        prompt: ""Write fib"",
        chosen: ""def fib(n): return n if n < 2 else fib(n-1) + fib(n-2)"",
        rejected: ""while true pass""
    )
}

train model MathExpert {
    base: ""mock""
    data: MathQA
    epochs: 3
    learning_rate: 0.001
    validate {
        test(""What is 2+2?"", ""4"")
        min_accuracy: 80%
    }
}
";
        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();

        Assert.False(parser.Diagnostics.HasErrors);
        Assert.Equal(3, program.Declarations.Count);

        var ds1 = Assert.IsType<DatasetDeclarationNode>(program.Declarations[0]);
        Assert.Equal("MathQA", ds1.Name);
        Assert.Equal("qa", ds1.Mode);
        Assert.Equal(2, ds1.Items.Count);

        var ds2 = Assert.IsType<DatasetDeclarationNode>(program.Declarations[1]);
        Assert.Equal("CodeReviews", ds2.Name);
        Assert.Equal("preference", ds2.Mode);
        Assert.Single(ds2.Items);

        var train = Assert.IsType<TrainDeclarationNode>(program.Declarations[2]);
        Assert.Equal("MathExpert", train.ModelName);
        Assert.NotNull(train.Validation);
        Assert.Single(train.Validation!.TestCases);
    }

    [Fact]
    public void Parser_Parses_Mcp_And_CustomApi_Declarations()
    {
        string code = @"
mcp filesystem = ""npx -y @modelcontextprotocol/server-filesystem ./data"" {
    env: {
        ""DEBUG"": ""true""
    }
}

api WeatherService {
    endpoint: ""https://api.weather.com""
    type: ""rest""
    headers: {
        ""Authorization"": ""Bearer my_token""
    }
    get current(city: string) {
        path: ""/current?city="" + city
    }
}

api Ollama {
    endpoint: ""http://localhost:11434""
    type: ""openai-compatible""
    model: ""llama3""
}
";
        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();

        Assert.False(parser.Diagnostics.HasErrors);
        Assert.Equal(3, program.Declarations.Count);

        var mcp = Assert.IsType<McpDeclarationNode>(program.Declarations[0]);
        Assert.Equal("filesystem", mcp.ServerName);
        Assert.True(mcp.Env.ContainsKey("DEBUG"));

        var api1 = Assert.IsType<CustomApiDeclarationNode>(program.Declarations[1]);
        Assert.Equal("WeatherService", api1.ApiName);
        Assert.Single(api1.Methods);
        Assert.Equal("GET", api1.Methods[0].HttpMethod);
        Assert.Equal("current", api1.Methods[0].Name);

        var api2 = Assert.IsType<CustomApiDeclarationNode>(program.Declarations[2]);
        Assert.Equal("Ollama", api2.ApiName);
    }

    [Fact]
    public void SemanticAnalyzer_Validates_V040_Symbols()
    {
        string code = @"
dataset MathData {
    mode: ""qa""
    pair(""1+1"", ""2"")
}

train model FastMath {
    base: ""mock""
    data: MathData
    epochs: 2
}

mcp testServer = ""mock""

api MyApi {
    endpoint: ""https://api.test.com""
    type: ""rest""
    get ping(param: string) {
        path: ""/ping""
    }
}

agent Assistant (
    model: FastMath,
    tools: [testServer, MyApi.ping, image, vision]
) {
}
";
        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var semantic = new SemanticAnalyzer();
        semantic.Analyze(program);

        Assert.False(semantic.Diagnostics.HasErrors);
        Assert.NotNull(semantic.GlobalScope.Lookup("MathData"));
        Assert.NotNull(semantic.GlobalScope.Lookup("FastMath"));
        Assert.NotNull(semantic.GlobalScope.Lookup("testServer"));
        Assert.NotNull(semantic.GlobalScope.Lookup("MyApi"));
    }

    [Fact]
    public async Task Runtime_Executes_Training_And_Agent_Inference()
    {
        string code = @"
dataset CapitalQA {
    mode: ""qa""
    pair(""What is capital of France?"", ""Paris"")
    pair(""What is capital of Japan?"", ""Tokyo"")
}

train model GeoExpert {
    base: ""mock""
    data: CapitalQA
    epochs: 2
    validate {
        test(""What is capital of France?"", ""Paris"")
        min_accuracy: 80%
    }
}

agent GeoBot (model: GeoExpert) {
    task answerFrance {
        result = think(""What is capital of France?"")
    }
    task answerJapan {
        result = think(""What is capital of Japan?"")
    }
}

main {
    agent GeoBot
}
";
        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var runtime = new AgentLangRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.True(runtime.AgentInstances.TryGetValue("GeoBot", out var geoBot));
        Assert.Equal("Paris", geoBot.Tasks["answerFrance"].Result?.ToString());
        Assert.Equal("Tokyo", geoBot.Tasks["answerJapan"].Result?.ToString());
    }

    [Fact]
    public async Task Runtime_Executes_LearnStatement_Dynamic_Learning()
    {
        string code = @"
dataset DynamicKnowledge {
    mode: ""qa""
    pair(""hello"", ""world"")
}

main {
    learn into DynamicKnowledge (""new question"", ""new answer"")
    learn into DynamicKnowledge (input: ""prompt2"", output: ""ans2"")
}
";
        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var runtime = new AgentLangRuntime();
        await runtime.ExecuteProgramAsync(program);

        var ds = runtime.TrainingEngine.GetDataset("DynamicKnowledge");
        Assert.NotNull(ds);
        Assert.Equal(3, ds.Entries.Count);
        Assert.Equal("new answer", ds.Entries[1].Response);
        Assert.Equal("ans2", ds.Entries[2].Response);
    }

    [Fact]
    public async Task Runtime_Executes_Mcp_Mock_Server()
    {
        string code = @"
mcp mockMcp = ""mock""

main {
    res1 = mockMcp.echo(message: ""Antigravity"")
    res2 = mockMcp.status()
}
";
        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var runtime = new AgentLangRuntime();
        await runtime.ExecuteProgramAsync(program);

        var res1 = runtime.GlobalScope.Get("res1")?.ToString();
        var res2 = runtime.GlobalScope.Get("res2")?.ToString();

        Assert.NotNull(res1);
        Assert.Contains("Echo: Antigravity", res1);
        Assert.NotNull(res2);
        Assert.Contains("Online (v0.4.0)", res2);
    }

    [Fact]
    public async Task Runtime_Executes_Custom_Api_Tool()
    {
        string code = @"
api MockWeather {
    endpoint: ""mock://weather-api""
    type: ""rest""
    get current(city: string) {
        path: ""/current""
    }
}

main {
    weatherRes = MockWeather.current(city: ""Tehran"")
}
";
        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var runtime = new AgentLangRuntime();
        await runtime.ExecuteProgramAsync(program);

        var weatherRes = runtime.GlobalScope.Get("weatherRes")?.ToString();
        Assert.NotNull(weatherRes);
        Assert.Contains("MockWeather", weatherRes);
        Assert.Contains("Tehran", weatherRes);
    }

    [Fact]
    public async Task Runtime_Executes_Image_And_Vision_Tools()
    {
        string code = @"
main {
    img = image.generate(""A cybernetic falcon"")
    vis = vision.analyze(""falcon.png"", ""Is it cybernetic?"")
}
";
        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var runtime = new AgentLangRuntime();
        await runtime.ExecuteProgramAsync(program);

        var img = runtime.GlobalScope.Get("img")?.ToString();
        var vis = runtime.GlobalScope.Get("vis")?.ToString();

        Assert.NotNull(img);
        Assert.Contains("Generated 'A cybernetic falcon'", img);
        Assert.NotNull(vis);
        Assert.Contains("Vision Analysis of 'falcon.png'", vis);
    }

    [Fact]
    public async Task Runtime_Fails_When_Train_Validation_Accuracy_Is_Below_Threshold()
    {
        string code = @"
dataset BrokenData {
    mode: ""qa""
    pair(""2+2"", ""4"")
}

train model FailingModel {
    base: ""mock""
    data: BrokenData
    epochs: 1
    validate {
        test(""100*100"", ""999999"") // This will not match 100*100
        min_accuracy: 100%
    }
}

main {
}
";
        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var runtime = new AgentLangRuntime();
        var ex = await Assert.ThrowsAsync<AgentLangRuntimeException>(() => runtime.ExecuteProgramAsync(program));
        Assert.Contains("validation failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
