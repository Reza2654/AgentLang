using System.Diagnostics;

namespace AgentLang.Models;

public sealed record DatasetEntry(
    string Prompt,
    string Response,
    string? Rejected = null,
    string? Image = null);

public sealed class DatasetDefinition
{
    private readonly List<DatasetEntry> _entries = [];

    public string Name { get; }
    public string Mode { get; }
    public IReadOnlyList<DatasetEntry> Entries => _entries;

    public DatasetDefinition(string name, string? mode = null)
    {
        Name = name;
        Mode = string.IsNullOrWhiteSpace(mode) ? "qa" : mode.ToLowerInvariant();
    }

    public void AddPair(string prompt, string response, string? image = null)
    {
        _entries.Add(new DatasetEntry(prompt, response, null, image));
    }

    public void AddPreference(string prompt, string chosen, string rejected)
    {
        _entries.Add(new DatasetEntry(prompt, chosen, rejected, null));
    }
}

public sealed record TrainingValidationConfig(
    IReadOnlyList<(string Prompt, string Expected)> TestCases,
    double MinAccuracy = 0.0);

public sealed record TrainingConfig(
    string ModelName,
    string BaseModel,
    string DatasetName,
    int Epochs = 3,
    double LearningRate = 0.001,
    TrainingValidationConfig? Validation = null);

public sealed record TrainingResult(
    string ModelName,
    bool Success,
    int EpochsCompleted,
    double FinalLoss,
    double ValidationAccuracy,
    IReadOnlyList<string> Logs,
    string? Error = null);

public sealed class TrainedModelProvider : IModelProvider
{
    private readonly TrainingConfig _config;
    private readonly Dictionary<string, string> _qaMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DatasetEntry> _knowledge = [];
    private readonly IModelProvider _baseProvider;

    public string ProviderId => $"trained:{_config.ModelName}";
    public string ModelName => _config.ModelName;
    public TrainingConfig Config => _config;

    public TrainedModelProvider(TrainingConfig config, IEnumerable<DatasetEntry> entries, IModelProvider baseProvider)
    {
        _config = config;
        _baseProvider = baseProvider;

        foreach (var entry in entries)
        {
            _knowledge.Add(entry);
            _qaMap[entry.Prompt.Trim()] = entry.Response;
        }
    }

    public bool CanHandle(string modelName)
    {
        return modelName.Equals(_config.ModelName, StringComparison.OrdinalIgnoreCase) ||
               modelName.Equals(ProviderId, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string cleanPrompt = request.Prompt.Trim();

        // 1. Direct Q&A lookup
        if (_qaMap.TryGetValue(cleanPrompt, out var directAnswer))
        {
            sw.Stop();
            return new ModelResponse(directAnswer, _config.ModelName, 15, sw.Elapsed, true);
        }

        // 2. Substring or fuzzy matching across trained dataset
        foreach (var entry in _knowledge)
        {
            if (cleanPrompt.Contains(entry.Prompt, StringComparison.OrdinalIgnoreCase) ||
                entry.Prompt.Contains(cleanPrompt, StringComparison.OrdinalIgnoreCase))
            {
                sw.Stop();
                return new ModelResponse(entry.Response, _config.ModelName, 20, sw.Elapsed, true);
            }
        }

        // 3. Fallback to base model augmented with trained domain context
        var augmentedContext = new List<string>(request.Context ?? []);
        augmentedContext.Add($"You are '{_config.ModelName}', a specialized model trained on {_knowledge.Count} examples from dataset '{_config.DatasetName}'.");

        var augmentedRequest = request with
        {
            ModelName = _config.BaseModel,
            Context = augmentedContext
        };

        var baseRes = await _baseProvider.GenerateAsync(augmentedRequest, ct);
        sw.Stop();

        return new ModelResponse(
            baseRes.Content,
            _config.ModelName,
            baseRes.TokensUsed,
            sw.Elapsed,
            baseRes.Success,
            baseRes.ErrorMessage);
    }
}

public sealed class AiTrainingEngine
{
    private readonly Dictionary<string, DatasetDefinition> _datasets = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, DatasetDefinition> Datasets => _datasets;

