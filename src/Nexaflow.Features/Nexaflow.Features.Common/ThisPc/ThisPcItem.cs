namespace Nexaflow.Features.Common.ThisPc;

/// <summary>How a <see cref="ThisPcItem"/>'s contents are reached.</summary>
public enum ThisPcItemBacking
{
    /// <summary><see cref="ThisPcItem.TargetPath"/> is a real directory. The browser registers it as a
    /// pass-through mount, so the user navigates it under the item's own virtual root and never sees
    /// where it actually lives — while every ordinary file operation still works.</summary>
    LocalPath,

    /// <summary><see cref="ThisPcItem.TargetPath"/> is a real directory browsed at that path itself — a volume's root —
    /// so nothing is mounted for it, and the row opens where it is.</summary>
    OwnPath,

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
/// It covers the volumes as well as contributed locations so that every surface classifies the whole list
/// through one enum. <see cref="SolidState"/> is a refinement a row's detail makes once the device has
/// answered (<see cref="ThisPcItemDetail.Icon"/>); a volume is listed as a <see cref="Disk"/> until then.
/// </para>
/// </summary>
public enum ThisPcItemIcon { Disk, Removable, Optical, Network, Cloud, Folder, SolidState }

/// <summary>
/// One row in "This PC", offered by an <see cref="IThisPcItemProvider"/> — a volume, a cloud sync root, a network
/// location or a pinned folder alike.
/// </summary>
public sealed record ThisPcItem
{
    /// <summary>
    /// Stable, provider-namespaced identity, e.g. <c>onedrive.Business1</c>. A <see cref="ThisPcItemBacking.LocalPath"/>
    /// row becomes the virtual root <c>::{Id}</c>, so it must survive a rename — saved tabs and restored sessions are
    /// keyed on it — and must be a single path segment: no <c>\</c>, <c>/</c> or <c>:</c>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Row text, e.g. "OneDrive – Contoso". Free to change; nothing persists it.</summary>
    public required string Label { get; init; }

    /// <summary>What the row points at — a real directory for <see cref="ThisPcItemBacking.LocalPath"/> and
    /// <see cref="ThisPcItemBacking.OwnPath"/>.</summary>
    public required string TargetPath { get; init; }

    /// <summary>The "Type" column, e.g. "OneDrive".</summary>
    public required string TypeLabel { get; init; }

    public ThisPcItemBacking Backing { get; init; } = ThisPcItemBacking.LocalPath;
    public ThisPcItemIcon    Icon    { get; init; } = ThisPcItemIcon.Cloud;

    /// <summary>Order among this provider's own rows.</summary>
    public int SortOrder { get; init; }
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

    /// <summary>The file system's name — <c>NTFS</c>, <c>exFAT</c> — or null where there is none to report.</summary>
    public string? FileSystem { get; init; }

    /// <summary>A truer icon kind now that the device has answered — a solid-state volume — or null to keep the row's.</summary>
    public ThisPcItemIcon? Icon { get; init; }
}
