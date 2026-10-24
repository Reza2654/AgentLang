using System.Collections.Concurrent;
using System.Text;

namespace AgentLang.VM.Graphs;

public static class StateGraphConstants
{
    public const string StartNode = "__start__";
    public const string EndNode = "__end__";
}

public sealed class StateGraphOptions
{
    public int MaxCycles { get; set; } = 50;
    public bool RecordTrace { get; set; } = true;
}

public sealed record StateGraphTraceStep(
    string NodeName,
    int StepIndex,
    TimeSpan Duration,
    string? NextNode,
    string? Notes = null
);

public sealed class StateGraphResult<TState>
{
    public required TState State { get; init; }
    public bool IsInterrupted { get; init; }
    public string? InterruptedNode { get; init; }
    public string? InterruptedPhase { get; init; } // "before" or "after"
    public int TotalSteps { get; init; }
    public List<StateGraphTraceStep> ExecutionTrace { get; init; } = [];
}

public sealed class StateGraphCycleLimitException : Exception
{
    public int MaxCycles { get; }
    public int TotalCycles { get; }

    public StateGraphCycleLimitException(int maxCycles, int totalCycles)
        : base($"StateGraph cycle limit exceeded. Max cycles: {maxCycles}, executed: {totalCycles}. Potential infinite loop detected.")
    {
        MaxCycles = maxCycles;
        TotalCycles = totalCycles;
    }
}

public class StateGraph<TState>
{
    private readonly Dictionary<string, Func<TState, CancellationToken, Task<TState>>> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _edges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (Func<TState, string> Router, Dictionary<string, string>? Mapping)> _conditionalEdges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (List<string> Branches, Func<TState, IEnumerable<TState>, TState> Reducer)> _parallelBranches = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _interruptBefore = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _interruptAfter = new(StringComparer.OrdinalIgnoreCase);

    public string Name { get; }

    public StateGraph(string name = "StateGraph")
    {
        Name = name;
    }

    public StateGraph<TState> AddNode(string name, Func<TState, CancellationToken, Task<TState>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        _nodes[name] = handler;
        return this;
    }

    public StateGraph<TState> AddNode(string name, Func<TState, TState> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddNode(name, (state, _) => Task.FromResult(handler(state)));
    }