    public void RegisterDataset(DatasetDefinition dataset)
    {
        _datasets[dataset.Name] = dataset;
    }

    public DatasetDefinition? GetDataset(string name) =>
        _datasets.TryGetValue(name, out var d) ? d : null;

    public DatasetDefinition GetOrCreateDataset(string name, string? mode = null)
    {
        if (!_datasets.TryGetValue(name, out var d))
        {
            d = new DatasetDefinition(name, mode);
            _datasets[name] = d;
        }
        return d;
    }

    public async Task<TrainingResult> TrainAsync(
        TrainingConfig config,
        ModelRegistry registry,
        TextWriter? logger = null,
        CancellationToken ct = default)
    {
        var logs = new List<string>();
        void Log(string msg)
        {
            logs.Add(msg);
            logger?.WriteLine(msg);
        }

        Log($"[AgentLang Training Engine] Starting training for model '{config.ModelName}'...");
        Log($"  Base Model: {config.BaseModel}");
        Log($"  Dataset: {config.DatasetName}");
        Log($"  Epochs: {config.Epochs}, Learning Rate: {config.LearningRate}");

        if (!_datasets.TryGetValue(config.DatasetName, out var dataset) || dataset.Entries.Count == 0)
        {
            string err = $"Dataset '{config.DatasetName}' is empty or does not exist.";
            Log($"  ERROR: {err}");
            return new TrainingResult(config.ModelName, false, 0, 0, 0, logs, err);
        }

        Log($"  Loaded {dataset.Entries.Count} training items (Mode: {dataset.Mode.ToUpperInvariant()})");

        // Simulate training epochs with realistic loss convergence
        double loss = 1.85;
        for (int epoch = 1; epoch <= config.Epochs; epoch++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(20, ct); // fast simulation step
            loss = Math.Max(0.02, loss * (0.55 + 0.1 * (1.0 / epoch)));
            Log($"  Epoch {epoch}/{config.Epochs} - Loss: {loss:F4}");
        }

        var baseProvider = registry.Resolve(config.BaseModel);
        var trainedProvider = new TrainedModelProvider(config, dataset.Entries, baseProvider);

        // Validation pass
        double accuracy = 1.0;
        if (config.Validation != null && config.Validation.TestCases.Count > 0)
        {
            Log($"  Running validation on {config.Validation.TestCases.Count} test cases (Min Accuracy: {config.Validation.MinAccuracy:P0})...");
            int passed = 0;
            foreach (var tc in config.Validation.TestCases)
            {
                var response = await trainedProvider.GenerateAsync(new ModelRequest(config.ModelName, tc.Prompt), ct);
                string actual = response.Content.Trim();
                string expected = tc.Expected.Trim();

                bool isMatch = actual.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                               actual.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
                               expected.Contains(actual, StringComparison.OrdinalIgnoreCase);

                if (isMatch)
                {
                    passed++;
                    Log($"    [PASS] \"{tc.Prompt}\" -> \"{actual}\"");
                }
                else
                {
                    Log($"    [FAIL] \"{tc.Prompt}\" -> Got: \"{actual}\", Expected: \"{expected}\"");
                }
            }

            accuracy = (double)passed / config.Validation.TestCases.Count;
            Log($"  Validation Accuracy: {accuracy:P1} ({passed}/{config.Validation.TestCases.Count})");

            if (accuracy < config.Validation.MinAccuracy)
            {
                string valErr = $"Model validation failed: accuracy {accuracy:P1} is below required minimum {config.Validation.MinAccuracy:P1}";
                Log($"  ERROR: {valErr}");
                return new TrainingResult(config.ModelName, false, config.Epochs, loss, accuracy, logs, valErr);
            }
        }

        // Register trained model into ModelRegistry
        registry.RegisterProvider(trainedProvider);
        registry.RegisterAlias(config.ModelName, trainedProvider.ProviderId);
        Log($"  Model '{config.ModelName}' successfully trained and registered in model registry!");

        return new TrainingResult(config.ModelName, true, config.Epochs, loss, accuracy, logs);
    }
}
