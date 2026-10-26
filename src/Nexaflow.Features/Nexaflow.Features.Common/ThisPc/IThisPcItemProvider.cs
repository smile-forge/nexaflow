using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Common.ThisPc;

/// <summary>
/// Offers rows to "This PC". Nothing is there by default: the volumes come from the file browser's own provider, cloud
/// sync roots from OneDrive's, and network locations or pinned folders can arrive on the same seam.
/// <para>
/// The two halves keep This PC drawing at once: <see cref="GetItems"/> is the cheap synchronous list, so every row
/// appears in the same frame, and <see cref="GetDetailAsync"/> is the slow per-row probe that settles each row's badge
/// off the UI thread.
/// </para>
/// <para>
/// Discovered by reflection — by the file browser through
/// <c>IShellServices.DiscoverImplementations&lt;IThisPcItemProvider&gt;()</c>, and by the shell's pickers
/// through <c>FeatureManager.GetThisPcItemProviders</c>. Constructors are resolved by the usual feature
/// DI, so they may take an <c>IFeatureConfig</c>, <c>IShellServices</c>, <c>IAIService</c>, the config
/// map, the runtime's <c>IVirtualFileSystem</c>, or nothing.
/// </para>
/// </summary>
public interface IThisPcItemProvider
{
    /// <summary>Short slug namespacing this provider's item ids, e.g. <c>onedrive</c>.</summary>
    string ProviderId { get; }

    /// <summary>Order of this provider's block relative to others. The volumes are 0, and every other provider
    /// follows them.</summary>
    int SortOrder => 100;

    /// <summary>
    /// The rows to show, right now. Reads only what is already known — registry values, this feature's
    /// config, environment — so it is safe on the UI thread: no network, no directory enumeration, no
    /// blocking. An empty list is the normal answer when nothing is configured (the client isn't
    /// installed), and must never be an error.
    /// </summary>
    IReadOnlyList<ThisPcItem> GetItems();

    /// <summary>
    /// The slow half, run one task per row off the UI thread: whether the row is there, whether it holds folders, and
    /// what only the device can say. Every provider answers for its own rows — there is no default, because only the
    /// provider knows what a row is: a volume, a folder, a namespace it serves.
    /// </summary>
    Task<ThisPcItemDetail> GetDetailAsync(ThisPcItem item, CancellationToken ct = default);

    /// <summary>
    /// Raised when the set <see cref="GetItems"/> would return has changed — the user edited Options, a
    /// sync client appeared. Consumers re-query. May fire on any thread; the file browser marshals.
    /// </summary>
    event Action? Changed;
}
