using System.Collections.Concurrent;

namespace AgentLang.VM.Scheduling;

public sealed class RateLimiter
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Queue<DateTime> _requestTimestamps = new();
    public int MaxRequests { get; }
    public TimeSpan WindowSize { get; }

    public RateLimiter(int maxRequests = 60, TimeSpan? windowSize = null)
    {
        MaxRequests = maxRequests;
        WindowSize = windowSize ?? TimeSpan.FromSeconds(1);
    }

    public async Task AcquireAsync(CancellationToken ct = default)
    {
        while (true)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var now = DateTime.UtcNow;
                var cutoff = now - WindowSize;

                while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < cutoff)
                {
                    _requestTimestamps.Dequeue();
                }

                if (_requestTimestamps.Count < MaxRequests)
                {
                    _requestTimestamps.Enqueue(now);
                    return;
                }

                var oldest = _requestTimestamps.Peek();
                var waitTime = (oldest + WindowSize) - now;
                if (waitTime <= TimeSpan.Zero)
                {
                    _requestTimestamps.Dequeue();
                    _requestTimestamps.Enqueue(now);
                    return;
                }

                // Need to wait until earliest timestamp expires
                await Task.Delay(waitTime, ct);
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public bool TryAcquire()
    {
        _lock.Wait();
        try
        {
            var now = DateTime.UtcNow;
            var cutoff = now - WindowSize;

            while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < cutoff)
            {
                _requestTimestamps.Dequeue();
            }

            if (_requestTimestamps.Count < MaxRequests)
            {
                _requestTimestamps.Enqueue(now);
                return true;
            }

            return false;
        }
        finally
        {
            _lock.Release();
        }
    }
}

public sealed class ToolScheduler
{
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly RateLimiter _rateLimiter;
    private long _executedCount;
    private int _queuedCount;
    private int _peakConcurrency;
    private int _currentConcurrency;

    public int MaxConcurrency { get; }
    public long ExecutedCount => Interlocked.Read(ref _executedCount);
    public int QueuedCount => _queuedCount;
    public int PeakConcurrency => _peakConcurrency;

    public ToolScheduler(int maxConcurrency = 8, int maxRequestsPerWindow = 120, TimeSpan? windowSize = null)
    {
        MaxConcurrency = maxConcurrency;
        _concurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _rateLimiter = new RateLimiter(maxRequestsPerWindow, windowSize);
    }

    public async Task<T> ScheduleAsync<T>(string toolName, Func<Task<T>> execution, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _queuedCount);
        try
        {
            await _rateLimiter.AcquireAsync(ct);
            await _concurrencySemaphore.WaitAsync(ct);
            try
            {
                int current = Interlocked.Increment(ref _currentConcurrency);
                lock (this)
                {
                    if (current > _peakConcurrency) _peakConcurrency = current;
                }

                return await execution();
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrency);
                _concurrencySemaphore.Release();
                Interlocked.Increment(ref _executedCount);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _queuedCount);
        }
    }
}
