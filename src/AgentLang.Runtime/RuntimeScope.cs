using System.Collections.Concurrent;

namespace AgentLang.Runtime;

public sealed class RuntimeScope
{
    private readonly ConcurrentDictionary<string, object?> _variables = new(StringComparer.Ordinal);
    public RuntimeScope? Parent { get; }
    public string Name { get; }

    public RuntimeScope(string name, RuntimeScope? parent = null)
    {
        Name = name;
        Parent = parent;
    }

    public void SetLocal(string name, object? value)
    {
        _variables[name] = value;
    }

    public void Assign(string name, object? value)
    {
        if (_variables.ContainsKey(name) || Parent == null)
        {
            _variables[name] = value;
        }
        else
        {
            Parent.Assign(name, value);
        }
    }

    public bool TryGet(string name, out object? value)
    {
        if (_variables.TryGetValue(name, out value))
            return true;
        if (Parent != null)
            return Parent.TryGet(name, out value);
        value = null;
        return false;
    }

    public object? Get(string name)
    {
        if (TryGet(name, out var val))
            return val;
        return null;
    }

    public bool Contains(string name) =>
        _variables.ContainsKey(name) || (Parent?.Contains(name) ?? false);
}

public sealed class EventBus
{
    private readonly ConcurrentDictionary<string, List<Func<object?, Task>>> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public void Subscribe(string eventName, Func<object?, Task> handler)
    {
        _handlers.AddOrUpdate(
            eventName,
            [handler],
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(handler);
                }
                return list;
            });
    }

    public async Task PublishAsync(string eventName, object? payload)
    {
        if (_handlers.TryGetValue(eventName, out var list))
        {
            List<Func<object?, Task>> copy;
            lock (list)
            {
                copy = [.. list];
            }

            foreach (var handler in copy)
            {
                await handler(payload);
            }
        }
    }
}
