namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Deterministic percentile over a measured sample: values are sorted ascending and the
/// requested rank is linearly interpolated between the two closest ranks (the inclusive
/// definition, matching spreadsheet PERCENTILE.INC).
/// </summary>
public static class PercentileCalculator
{
    public static double Percentile(IEnumerable<double> samples, double percentile)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(percentile);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 100);

        var sorted = samples.Order().ToArray();

        if (sorted.Length == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        var rank = percentile / 100.0 * (sorted.Length - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);

        if (lowerIndex == upperIndex)
        {
            return sorted[lowerIndex];
        }

        var weight = rank - lowerIndex;
        return sorted[lowerIndex] + ((sorted[upperIndex] - sorted[lowerIndex]) * weight);
    }
}
