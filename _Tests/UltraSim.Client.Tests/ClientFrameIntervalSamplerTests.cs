using UltraSim.Client.Core;

internal static class ClientFrameIntervalSamplerTests
{
    internal static void Run()
    {
        ExactDistributionAndWarmup();
        IneligibleGapsAndRealHitches();
        ChronologicalBoundedRingAndMinMaxGraph();
        WarmedHotPathDoesNotAllocate();
    }

    private static void ExactDistributionAndWarmup()
    {
        var sampler = new ClientFrameIntervalSampler(1_000_000);
        Assert.False(sampler.Observe(1_000_000, true));
        long now = 1_000_000;
        for (int index = 0; index < 99; index++)
        {
            now += 10_000;
            Assert.True(sampler.Observe(now, true));
        }
        Assert.False(sampler.Summarize().OnePercentLowReady);
        now += 100_000;
        sampler.Observe(now, true);
        ClientFrameIntervalSummary hundred = sampler.Summarize();
        Assert.Equal(100, hundred.Samples);
        Assert.Equal(10d, hundred.P99Milliseconds);
        Assert.Equal(10d, hundred.P95Milliseconds);
        Assert.Equal(10d, hundred.OnePercentLowFps);
        Assert.True(hundred.PointOnePercentLowFps is null);
        Assert.Equal(1090d, hundred.EligibleMilliseconds);
        AssertClose(100_000d / 1090, hundred.AverageFps!.Value);
        for (int index = 100; index < 1000; index++)
        {
            now += 10_000;
            sampler.Observe(now, true);
        }
        ClientFrameIntervalSummary thousand = sampler.Summarize();
        Assert.True(thousand.PointOnePercentLowReady);
        Assert.Equal(10d, thousand.P999Milliseconds);
        Assert.Equal(10d, thousand.PointOnePercentLowFps);
        Assert.Equal(1, thousand.Over50Milliseconds);
        Assert.Equal(0, thousand.Over100Milliseconds); // strictly greater than threshold
    }

    private static void IneligibleGapsAndRealHitches()
    {
        var sampler = new ClientFrameIntervalSampler(1_000_000);
        sampler.Observe(0, true);
        sampler.Observe(16_000, true);
        Assert.False(sampler.Observe(4_016_000, false)); // Alt-Tab is not a four-second frame.
        Assert.False(sampler.Observe(8_016_000, true)); // First refocused frame primes the clock.
        Assert.True(sampler.Observe(8_096_000, true)); // Real gameplay hitch stays in the census.
        ClientFrameIntervalSummary result = sampler.Summarize();
        Assert.Equal(2, result.Samples);
        Assert.Equal(80d, result.MaximumMilliseconds);
        Assert.Equal(1, result.Over50Milliseconds);
        Assert.Equal(0, result.Over100Milliseconds);
    }

    private static void ChronologicalBoundedRingAndMinMaxGraph()
    {
        var sampler = new ClientFrameIntervalSampler(1_000_000);
        long now = 0;
        sampler.Observe(now, true);
        for (int index = 0; index < ClientFrameIntervalSampler.Capacity + 10; index++)
        {
            now += 1000;
            sampler.Observe(now, true);
        }
        now += 77_000;
        sampler.Observe(now, true);
        Assert.Equal(ClientFrameIntervalSampler.Capacity, sampler.Count);
        Assert.Equal((ulong)ClientFrameIntervalSampler.Capacity + 11, sampler.TotalSamples);
        var samples = new uint[ClientFrameIntervalSampler.Capacity];
        int count = sampler.CopyWindowSamples(samples, 30, out ulong covered);
        Assert.Equal(ClientFrameIntervalSampler.Capacity, count);
        Assert.Equal(77_000u, samples[count - 1]);
        Assert.Equal((ulong)(ClientFrameIntervalSampler.Capacity - 1) * 1000 + 77_000,
            covered);
        var columns = new ClientFrameGraphColumn[1];
        sampler.FillGraphColumns(columns);
        Assert.Equal(1000u, columns[0].MinimumMicroseconds);
        Assert.Equal(77_000u, columns[0].MaximumMicroseconds);
        Assert.Equal(ClientFrameIntervalSampler.Capacity, columns[0].Samples);
        sampler.Reset();
        Assert.Equal(0, sampler.Count);
        Assert.Equal(0UL, sampler.TotalSamples);
    }

    private static void WarmedHotPathDoesNotAllocate()
    {
        var sampler = new ClientFrameIntervalSampler(1_000_000);
        long now = 0;
        for (int index = 0; index < 20000; index++)
        {
            now += 16_667;
            sampler.Observe(now, true);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 20000; index++)
        {
            now += 16_667;
            sampler.Observe(now, true);
        }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void AssertClose(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
