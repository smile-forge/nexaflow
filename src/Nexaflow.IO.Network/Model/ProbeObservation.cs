namespace Nexaflow.IO.Network.Model;

/// <summary>
/// One thing a probe saw. This is the <b>only</b> way information enters the graph: a probe never creates
/// or names a node, it describes what it observed and lets <see cref="DeviceGraph"/> decide identity.
/// Keeping probes out of the identity decision is what stops fifteen plugins each inventing their own
/// notion of "the same device".
/// </summary>
public sealed class ProbeObservation
{
    /// <summary>The probe id, e.g. <c>network.arp</c>, or <c>protocol:&lt;id&gt;</c> for a document.</summary>
    public required string SourceProbe { get; init; }

    /// <summary>When the observation was made.</summary>
    public required DateTimeOffset ObservedUtc { get; init; }

    /// <summary>
    /// Every key this observation offers as identity. At least one is required — an observation that can
    /// name nothing cannot be attributed and is dropped, which is a probe bug worth surfacing.
    /// </summary>
    public List<IdentityClaim> Identities { get; } = [];

    /// <summary>
    /// What was learned. <see cref="DeviceFact.SourceProbe"/> and <see cref="DeviceFact.ObservedUtc"/> are
    /// filled in from this observation if the probe left them unset, so a probe cannot accidentally
    /// misattribute its own claims.
    /// </summary>
    public List<DeviceFact> Facts { get; } = [];

    /// <summary>
    /// Relationships to other devices, expressed by identity claim rather than node id — the probe does
    /// not know node ids, and the far end may not exist yet.
    /// </summary>
    public List<PendingEdge> Edges { get; } = [];

    /// <summary>An edge whose far end is named by an identity claim, resolved (or created) by the graph.</summary>
    /// <param name="To">Identity of the far end.</param>
    /// <param name="Kind">Relationship.</param>
    /// <param name="Confidence">How strongly asserted.</param>
    /// <param name="Label">Optional annotation, e.g. a switch port.</param>
    public sealed record PendingEdge(IdentityClaim To, EdgeKind Kind, Confidence Confidence, string? Label = null);
}
