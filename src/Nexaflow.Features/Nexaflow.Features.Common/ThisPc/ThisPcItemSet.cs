using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Common.ThisPc;

/// <summary>
/// The merge rules for provided "This PC" rows, in one place so every surface that shows them — the
/// browser tab, the folder tree, the file picker, the folder picker — agrees on what the set is.
/// </summary>
public static class ThisPcItemSet
{
    /// <summary>Comparison key for a location: case- and trailing-separator-insensitive.</summary>
    public static string NormalizeRoot(string path)
        => string.IsNullOrEmpty(path) ? string.Empty : path.TrimEnd('\\', '/').ToLowerInvariant();

    /// <summary>
    /// Every provider's rows, ordered by provider then item, minus the ones that should not be shown:
    /// <list type="bullet">
    /// <item><see cref="ThisPcItemBacking.Virtual"/> rows when the caller can only navigate real paths.</item>
    /// <item>Anything already present as a physical drive — a sync client that mounts its own drive
    /// letter would otherwise appear twice, once as itself and once as the provider's row.</item>
    /// <item>Duplicate ids or duplicate locations across providers; the first (lowest-sorted) wins.</item>
    /// </list>
    /// A provider that throws is skipped: one broken feature must not empty This PC.
    /// </summary>
    /// <param name="existingRoots">The physical drive roots the caller is already showing. These differ
    /// per surface — the pickers list only ready drives, the browser lists them all — which is why the
    /// dedupe lives here rather than inside a provider.</param>
    public static IReadOnlyList<ThisPcItem> Collect(
        IEnumerable<IThisPcItemProvider> providers,
        IEnumerable<string> existingRoots,
        bool allowVirtual = true)
    {
        var taken = new HashSet<string>(existingRoots.Select(NormalizeRoot), StringComparer.Ordinal);
        var ids   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ThisPcItem>();

        foreach (var provider in providers.OrderBy(p => p.SortOrder).ThenBy(p => p.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            IReadOnlyList<ThisPcItem> items;
            try { items = provider.GetItems() ?? []; }
            catch { continue; }

            foreach (var item in items.OrderBy(i => i.SortOrder))
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.TargetPath))
                    continue;
                if (!allowVirtual && item.Backing == ThisPcItemBacking.Virtual) continue;
                if (!ids.Add(item.Id)) continue;

                // Only a real location can collide with a drive or another row.
                if (item.Backing == ThisPcItemBacking.LocalPath && !taken.Add(NormalizeRoot(item.TargetPath)))
                    continue;

                result.Add(item);
            }
        }
        return result;
    }

    /// <summary>
    /// The default <see cref="IThisPcItemProvider.GetDetailAsync"/>: does the location exist, and does it
    /// have subfolders worth an expander. Deliberately no size walk — recursing a synced tree to fill a
    /// column nobody sorted by is not worth the IO.
    /// </summary>
    public static Task<ThisPcItemDetail> ProbeLocalAsync(ThisPcItem item, CancellationToken ct = default)
        => Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(item.TargetPath))
                    return new ThisPcItemDetail { Available = false };

                bool hasChildren;
                try { hasChildren = Directory.EnumerateDirectories(item.TargetPath).Any(); }
                catch { hasChildren = false; }   // readable enough to exist, not enough to list

                return new ThisPcItemDetail { Available = true, HasChildren = hasChildren };
            }
            catch (OperationCanceledException) { throw; }
            catch { return new ThisPcItemDetail { Available = false }; }
        }, ct);
}
