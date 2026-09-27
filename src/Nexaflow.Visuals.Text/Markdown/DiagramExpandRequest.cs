using System;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A node's expand chip was clicked.
/// <para>
/// This is the counterpart of the link hook rather than a variation on it: a link says "go here",
/// an expansion says "there is more behind this one — show it". Smuggling the second through the
/// first (a private scheme on the href) makes a node with both actions impossible, because a
/// rendered node then has one gesture to spend on two meanings.
/// </para>
/// </summary>
/// <param name="NodeId">The node's id in the diagram source.</param>
/// <param name="Key">The producer's own name for it, from the front-matter, else the id. What a host
/// that generated the diagram wants back — it thinks in module names, not in <c>n7</c>.</param>
/// <param name="Label">The node's rendered label, for a message or a prompt.</param>
/// <param name="Expand">True to open the node, false to close it again.</param>
public readonly record struct DiagramExpandRequest(string NodeId, string Key, string Label, bool Expand);
