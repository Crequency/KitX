using System.Diagnostics;
using System.Globalization;

namespace KitX.ToolKit.Perf;

/// <summary>A single measured round.</summary>
public sealed record Measurement(double MeanMs, double P50Ms, double P99Ms)
{
    /// <summary>Formats a millisecond value to 3 significant digits.</summary>
    public static string Sig3(double ms)
    {
        if (ms == 0)
            return "0";
        var mag = Math.Floor(Math.Log10(Math.Abs(ms)));
        var scale = Math.Pow(10, 2 - mag);
        var rounded = Math.Round(ms * scale) / scale;
        return rounded.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>One markdown table row: <c>| name | scenario | mean | P50 | P99 |</c>.</summary>
    public string Row(string name, string scenario)
        => $"| {name} | {scenario} | {Sig3(MeanMs)} | {Sig3(P50Ms)} | {Sig3(P99Ms)} |";
}

/// <summary>
/// A minimal Stopwatch benchmark fixture. Runs <c>warmupRounds</c> un-timed rounds, then
/// <c>iterations</c> timed rounds collecting mean / P50 / P99 in milliseconds. Each timed
/// round runs <paramref name="round"/> once; the elapsed time is divided by
/// <paramref name="perUnit"/> so callers can report per-unit (e.g. per-message) cost.
/// </summary>
public static class Benchmark
{
    public static Measurement Measure(int warmupRounds, int iterations, Action round, double perUnit = 1.0)
    {
        for (var i = 0; i < warmupRounds; i++)
            round();

        var samples = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            round();
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds / perUnit;
        }

        Array.Sort(samples);
        return new Measurement(
            samples.Average(),
            samples[(int)(iterations * 0.50)],
            samples[Math.Min(iterations - 1, (int)(iterations * 0.99))]);
    }
}
