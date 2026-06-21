using System.Collections.Concurrent;
using System.Diagnostics;
using Nexaflow.Features.Processes.Services;

namespace Nexaflow.Tests.Features.Processes;

/// <summary>
/// Regression guard for the concurrency corruption in <see cref="LiveProcessSource"/>. The view models
/// drive <c>Snapshot()</c> and <c>ReadDetail()</c> from background threads (<c>Task.Run</c>), so the
/// source's caches must tolerate concurrent access. Before the caches were serialised this threw
/// "a concurrent update was performed on this collection and corrupted its state" from the unsynchronised
/// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> caches (e.g. PruneDead removing from
/// the meta cache while another Snapshot read it).
/// </summary>
[TestClass]
public class LiveProcessSourceConcurrencyTests
{
    [TestMethod]
    public void Concurrent_Snapshot_and_ReadDetail_do_not_corrupt_caches()
    {
        using var source = new LiveProcessSource();
        source.Snapshot();                       // warm the caches so PruneDead has entries to churn
        int pid = Environment.ProcessId;

        var errors = new ConcurrentQueue<Exception>();
        var clock  = Stopwatch.StartNew();
        int workers = Math.Max(4, Environment.ProcessorCount);

        var tasks = Enumerable.Range(0, workers).Select(i => Task.Run(() =>
        {
            // Mostly snapshots (they churn every cache); a quarter read detail to add cross-method races.
            while (clock.ElapsedMilliseconds < 1000 && errors.IsEmpty)
            {
                try
                {
                    if (i % 4 == 0) source.ReadDetail(pid);
                    else            source.Snapshot();
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            }
        })).ToArray();

        Task.WaitAll(tasks);

        Assert.IsTrue(errors.IsEmpty,
            $"Concurrent access corrupted a cache: {errors.FirstOrDefault()}");
    }
}
