using System;
using System.Collections.Generic;

namespace Nexaflow.Tests.Core;

/// <summary>
/// Runs a body on a fresh STA thread and rethrows whatever it threw. WPF elements can only be constructed
/// and driven from an STA thread, so every UI-category test that builds a control goes through here.
/// <para>Lives in the root test namespace rather than beside any one suite: C# name lookup walks up
/// enclosing namespaces, so <c>Nexaflow.Tests.Core.Visuals.*</c> still sees it unqualified.</para>
/// </summary>
internal static class UiThread
{
    public static void Run(Action action)
    {
        Exception? caught = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        // Rethrow with the STA thread's own stack intact — a bare `throw caught` resets it to this
        // line, which leaves a failure inside the body with nothing to point at.
        if (caught is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
    }

    /// <summary>
    /// Runs a body over every item, on as many STA threads as there are processors, and rethrows the
    /// first thing any of them threw.
    /// <para>
    /// For work that is per-item and finished with when the item is: measuring one formula, say. WPF
    /// objects belong to the thread that made them, which is fine as long as each thread makes its own
    /// and lets go of them, and is why these are STA threads rather than the thread pool's.
    /// </para>
    /// <para>
    /// The first item is done alone before the rest start. Everything behind this — symbol tables, font
    /// metrics, the predefined formulas — is built on first use, and racing to build it is the one part
    /// of the work that is not safe to do twice at once.
    /// </para>
    /// </summary>
    public static void Across<T>(IReadOnlyList<T> items, Action<T> body)
    {
        if (items.Count == 0) return;

        Run(() => body(items[0]));

        var next = 1;
        Exception? caught = null;

        var threads = new System.Threading.Thread[Math.Min(Environment.ProcessorCount, items.Count)];

        for (var i = 0; i < threads.Length; i++)
        {
            threads[i] = new System.Threading.Thread(() =>
            {
                try
                {
                    for (var at = System.Threading.Interlocked.Increment(ref next) - 1;
                         at < items.Count;
                         at = System.Threading.Interlocked.Increment(ref next) - 1)
                        body(items[at]);
                }
                catch (Exception ex)
                {
                    System.Threading.Interlocked.CompareExchange(ref caught, ex, null);
                    System.Threading.Interlocked.Exchange(ref next, items.Count);   // stop the others
                }
            });

            threads[i].SetApartmentState(System.Threading.ApartmentState.STA);
            threads[i].Start();
        }

        foreach (var thread in threads) thread.Join();

        if (caught is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
    }
}
