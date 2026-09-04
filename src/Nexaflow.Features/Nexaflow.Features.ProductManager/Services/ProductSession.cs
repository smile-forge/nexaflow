using System;
using Nexaflow.Features.Common;
using Nexaflow.Services.Initiatives.Hosting;

namespace Nexaflow.Features.ProductManager.Services;

/// <summary>
/// One surface's hold on a product while it is open: the tree loaded once and watched, and a graph kept warm
/// beside it.
/// <para>
/// The first surface to open on a product creates the state; every surface after it finds it already there;
/// the last one to close lets it go. That is the lifetime the thing actually has, and it belongs to the
/// feature rather than to a container — the shell's FeatureManager has never heard of this domain and has
/// nothing sensible to inject. So a page owns its own session, and this is what it owns.
/// </para>
/// <para>
/// The same <see cref="InitiativesHost"/> that <c>nfi</c>'s resident process holds for the command line, with
/// only the lifetime different. That is what makes "the CLI and the assistant can do the same things" a
/// structural fact rather than two implementations that happen to agree today.
/// </para>
/// </summary>
public sealed class ProductSession : IDisposable
{
    private readonly InitiativesLease _lease;

    private ProductSession(InitiativesLease lease) => _lease = lease;

    /// <summary>The live state for this product — the same object every open surface on it is looking at.</summary>
    public InitiativesHost Host => _lease.Host;

    public string ProductRoot => _lease.Host.ProductRoot;

    /// <summary>
    /// What the graph is doing, when it is doing something worth saying: missing and being built, or stale
    /// and being brought up to date, with how far through it is. Null the rest of the time, which is most of
    /// it.
    /// </summary>
    public string? GraphStatus { get; private set; }

    /// <summary>Raised when <see cref="GraphStatus"/> changes. On a background thread — a view-model marshals
    /// it itself, because a feature never touches the dispatcher.</summary>
    public event Action? GraphStatusChanged;

    /// <summary>
    /// Opens — or joins — the session for <paramref name="productRoot"/>. Cheap for every caller but the
    /// first, which is also the one that pays for bringing the graph up to date.
    /// </summary>
    public static ProductSession Open(IShellServices shell, string productRoot)
    {
        var lease   = InitiativesHosts.Acquire(productRoot, out var created);
        var session = new ProductSession(lease);

        // Only the surface that created the host: a repo walk per opened tab would be absurd, and the ones
        // joining an existing session are joining state that is already being kept current.
        if (created) session.WarmUp(shell);

        return session;
    }

    private void WarmUp(IShellServices shell)
    {
        try { shell.QueueBackgroundTask(new GraphWarmUpTask(Host, Say), _ => { }); }
        catch (InvalidOperationException) { /* nothing to queue on; the tools still read from disk */ }
    }

    private void Say(string? status)
    {
        if (status == GraphStatus) return;

        GraphStatus = status;
        GraphStatusChanged?.Invoke();
    }

    public void Dispose() => _lease.Dispose();
}
