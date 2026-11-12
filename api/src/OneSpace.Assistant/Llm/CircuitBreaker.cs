namespace OneSpace.Assistant.Llm;

/// <summary>
/// Per-provider breaker: after <c>threshold</c> consecutive failures the provider is skipped for the break
/// duration, then tried again (half-open). One success closes it.
/// </summary>
public sealed class CircuitBreaker(int threshold, TimeSpan breakDuration, TimeProvider clock)
{
    private readonly Lock _lock = new();
    private int _failures;
    private DateTimeOffset? _openUntil;

    public bool IsAvailable
    {
        get
        {
            lock (_lock) return _openUntil is null || clock.GetUtcNow() >= _openUntil;
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failures = 0;
            _openUntil = null;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _failures++;
            if (_failures >= threshold) _openUntil = clock.GetUtcNow() + breakDuration;
        }
    }
}
