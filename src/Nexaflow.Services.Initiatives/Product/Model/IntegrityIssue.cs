using System.Text.Json.Serialization;

namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>Why a snaplink failed validation. Serialized snake_case (<c>missing_file</c>, …).</summary>
public enum IntegrityKind
{
    /// <summary>A file-backed link with no <c>doc</c> path recorded.</summary>
    MissingDoc,

    /// <summary>The <c>doc</c> path no longer exists on disk (file moved, renamed or deleted).</summary>
    MissingFile,

    /// <summary>The markdown heading path is gone (heading renamed or re-nested).</summary>
    MissingHeading,

    /// <summary>The class is no longer declared in that file.</summary>
    MissingClass,

    /// <summary>The method is no longer declared on that class (or at file top level).</summary>
    MissingMethod,

    /// <summary>A <c>url</c> link with no target.</summary>
    EmptyTarget,

    /// <summary>A <c>url</c> link whose target isn't a well-formed absolute URI.</summary>
    InvalidUrl
}

/// <summary>
/// One broken snaplink. <see cref="NodeId"/> + <see cref="Concern"/> + <see cref="Index"/> is the handle
/// used to repair or remove the offending link — it locates the exact slot in the tree, which the
/// <see cref="Link"/> copy alone could not (the same link may appear on several nodes).
/// </summary>
public sealed class IntegrityIssue
{
    public string NodeId { get; set; } = string.Empty;
    public string NodeTitle { get; set; } = string.Empty;

    /// <summary>The concern tag whose snaplink broke, or <c>null</c> for a node-level snaplink.</summary>
    public string? Concern { get; set; }

    /// <summary>Position of the link within its list — the repair handle.</summary>
    public int Index { get; set; }

    public IntegrityKind Kind { get; set; }

    /// <summary>Human-readable explanation, e.g. "class 'Foo' is not declared in src/Foo.cs".</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>A copy of the offending link, so a saved report reads on its own without the tree.</summary>
    public Snaplink Link { get; set; } = new();

    /// <summary>Where the issue sits, for display: "node" or the concern tag.</summary>
    [JsonIgnore]
    public string Scope => Concern ?? "node";
}