    public StateGraph<TState> AddEdge(string fromNode, string toNode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromNode);
        ArgumentException.ThrowIfNullOrWhiteSpace(toNode);
        _edges[fromNode] = toNode;
        return this;
    }

    public StateGraph<TState> AddConditionalEdge(
        string fromNode,
        Func<TState, string> router,
        Dictionary<string, string>? pathMapping = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromNode);
        ArgumentNullException.ThrowIfNull(router);
        _conditionalEdges[fromNode] = (router, pathMapping);
        return this;
    }

    public StateGraph<TState> AddParallelBranch(
        string fromNode,
        IEnumerable<string> branchNodes,
        Func<TState, IEnumerable<TState>, TState> joinReducer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromNode);
        ArgumentNullException.ThrowIfNull(branchNodes);
        ArgumentNullException.ThrowIfNull(joinReducer);

        var list = branchNodes.ToList();
        if (list.Count == 0) throw new ArgumentException("Parallel branches must contain at least one branch node.", nameof(branchNodes));
        _parallelBranches[fromNode] = (list, joinReducer);
        return this;
    }

    public StateGraph<TState> SetEntryPoint(string nodeName)
    {
        return AddEdge(StateGraphConstants.StartNode, nodeName);
    }

    public StateGraph<TState> SetFinishPoint(string nodeName)
    {
        return AddEdge(nodeName, StateGraphConstants.EndNode);
    }

    public StateGraph<TState> InterruptBefore(params string[] nodeNames)
    {
        foreach (var name in nodeNames) _interruptBefore.Add(name);
        return this;
    }

    public StateGraph<TState> InterruptAfter(params string[] nodeNames)
    {
        foreach (var name in nodeNames) _interruptAfter.Add(name);
        return this;
    }

    public CompiledStateGraph<TState> Compile(StateGraphOptions? options = null)
    {
        if (!_edges.ContainsKey(StateGraphConstants.StartNode))
        {
            throw new InvalidOperationException("StateGraph must have an entry point set. Use SetEntryPoint(nodeName).");
        }

        return new CompiledStateGraph<TState>(
            Name,
            new Dictionary<string, Func<TState, CancellationToken, Task<TState>>>(_nodes, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(_edges, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, (Func<TState, string>, Dictionary<string, string>?)>(_conditionalEdges, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, (List<string>, Func<TState, IEnumerable<TState>, TState>)>(_parallelBranches, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(_interruptBefore, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(_interruptAfter, StringComparer.OrdinalIgnoreCase),
            options ?? new StateGraphOptions()
        );
    }

    public string ToMermaid(string direction = "TD")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"flowchart {direction}");

        foreach (var node in _nodes.Keys)
        {
            sb.AppendLine($"    {node}[\"{node}\"]");
        }

        foreach (var (from, to) in _edges)
        {
            if (from == StateGraphConstants.StartNode)
                sb.AppendLine($"    __start__([Start]) --> {to}");
            else if (to == StateGraphConstants.EndNode)
                sb.AppendLine($"    {from} --> __end__([End])");
            else
                sb.AppendLine($"    {from} --> {to}");
        }

        foreach (var (from, (router, mapping)) in _conditionalEdges)
        {
            if (mapping != null)
            {
                foreach (var (condition, target) in mapping)
                {
                    sb.AppendLine($"    {from} -.->|\"{condition}\"| {target}");
                }
            }
            else
            {
                sb.AppendLine($"    {from} -.->|conditional| ?");
            }
        }

        foreach (var (from, (branches, _)) in _parallelBranches)
        {
            foreach (var b in branches)
            {
                sb.AppendLine($"    {from} ==>|parallel| {b}");
            }
        }

        return sb.ToString();
    }
}

public sealed class CompiledStateGraph<TState>
{
    public string Name { get; }
    private readonly Dictionary<string, Func<TState, CancellationToken, Task<TState>>> _nodes;
    private readonly Dictionary<string, string> _edges;
    private readonly Dictionary<string, (Func<TState, string> Router, Dictionary<string, string>? Mapping)> _conditionalEdges;
    private readonly Dictionary<string, (List<string> Branches, Func<TState, IEnumerable<TState>, TState> Reducer)> _parallelBranches;
    private readonly HashSet<string> _interruptBefore;
    private readonly HashSet<string> _interruptAfter;
    private readonly StateGraphOptions _options;

    private string? _pendingNextNode;
    private TState? _lastState;

    internal CompiledStateGraph(
        string name,
        Dictionary<string, Func<TState, CancellationToken, Task<TState>>> nodes,
        Dictionary<string, string> edges,
        Dictionary<string, (Func<TState, string>, Dictionary<string, string>?)> conditionalEdges,
        Dictionary<string, (List<string>, Func<TState, IEnumerable<TState>, TState>)> parallelBranches,
        HashSet<string> interruptBefore,
        HashSet<string> interruptAfter,
        StateGraphOptions options)
    {
        Name = name;
        _nodes = nodes;
        _edges = edges;
        _conditionalEdges = conditionalEdges;
        _parallelBranches = parallelBranches;
        _interruptBefore = interruptBefore;
        _interruptAfter = interruptAfter;
        _options = options;
    }

    public async Task<StateGraphResult<TState>> InvokeAsync(TState initialState, CancellationToken ct = default)
    {
        var trace = new List<StateGraphTraceStep>();
        var currentState = initialState;
        string currentNode = _edges[StateGraphConstants.StartNode];
        int cycles = 0;

        while (!string.IsNullOrEmpty(currentNode) && !currentNode.Equals(StateGraphConstants.EndNode, StringComparison.OrdinalIgnoreCase))
        {
            cycles++;
            if (cycles > _options.MaxCycles)
            {
                throw new StateGraphCycleLimitException(_options.MaxCycles, cycles);
            }

            // Check interrupt before
            if (_interruptBefore.Contains(currentNode))
            {
                _pendingNextNode = currentNode;
                _lastState = currentState;
                return new StateGraphResult<TState>
                {
                    State = currentState,
                    IsInterrupted = true,
                    InterruptedNode = currentNode,
                    InterruptedPhase = "before",
                    TotalSteps = cycles,
                    ExecutionTrace = trace
                };
            }

            var startTimestamp = DateTime.UtcNow;

            // Handle parallel branches or normal node execution
            if (_parallelBranches.TryGetValue(currentNode, out var parallelConfig))
            {
                var tasks = parallelConfig.Branches.Select(async branchName =>
                {
                    if (_nodes.TryGetValue(branchName, out var branchHandler))
                    {
                        return await branchHandler(currentState, ct);
                    }
                    return currentState;
                });

                var branchResults = await Task.WhenAll(tasks);
                currentState = parallelConfig.Reducer(currentState, branchResults);
            }
            else if (_nodes.TryGetValue(currentNode, out var handler))
            {
                currentState = await handler(currentState, ct);
            }
            else
            {
                throw new InvalidOperationException($"Node '{currentNode}' is referenced in graph '{Name}' but has no handler registered.");
            }

            var duration = DateTime.UtcNow - startTimestamp;

            // Check interrupt after
            if (_interruptAfter.Contains(currentNode))
            {
                var nextAfterInterrupt = ResolveNextNode(currentNode, currentState);
                _pendingNextNode = nextAfterInterrupt;
                _lastState = currentState;

                if (_options.RecordTrace)
                {
                    trace.Add(new StateGraphTraceStep(currentNode, cycles, duration, nextAfterInterrupt, "Interrupted after execution"));
                }

                return new StateGraphResult<TState>
                {
                    State = currentState,
                    IsInterrupted = true,
                    InterruptedNode = currentNode,
                    InterruptedPhase = "after",
                    TotalSteps = cycles,
                    ExecutionTrace = trace
                };
            }

            // Route to next node
            string? nextNode = ResolveNextNode(currentNode, currentState);

            if (_options.RecordTrace)
            {
                trace.Add(new StateGraphTraceStep(currentNode, cycles, duration, nextNode));
            }

            if (string.IsNullOrEmpty(nextNode))
            {
                break;
            }

            currentNode = nextNode;
        }

        return new StateGraphResult<TState>
        {
            State = currentState,
            IsInterrupted = false,
            TotalSteps = cycles,
            ExecutionTrace = trace
        };
    }

    public async Task<StateGraphResult<TState>> ResumeAsync(TState resumedState, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_pendingNextNode))
        {
            throw new InvalidOperationException("Cannot resume graph: No pending execution or interrupt checkpoint found.");
        }

        string startNode = _pendingNextNode;
        _pendingNextNode = null;
        _lastState = default;

        var trace = new List<StateGraphTraceStep>();
        var currentState = resumedState;
        string currentNode = startNode;
        int cycles = 0;

        while (!string.IsNullOrEmpty(currentNode) && !currentNode.Equals(StateGraphConstants.EndNode, StringComparison.OrdinalIgnoreCase))
        {
            cycles++;
            if (cycles > _options.MaxCycles)
            {
                throw new StateGraphCycleLimitException(_options.MaxCycles, cycles);
            }

            var startTimestamp = DateTime.UtcNow;

            if (_parallelBranches.TryGetValue(currentNode, out var parallelConfig))
            {
                var tasks = parallelConfig.Branches.Select(async branchName =>
                {
                    if (_nodes.TryGetValue(branchName, out var branchHandler))
                    {
                        return await branchHandler(currentState, ct);
                    }
                    return currentState;
                });

                var branchResults = await Task.WhenAll(tasks);
                currentState = parallelConfig.Reducer(currentState, branchResults);
            }
            else if (_nodes.TryGetValue(currentNode, out var handler))
            {
                currentState = await handler(currentState, ct);
            }
            else
            {
                throw new InvalidOperationException($"Node '{currentNode}' is referenced in graph '{Name}' but has no handler registered.");
            }

            var duration = DateTime.UtcNow - startTimestamp;

            if (_interruptAfter.Contains(currentNode))
            {
                var nextAfterInterrupt = ResolveNextNode(currentNode, currentState);
                _pendingNextNode = nextAfterInterrupt;
                _lastState = currentState;

                if (_options.RecordTrace)
                {
                    trace.Add(new StateGraphTraceStep(currentNode, cycles, duration, nextAfterInterrupt, "Interrupted after resume"));
                }

                return new StateGraphResult<TState>
                {
                    State = currentState,
                    IsInterrupted = true,
                    InterruptedNode = currentNode,
                    InterruptedPhase = "after",
                    TotalSteps = cycles,
                    ExecutionTrace = trace
                };
            }

            string? nextNode = ResolveNextNode(currentNode, currentState);

            if (_options.RecordTrace)
            {
                trace.Add(new StateGraphTraceStep(currentNode, cycles, duration, nextNode));
            }

            if (string.IsNullOrEmpty(nextNode))
            {
                break;
            }

            currentNode = nextNode;
        }

        return new StateGraphResult<TState>
        {
            State = currentState,
            IsInterrupted = false,
            TotalSteps = cycles,
            ExecutionTrace = trace
        };
    }

    private string? ResolveNextNode(string currentNode, TState state)
    {
        // 1. Conditional edge takes priority
        if (_conditionalEdges.TryGetValue(currentNode, out var cond))
        {
            var routeKey = cond.Router(state);
            if (cond.Mapping != null && cond.Mapping.TryGetValue(routeKey, out var target))
            {
                return target;
            }
            return routeKey;
        }

        // 2. Direct unconditional edge
        if (_edges.TryGetValue(currentNode, out var directTarget))
        {
            return directTarget;
        }

        return null;
    }
}

public sealed class StateGraph : StateGraph<Dictionary<string, object?>>
{
    public StateGraph(string name = "StateGraph") : base(name)
    {
    }
}
