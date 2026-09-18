using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>Deterministic percentile math: sorted input, inclusive linear interpolation.</summary>
public sealed class PercentileCalculatorTests
{
    [Fact]
    public void Median_of_an_odd_sample_is_the_middle_value() =>
        Assert.Equal(3, PercentileCalculator.Percentile([5, 1, 3, 2, 4], 50));

    [Fact]
    public void Median_of_an_even_sample_interpolates_between_the_middle_values() =>
        Assert.Equal(5, PercentileCalculator.Percentile([8, 4, 2, 6], 50));

    [Fact]
    public void P95_of_one_to_one_hundred_is_the_interpolated_rank() =>
        Assert.Equal(95.05, PercentileCalculator.Percentile(Enumerable.Range(1, 100).Select(value => (double)value), 95), 6);

    [Fact]
    public void P95_of_three_samples_is_interpolated()
    {
        var percentile = PercentileCalculator.Percentile([1000, 2000, 3000], 95);

        Assert.Equal(2900, percentile, 6);
    }

    [Fact]
    public void P100_is_the_maximum_and_the_smallest_rank_is_the_minimum()
    {
        double[] samples = [10, 20, 30, 40];

        Assert.Equal(40, PercentileCalculator.Percentile(samples, 100));
        Assert.Equal(10.3, PercentileCalculator.Percentile(samples, 1), 6);
    }

    [Fact]
    public void A_single_sample_is_its_own_percentile() =>
        Assert.Equal(42, PercentileCalculator.Percentile([42], 95));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void An_out_of_range_percentile_is_rejected(double percentile) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PercentileCalculator.Percentile([1, 2, 3], percentile));

    [Fact]
    public void An_empty_sample_is_rejected() =>
        Assert.Throws<ArgumentException>(() => PercentileCalculator.Percentile([], 50));
}
