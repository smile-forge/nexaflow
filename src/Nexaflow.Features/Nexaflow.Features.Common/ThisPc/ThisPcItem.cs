using System.IO;

namespace Nexaflow.Features.Common.ThisPc;

/// <summary>How a <see cref="ThisPcItem"/>'s contents are reached.</summary>
public enum ThisPcItemBacking
{
    /// <summary><see cref="ThisPcItem.TargetPath"/> is a real directory. The browser registers it as a
    /// pass-through mount, so the user navigates it under the item's own virtual root and never sees
    /// where it actually lives — while every ordinary file operation still works.</summary>
    LocalPath,

    /// <summary>The provider serves the namespace itself rather than pointing at a directory (a cloud
    /// API, say). Consumers that can only navigate real paths — the file and folder pickers — skip
    /// these. Nothing emits this yet; it exists so adding one later is not a contract change.</summary>
    Virtual,
}

/// <summary>
/// What kind of place a This PC row is, for icon purposes. Semantic only: a provider never supplies a
/// glyph, a colour or a brush, because each surface owns its own visual vocabulary — themed vectors in
/// the file browser, plain glyphs in the pickers — and a theme must be able to retune either.
/// <para>
/// It covers physical drives as well as contributed locations so that every surface classifies the whole
/// list through one enum. Deliberately no SSD: that is a refinement the browser probes for after the
/// fact, not something a contributor gets to claim.
/// </para>
/// </summary>
public enum ThisPcItemIcon { Disk, Removable, Optical, Network, Cloud, Folder }

/// <summary>
/// One extra row in "This PC", contributed by an <see cref="IThisPcItemProvider"/> — a cloud sync root
/// today; a network location or a pinned folder just as easily.
/// </summary>
public sealed record ThisPcItem
{
    /// <summary>
    /// Stable, provider-namespaced identity, e.g. <c>onedrive.Business1</c>. It becomes the virtual root
    /// <c>::{Id}</c>, so it must survive a rename — saved tabs and restored sessions are keyed on it —
    /// and must be a single path segment: no <c>\</c>, <c>/</c> or <c>:</c>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Row text, e.g. "OneDrive – Contoso". Free to change; nothing persists it.</summary>
    public required string Label { get; init; }

    /// <summary>What the row points at — a real directory for <see cref="ThisPcItemBacking.LocalPath"/>.</summary>
    public required string TargetPath { get; init; }

    /// <summary>The "Type" column, e.g. "OneDrive".</summary>
    public required string TypeLabel { get; init; }

    public ThisPcItemBacking Backing { get; init; } = ThisPcItemBacking.LocalPath;
    public ThisPcItemIcon    Icon    { get; init; } = ThisPcItemIcon.Cloud;

    /// <summary>Order among this provider's own rows.</summary>
    public int SortOrder { get; init; }
}

/// <summary>Whether a place is a volume Windows reported or a location a feature contributed.</summary>
public enum ThisPcPlaceKind { Drive, Provided }

/// <summary>
/// One top-level place in "This PC", whatever its origin — the single vocabulary every surface builds its
/// own row type from, so the drive list and the pickers can't drift apart in labelling, ordering or
/// iconography the way four independent <c>DriveInfo.GetDrives()</c> loops did.
/// </summary>
public sealed record ThisPcPlace
{
    public required ThisPcPlaceKind Kind { get; init; }

    /// <summary>Where the place is on disk. For a contributed location this is its real target: the file
    /// browser navigates the virtual root instead, but a picker has to hand back an openable path.</summary>
    public required string RealPath { get; init; }

    public required string         Label     { get; init; }
    public required string         TypeLabel { get; init; }
    public required ThisPcItemIcon Icon      { get; init; }

    /// <summary>The contributed item, when <see cref="Kind"/> is <see cref="ThisPcPlaceKind.Provided"/>.</summary>
    public ThisPcItem? Item { get; init; }

    /// <summary>The volume, when <see cref="Kind"/> is <see cref="ThisPcPlaceKind.Drive"/> — the browser
    /// reads capacity and refines the icon from it on a background thread.</summary>
    public DriveInfo? Drive { get; init; }
}

/// <summary>The part of a row that costs real work to discover, filled in off the UI thread.</summary>
public sealed record ThisPcItemDetail
{
    /// <summary>False when the target has gone (a sync folder deleted, a share offline) — the row shows
    /// the unavailable badge rather than silently pretending to work.</summary>
    public bool Available { get; init; }

    /// <summary>Whether to offer an expander in the folder tree.</summary>
    public bool HasChildren { get; init; }

    /// <summary>Capacity figures, or 0 when unknown — an unknown size renders as an empty cell rather
    /// than a misleading zero. Not worth a recursive walk to compute.</summary>
    public long UsedBytes  { get; init; }
    public long TotalBytes { get; init; }

    /// <summary>A better label now that the slow work is done, or null to keep the fast one.</summary>
    public string? Label { get; init; }
}
