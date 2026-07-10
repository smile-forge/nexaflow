namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>Why a coverage advisory was raised.</summary>
public enum CoverageAdvisoryKind
{
    /// <summary>A test declares it covers this node (via <c>[CoversNode]</c>) but the node's <c>tests</c>
    /// concern has no matching snaplink — offer to add it.</summary>
    DeclaredButUnlinked,

    /// <summary>A test declares coverage of a node id that no longer exists in the tree — the id is stale.</summary>
    UnknownNode
}

/// <summary>
/// A non-gating suggestion produced by reconciling the test-coverage manifest against the tree. Unlike an
/// <see cref="IntegrityIssue"/> (which fails a release build), an advisory is informational: the tree stays
/// authoritative, and the Integrity page lets the user <em>choose</em> to turn a
/// <see cref="CoverageAdvisoryKind.DeclaredButUnlinked"/> advisory into a real <c>tests</c> snaplink.
/// </summary>
public sealed class CoverageAdvisory
{
    public CoverageAdvisoryKind Kind { get; set; }

    public string NodeId { get; set; } = string.Empty;
    public string NodeTitle { get; set; } = string.Empty;

    /// <summary>The declaring test assembly / class (fully-qualified) / method (null = class-level) / file.</summary>
    public string Assembly { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string? Method { get; set; }
    public string File { get; set; } = string.Empty;

    /// <summary>True when this advisory can be resolved by adding a snaplink (declared, and the file resolved).</summary>
    public bool CanAdd => Kind == CoverageAdvisoryKind.DeclaredButUnlinked && File.Length > 0;

    /// <summary>The <c>code</c> snaplink this advisory proposes — the simple class name is what the validator matches.</summary>
    public Snaplink ToSnaplink() => new()
    {
        Type = "code",
        Status = Model.Status.Done,   // the link points at a real, compiled test — it's proof, not an intention
        Doc = File,
        Class = SimpleName(Class),
        Method = string.IsNullOrWhiteSpace(Method) ? null : Method
    };

    /// <summary>Last dotted segment of a fully-qualified type name (namespaces + nesting stripped).</summary>
    public static string SimpleName(string fullName)
    {
        var cut = fullName.LastIndexOf('.');
        return cut >= 0 ? fullName[(cut + 1)..] : fullName;
    }
}
