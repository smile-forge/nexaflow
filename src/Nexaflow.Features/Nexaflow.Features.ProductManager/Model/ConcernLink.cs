namespace Nexaflow.Features.ProductManager.Model;

/// <summary>
/// A node's stance on a cross-cutting concern: the concern (a reusable tag from
/// <see cref="ProductDocument.Concerns"/>) plus the <see cref="Status"/> of <em>this node's link</em> to it
/// — the status lives on the link, not on the concern. Artifacts like "tests" are just concerns.
/// </summary>
public sealed class ConcernLink
{
    public string Tag { get; set; } = string.Empty;
    public Status Status { get; set; } = Status.Should;

    /// <summary>Optional snaplinks attached to this concern↔node link (e.g. the doc/code that satisfies it).</summary>
    public List<Snaplink>? Snaplinks { get; set; }
}
