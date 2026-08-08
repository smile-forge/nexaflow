using System;
using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a reader has done to one diagram: what they have opened, what they have selected, and where
/// they have panned and zoomed to.
/// <para>
/// It lives outside the rendered element because the element does not survive. A host that generates
/// its diagram re-emits the whole markdown to answer an expand, which rebuilds the document and every
/// element in it — so anything held on the element alone is lost exactly when the user did something.
/// That is how opening one node used to close the "+N more" beside it and throw away the zoom.
/// </para>
/// <para>
/// Keyed by the producer's own names rather than by mermaid ids, because the ids are positional and
/// shift the moment the graph grows.
/// </para>
/// </summary>
public sealed class DiagramViewState
{
    /// <summary>Nodes the reader has opened or closed, by key.</summary>
    public Dictionary<string, bool> Expansion { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The selected node's key, or null.</summary>
    public string? Selected { get; set; }

    /// <summary>The pan/zoom, once there has been one. Zero scale means "never viewed" — fit it.</summary>
    public double Scale { get; private set; }
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    public bool HasViewport => Scale > 0;

    public void RememberViewport(double scale, double x, double y)
    {
        Scale   = scale;
        OffsetX = x;
        OffsetY = y;
    }
}

/// <summary>
/// The per-surface store of <see cref="DiagramViewState"/>, one entry per diagram in the document.
/// <para>
/// Diagrams are identified by their position in the document: a re-render of the same document
/// produces the same diagrams in the same order, and there is nothing else stable to key on — the
/// source text is precisely what changed.
/// </para>
/// </summary>
public sealed class DiagramViewStates
{
    private readonly List<DiagramViewState> _states = [];
    private int _next;

    /// <summary>Starts a render pass. Call before building a document.</summary>
    public void Rewind() => _next = 0;

    /// <summary>The state for the next diagram in document order, created on first sight.</summary>
    public DiagramViewState Next()
    {
        while (_states.Count <= _next) _states.Add(new DiagramViewState());
        return _states[_next++];
    }

    /// <summary>Forgets everything, so the next render starts fitted and unselected. For a host
    /// command that means "start over" — collapsing a whole tree, or loading a different one.</summary>
    public void Clear()
    {
        _states.Clear();
        _next = 0;
    }
}
