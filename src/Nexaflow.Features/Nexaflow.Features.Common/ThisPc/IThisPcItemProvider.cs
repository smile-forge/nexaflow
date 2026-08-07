using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Common.ThisPc;

/// <summary>
/// Contributes extra top-level rows to "This PC" beside the physical drives — cloud sync roots today,
/// network locations or pinned folders on the same seam later.
/// <para>
/// The two halves mirror how drives already load: <see cref="GetItems"/> is the cheap synchronous list
/// (the analogue of <c>DriveInfo.GetDrives()</c>) so rows appear in the same frame as the drives, and
/// <see cref="GetDetailAsync"/> is the slow per-row probe (the analogue of <c>CheckDriveAsync</c>) that
/// settles each row's badge off the UI thread. Reusing that split means no new UI states.
/// </para>
/// <para>
/// Discovered by reflection — by the file browser through
/// <c>IShellServices.DiscoverImplementations&lt;IThisPcItemProvider&gt;()</c>, and by the shell's pickers
/// through <c>FeatureManager.GetThisPcItemProviders</c>. Constructors are resolved by the usual feature
/// DI, so they may take an <c>IFeatureConfig</c>, <c>IShellServices</c>, <c>IAIService</c>, the config
/// map, or nothing.
/// </para>
/// </summary>
public interface IThisPcItemProvider
{
    /// <summary>Short slug namespacing this provider's item ids, e.g. <c>onedrive</c>.</summary>
    string ProviderId { get; }

    /// <summary>Order of this provider's block relative to others. Physical drives are conceptually 0,
    /// and provided rows follow them.</summary>
    int SortOrder => 100;

    /// <summary>
    /// The rows to show, right now. Reads only what is already known — registry values, this feature's
    /// config, environment — so it is safe on the UI thread: no network, no directory enumeration, no
    /// blocking. An empty list is the normal answer when nothing is configured (the client isn't
    /// installed), and must never be an error.
    /// </summary>
    IReadOnlyList<ThisPcItem> GetItems();

    /// <summary>
    /// The slow half, run one task per row off the UI thread. The default probes the local path, which
    /// is right for every <see cref="ThisPcItemBacking.LocalPath"/> row; override only to answer faster
    /// or to describe a <see cref="ThisPcItemBacking.Virtual"/> row.
    /// </summary>
    Task<ThisPcItemDetail> GetDetailAsync(ThisPcItem item, CancellationToken ct = default)
        => ThisPcItemSet.ProbeLocalAsync(item, ct);

    /// <summary>
    /// Raised when the set <see cref="GetItems"/> would return has changed — the user edited Options, a
    /// sync client appeared. Consumers re-query. May fire on any thread; the file browser marshals.
    /// </summary>
    event Action? Changed;
}
