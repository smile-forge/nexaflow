using System.Diagnostics;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// How the decode benches time and weigh a decode — the median of a few batched runs, and what it allocates and costs
/// the collector — the same for every codec, so their figures compare.
/// </summary>
internal static class DecodeTiming
{
    /// <summary>How many times each decode is timed; the median is kept.</summary>
    private const int Runs = 5;

    /// <summary>
    /// A decode slower than this is timed once: its first run, which the pixel comparison has already warmed. Repeats
    /// buy nothing at that size, where a run is long beside the timer and the noise.
    /// </summary>
    private const double Slow = 1000;

    /// <summary>A timed run repeats a decode until it lasts this long, so a tiny image is not timer noise.</summary>
    private const double Batch = 5;

    /// <summary>How many decodes an allocation figure is averaged over.</summary>
    private const int MemoryRuns = 2;

    /// <summary>How many decodes warm a decoder up, at the least.</summary>
    public const int WarmUpDecodes = 64;

    /// <summary>
    /// How long warming a decoder up takes, at the least, in milliseconds: the JIT holds back its recompiling while
    /// methods are still being compiled for the first time, so a second is not enough.
    /// </summary>
    public const double WarmUpMs = 3000;

    /// <summary>
    /// The median of a decode's timed runs, in milliseconds, after one run to warm it — or that one run, when it is
    /// <see cref="Slow"/>.
    /// </summary>
    public static double Time(Action decode)
    {
        var clock = Stopwatch.StartNew();
        decode();
        var once = clock.Elapsed.TotalMilliseconds;
        if (once > Slow) return Round(once);

        var batch = Math.Max(1, (int)Math.Ceiling(Batch / Math.Max(once, 0.001)));
        var times = new double[Runs];

        for (var at = 0; at < times.Length; at++)
        {
            clock.Restart();
            for (var repeat = 0; repeat < batch; repeat++) decode();
            times[at] = clock.Elapsed.TotalMilliseconds / batch;
        }

        Array.Sort(times);
        return Round(times[times.Length / 2]);
    }

    /// <summary>
    /// What one decode allocates, across every thread it uses, and how many collections of each generation it costs —
    /// averaged over <see cref="MemoryRuns"/> decodes after one to warm it, so a pool or a cache filled once is not counted.
    /// </summary>
    public static DecodeMemory Allocated(Action decode)
    {
        decode();
        var (gen0, gen1, gen2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var before = GC.GetTotalAllocatedBytes(precise: true);

        for (var run = 0; run < MemoryRuns; run++) decode();

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        return new DecodeMemory(
            Round(allocated / 1024.0 / MemoryRuns),
            Round((GC.CollectionCount(0) - gen0) / (double)MemoryRuns),
            Round((GC.CollectionCount(1) - gen1) / (double)MemoryRuns),
            Round((GC.CollectionCount(2) - gen2) / (double)MemoryRuns));
    }

    /// <summary>
    /// Decodes <paramref name="items"/> over and over before anything is timed — until there have been
    /// <see cref="WarmUpDecodes"/> decodes and <see cref="WarmUpMs"/> has gone by — so the JIT has replaced the
    /// decoder's first, unoptimised code with its optimised code: a method is recompiled after 30 calls and a short
    /// delay, and a small image is otherwise timed in the few runs before that happens. A decode that fails is
    /// reported when it is measured.
    /// </summary>
    public static void WarmUp<T>(IReadOnlyList<T> items, Action<T> decode)
    {
        if (items.Count == 0) return;

        var clock = Stopwatch.StartNew();
        for (var decodes = 0; decodes < WarmUpDecodes || clock.Elapsed.TotalMilliseconds < WarmUpMs; )
        {
            foreach (var item in items)
            {
                try { decode(item); }
                catch (Exception) { }
                decodes++;
            }
        }
    }

    public static double Ratio(double managed, double native) => native <= 0 ? 0 : Math.Round(managed / native, 2);

    public static double Round(double value) => Math.Round(value, 3);
}

/// <summary>One decode's allocations, in KB across every thread, and the collections of each generation it caused.</summary>
internal sealed record DecodeMemory(double AllocatedKb, double Gen0, double Gen1, double Gen2);
