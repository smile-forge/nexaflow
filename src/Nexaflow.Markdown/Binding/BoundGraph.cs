using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Markdown.Binding;

/// <summary>
/// A graph too big to write out whole, bound into content a piece at a time: it keeps which of its nodes are opened, walks
/// the graph those say is visible, and writes what it walked as the text the binding reads.
///
/// <para>
/// <strong>The rules are the walk's.</strong> What opening a node shows — its children, or nothing new because it is a cycle,
/// or only the first few of a thousand — is the host's to say, and it says it once, in the walk it hands this: a function
/// from the set of opened nodes to the graph they show. How that graph reads in the content is the writer's. Everything else
/// is here: the set, walking again when it changes, and never letting a walk that was overtaken land.
/// </para>
/// <para>
/// <strong>Walked away from the thread that asked.</strong> A walk may read files; the content is read where the binding
/// stands and that cannot wait. So what it says is whatever the last walk to land wrote, and <see cref="Changed"/> is raised
/// when a new one lands — on the thread the walk finished on, which whoever follows it moves to its own.
/// </para>
/// </summary>
/// <typeparam name="TGraph">What a walk comes to.</typeparam>
public sealed class BoundGraph<TGraph> : IBoundContent where TGraph : class
{
    private readonly Func<IReadOnlySet<string>, CancellationToken, Task<TGraph>> _walk;
    private readonly Func<TGraph, string> _write;
    private readonly HashSet<string> _opened;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _walking;

    /// <param name="walk">What the nodes named opened show — worked out however long that takes, and given up when told to.</param>
    /// <param name="write">What a walked graph reads as where the binding stands.</param>
    /// <param name="keys">How two keys are told apart; ordinal where nothing says.</param>
    public BoundGraph(Func<IReadOnlySet<string>, CancellationToken, Task<TGraph>> walk, Func<TGraph, string> write,
                      IEqualityComparer<string>? keys = null)
    {
        _walk = walk;
        _write = write;
        _opened = new HashSet<string>(keys ?? StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public string Text { get; private set; } = string.Empty;

    /// <summary>What the last walk to land came to, or null before one has.</summary>
    public TGraph? Graph { get; private set; }

    /// <summary>Whether a walk is under way that has not landed yet.</summary>
    public bool Walking { get; private set; }

    /// <summary>What the last walk fell over with, where it fell over — null where it landed.</summary>
    public Exception? Fault { get; private set; }

    /// <summary>The nodes opened, by key.</summary>
    public IReadOnlySet<string> Opened
    {
        get
        {
            lock (_gate) return new HashSet<string>(_opened, _opened.Comparer);
        }
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <summary>Raised when a walk lands, with what it walked — for whatever else shows the same graph, a tree beside the diagram.</summary>
    public event EventHandler<TGraph>? Walked;

    /// <inheritdoc/>
    public void Expand(string key, bool open)
    {
        lock (_gate)
        {
            if (!(open ? _opened.Add(key) : _opened.Remove(key))) return;
        }

        Walk();
    }

    /// <summary>Closes every node, and walks what that leaves.</summary>
    public void Reset()
    {
        lock (_gate) _opened.Clear();

        Walk();
    }

    /// <summary>Walks again with what is opened — the first time, and whenever what the walk reads may have changed.</summary>
    public void Walk()
    {
        CancellationToken walking;
        IReadOnlySet<string> opened;

        lock (_gate)
        {
            _walking?.Cancel();
            _walking = new CancellationTokenSource();
            walking = _walking.Token;
            opened = new HashSet<string>(_opened, _opened.Comparer);
            Walking = true;
        }

        _ = Landed(opened, walking);
    }

    /// <summary>A walk, carried to where it lands — unless another was asked for first, which is then the one that lands.</summary>
    private async Task Landed(IReadOnlySet<string> opened, CancellationToken walking)
    {
        TGraph graph;

        try
        {
            graph = await _walk(opened, walking).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (walking.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (walking.IsCancellationRequested) return;

                Fault = error;
                Walking = false;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var text = _write(graph);

        lock (_gate)
        {
            if (walking.IsCancellationRequested) return;

            Graph = graph;
            Text = text;
            Fault = null;
            Walking = false;
        }

        Walked?.Invoke(this, graph);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
