using System.Text.Json.Serialization;

namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>Why a snaplink failed validation. Serialized snake_case (<c>missing_file</c>, …).</summary>
public enum IntegrityKind
{
    /// <summary>A file-backed link with no <c>doc</c> path recorded.</summary>
    MissingDoc,

    /// <summary>The <c>doc</c> path no longer exists on disk (file moved, renamed or deleted).</summary>
    MissingFile,

    /// <summary>
    /// The <c>doc</c> points inside a linked git worktree (typically <c>.claude/worktrees/&lt;name&gt;/…</c>)
    /// rather than at the repo's own copy. The file resolves today and rots the moment that branch merges and
    /// the worktree is removed, so it is broken on arrival. <c>doctor --fix</c> re-roots it.
    /// </summary>
    WorktreePath,

    /// <summary>The markdown heading path is gone (heading renamed or re-nested).</summary>
    MissingHeading,

    /// <summary>The class is no longer declared in that file.</summary>
    MissingClass,

    /// <summary>The method is no longer declared on that class (or at file top level).</summary>
    MissingMethod,

    /// <summary>A <c>url</c> or <c>node</c> link with no target.</summary>
    EmptyTarget,

    /// <summary>A <c>url</c> link whose target isn't a well-formed absolute URI.</summary>
    InvalidUrl,

    /// <summary>A <c>node</c> link whose target node id is not in the tree (node deleted, renamed, or a typo).</summary>
    MissingNode,

    /// <summary>
    /// A concern link is <c>done</c>/<c>faulted</c> but carries no snaplink, and its concern def is marked
    /// <see cref="ConcernDef.RequiresSnaplink"/> — the claim has nothing backing it. Unlike the other kinds
    /// this hangs off the concern itself, not an existing link, so <see cref="IntegrityIssue.Index"/> is -1.
    /// </summary>
    MissingSnaplink,

    /// <summary>
    /// A test declares <c>[CoversNode("id")]</c> for an id that is not in the tree — the node was renamed or
    /// deleted and the declaration rotted, so the test claims to back something that does not exist. The
    /// gating counterpart of <see cref="CoverageAdvisoryKind.UnknownNode"/>: provable from the manifest and
    /// the tree alone, with no judgement about whether the test is *good* coverage.
    /// <para>
    /// Hangs off a test declaration rather than an existing link, so <see cref="IntegrityIssue.Index"/> is -1
    /// and <see cref="IntegrityIssue.Link"/> is empty — there is nothing in the tree to repair; the fix is in
    /// the test's attribute (or in restoring the node).
    /// </para>
    /// </summary>
    StaleCoverageNode,

    /// <summary>
    /// A feature or provider assembly ships on disk but no node under its family links its <c>.csproj</c>, so
    /// the tree — the inventory of what exists — does not know about it. It has no owning node, no concerns
    /// and no status, which is exactly how a shipped assembly goes untracked.
    /// <para>
    /// Found by walking the filesystem rather than an existing link, so <see cref="IntegrityIssue.Index"/> is
    /// -1 and <see cref="IntegrityIssue.NodeId"/> holds the family node the assembly *should* sit under —
    /// there is no node for it, which is the finding.
    /// </para>
    /// </summary>
    UnlinkedProject
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

/// <summary>
/// What a check could actually establish about one link. The third case is the point: the validator only
/// reports what it can <em>prove</em> broken, so "no problem found" and "nothing could be checked" arrive at
/// the same place — and telling a user their link is fixed when neither was established is a lie the UI told
/// for as long as the two were the same value.
/// </summary>
public enum LinkVerdict
{
    /// <summary>Checked, and the target is really there.</summary>
    Sound,

    /// <summary>Checked, and it is not. This is what gates a release build.</summary>
    Broken,

    /// <summary>Nothing could be established either way — no grammar for the file, a file too big or
    /// unreadable, or a <c>node</c> link checked with no tree in scope.</summary>
    Unverifiable,
}

/// <summary>The outcome of checking one link: the verdict, plus the failure when there is one.</summary>
public readonly record struct LinkCheck(LinkVerdict Verdict, IntegrityKind Kind, string? Detail)
{
    public static LinkCheck Sound() => new(LinkVerdict.Sound, default, null);
    public static LinkCheck Unverifiable() => new(LinkVerdict.Unverifiable, default, null);
    public static LinkCheck Broken(IntegrityKind kind, string detail) => new(LinkVerdict.Broken, kind, detail);
}
