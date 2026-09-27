using System;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The selected node changed. <paramref name="Key"/> is null when the selection was dropped.
/// <para>
/// Selection is the diagram's own state — it draws the node and its edges differently — but a host
/// can follow it to show detail beside the diagram, which is what turns "this node is here" into
/// "and this is why".
/// </para>
/// </summary>
public readonly record struct DiagramSelection(string? NodeId, string? Key, string? Label);
