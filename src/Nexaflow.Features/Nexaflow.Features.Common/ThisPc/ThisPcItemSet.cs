using System;
using System.Collections.Generic;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Common.ThisPc;

/// <summary>
/// The merge rules for This PC, in one place so every surface that shows it — the browser tab, the folder tree, the file
/// picker, the folder picker — agrees on what the set is. Nothing is listed by default: This PC is exactly what its
/// providers offer, the volumes included.
/// </summary>
public static class ThisPcItemSet
{
    /// <summary>Comparison key for a location: case- and trailing-separator-insensitive.</summary>
    public static string NormalizeRoot(string path)
        => string.IsNullOrEmpty(path) ? string.Empty : path.TrimEnd('\\', '/').ToLowerInvariant();

    /// <summary>
    /// Every provider's rows, ordered by provider then item, each with the provider that describes it, minus the ones that
    /// should not be shown:
    /// <list type="bullet">
    /// <item><see cref="ThisPcItemBacking.Virtual"/> rows when the caller can only navigate real paths.</item>
    /// <item>A second row at a location already listed — a sync client that mounts its own drive letter is a volume and
    /// a provider's row at once. The volumes sort first, so the volume keeps the place.</item>
    /// <item>Duplicate ids across providers; the first (lowest-sorted) wins.</item>
    /// </list>
    /// A provider that throws is skipped: one broken feature must not empty This PC.
    /// </summary>
    /// <param name="providers">The contributors to ask. Each is queried once; one that throws is skipped.</param>
    /// <param name="allowVirtual">Whether locations a provider serves itself may appear. False for a
    /// caller that can only navigate real paths.</param>
    public static IReadOnlyList<ThisPcRow> Collect(IEnumerable<IThisPcItemProvider> providers, bool allowVirtual = true)
    {
        var taken  = new HashSet<string>(StringComparer.Ordinal);
        var ids    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ThisPcRow>();

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

                // Only a real location can collide with another row.
                if (item.Backing != ThisPcItemBacking.Virtual && !taken.Add(NormalizeRoot(item.TargetPath)))
                    continue;

                result.Add(new ThisPcRow(provider, item));
            }
        }
        return result;
    }
}

/// <summary>A row of This PC and the provider that offered it, which is who describes it.</summary>
public sealed record ThisPcRow(IThisPcItemProvider Provider, ThisPcItem Item)
{
    /// <summary>The slow half of the row, from its provider (<see cref="IThisPcItemProvider.GetDetailAsync"/>).</summary>
    public Task<ThisPcItemDetail> DetailAsync(CancellationToken ct = default) => Provider.GetDetailAsync(Item, ct);
}
