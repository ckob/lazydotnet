namespace lazydotnet.Services;

/// <summary>
/// Combines per-source run events into a single view of the whole run.
/// A multi-project run emits progress/completed events per source; without
/// aggregation the last source to report would overwrite the others.
/// </summary>
public static class TestRunAggregator
{
    /// <summary>
    /// Sums the latest progress of every source. Returns null when no run is
    /// active (no expected total and no reports yet). The denominator is kept at
    /// <paramref name="expectedTotal"/> so it stays stable before every source
    /// has reported its own total.
    /// </summary>
    public static TestRunProgress? AggregateProgress(IReadOnlyCollection<TestRunProgress> perSource, int expectedTotal)
    {
        if (expectedTotal == 0 && perSource.Count == 0) return null;

        int completed = 0, passed = 0, failed = 0, skipped = 0, total = 0;
        foreach (var p in perSource)
        {
            completed += p.Completed;
            passed += p.Passed;
            failed += p.Failed;
            skipped += p.Skipped;
            total += p.Total;
        }

        return new TestRunProgress("", completed, passed, failed, skipped, Math.Max(total, expectedTotal));
    }

    /// <summary>
    /// Sums the completed counters of every source, but only once every started
    /// source has reported completion. Duration is the longest source duration,
    /// reflecting wall-clock time of the concurrent run. Returns null until the
    /// whole run is finished.
    /// </summary>
    public static TestRunCompleted? AggregateCompleted(IReadOnlyCollection<TestRunCompleted> completed, int startedSourceCount)
    {
        if (startedSourceCount == 0 || completed.Count < startedSourceCount) return null;

        int passed = 0, failed = 0, skipped = 0;
        long? duration = null;
        foreach (var c in completed)
        {
            passed += c.Passed;
            failed += c.Failed;
            skipped += c.Skipped;
            if (c.DurationMs is { } ms) duration = Math.Max(duration ?? 0, ms);
        }

        return new TestRunCompleted("", passed, failed, skipped, duration);
    }
}
