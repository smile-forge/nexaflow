using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Nexaflow.Services.Initiatives.Hosting;

/// <summary>
/// Which products currently have a live <see cref="InitiativesHost"/>, and who is keeping each one alive.
/// <para>
/// The daemon needs none of this: it holds exactly one host for its whole life. An application does, because
/// several surfaces look at the same product at once — the Product page, the integrity page, the graph
/// viewer, a folder viewlet — and they open and close independently. Whichever arrives first should pay for
/// loading the tree; the rest should find it already there; and when the last one goes the memory should go
/// with it. That is a reference count, and writing it once here is better than four view-models each having
/// an opinion about whose host it is.
/// </para>
/// <para>
/// Deliberately not injected. A host is identified by a product root, which is a runtime value a caller
/// discovers rather than a dependency a container could know about — the shell's FeatureManager has never
/// heard of this assembly and has nothing sensible to hand a feature here. So the first page to want one
/// creates it and the last to finish with it lets it go, which is the lifetime the thing actually has.
/// </para>
/// <para>
/// Release is immediate rather than lingering. A host holds a whole knowledge graph, and keeping hundreds of
/// megabytes warm against the chance that someone reopens the tab is the wrong trade in a process that also
/// has to render.
/// </para>
/// </summary>
public static class InitiativesHosts
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Live = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Entry(InitiativesHost host)
    {
        public readonly InitiativesHost Host = host;
        public int Holders;
    }

    /// <summary>
    /// Takes a share in the host for <paramref name="productRoot"/>, creating one if this is the first. Dispose
    /// the lease when the surface that took it closes.
    /// </summary>
    /// <param name="created">
    /// True when this call is what brought the host into being. The caller uses it to do the once-per-host work
    /// — reading the tree, bringing the graph up to date — rather than every surface repeating it, or none of
    /// them doing it because each assumed another had.
    /// </param>
    public static InitiativesLease Acquire(string productRoot, out bool created)
    {
        var key = Key(productRoot);

        lock (Gate)
        {
            created = !Live.TryGetValue(key, out var entry);
            if (created) Live[key] = entry = new Entry(new InitiativesHost(productRoot));

            entry!.Holders++;
            return new InitiativesLease(key, entry.Host);
        }
    }

    /// <summary>As above, for a caller with no once-per-host work of its own.</summary>
    public static InitiativesLease Acquire(string productRoot) => Acquire(productRoot, out _);

    /// <summary>
    /// The host for this product <i>if something is already holding one</i>, and null otherwise — without
    /// taking a share.
    /// <para>
    /// This is what lets code that is handed nothing but a path still benefit: the client tools are static
    /// and per-root by design, and asking here means they answer from the warm copy while a page is open and
    /// fall back to reading from disk when none is. Correctness never depends on the lease; only speed does.
    /// </para>
    /// </summary>
    public static InitiativesHost? Warm(string productRoot)
    {
        lock (Gate) return Live.TryGetValue(Key(productRoot), out var entry) ? entry.Host : null;
    }

    /// <summary>How many products are live. For tests, and for anything wanting to assert that closing the
    /// last page really did let go.</summary>
    public static int Count
    {
        get { lock (Gate) return Live.Count; }
    }

    internal static void Release(string key)
    {
        InitiativesHost? closing = null;

        lock (Gate)
        {
            if (!Live.TryGetValue(key, out var entry)) return;
            if (--entry.Holders > 0) return;

            Live.Remove(key);
            closing = entry.Host;
        }

        // Outside the lock: disposing flushes a graph, which is not work to hold a global lock through.
        closing.Dispose();
    }

    private static string Key(string productRoot)
    {
        try   { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(productRoot)); }
        catch (ArgumentException) { return productRoot; }
    }
}

/// <summary>
/// One surface's share in a host. Disposing it says that surface is finished; the host goes when the last
/// share does.
/// </summary>
public sealed class InitiativesLease : IDisposable
{
    private readonly string _key;
    private int _released;

    internal InitiativesLease(string key, InitiativesHost host)
    {
        _key = key;
        Host = host;
    }

    public InitiativesHost Host { get; }

    /// <summary>Idempotent, because a view-model disposed twice must not hand back a share it does not
    /// have — that would close a host still being used by somebody else.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0) InitiativesHosts.Release(_key);
    }
}
