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

    // ── The whole list ───────────────────────────────────────────────────────

    /// <summary>
    /// Every top-level place: the physical drives, then the contributed locations, deduped against them.
    /// This is the one enumeration of "This PC" — the file browser and both pickers build their own row
    /// types from it rather than each running their own <c>DriveInfo.GetDrives()</c> loop with their own
    /// labelling, which is how they previously drifted into disagreeing about names and icons.
    /// </summary>
    /// <param name="readyDrivesOnly">Skip drives that aren't ready. The pickers do — you cannot browse
    /// an empty optical bay — while the browser lists them so it can show the unavailable badge. It also
    /// decides how much of the label is safe to read: see <see cref="DriveLabel"/>.</param>
    /// <param name="allowVirtual">Whether locations a provider serves itself may appear. False for the
    /// pickers, whose result is consumed by ordinary <see cref="System.IO"/> callers.</param>
    public static IReadOnlyList<ThisPcPlace> Enumerate(
        IEnumerable<IThisPcItemProvider> providers,
        bool readyDrivesOnly = false,
        bool allowVirtual = true)
    {
        var places     = new List<ThisPcPlace>();
        var driveRoots = new List<string>();

        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { drives = []; }

        foreach (var drive in drives)
        {
            bool ready;
            try { ready = drive.IsReady; } catch { ready = false; }
            if (readyDrivesOnly && !ready) continue;

            string root;
            try { root = drive.RootDirectory.FullName; } catch { continue; }

            places.Add(new ThisPcPlace
            {
                Kind      = ThisPcPlaceKind.Drive,
                RealPath  = root,
                Label     = DriveLabel(drive, ready),
                TypeLabel = DriveTypeLabel(drive),
                Icon      = IconFor(drive),
                Drive     = drive,
            });
            driveRoots.Add(root);
        }

        foreach (var item in Collect(providers, driveRoots, allowVirtual))
            places.Add(new ThisPcPlace
            {
                Kind      = ThisPcPlaceKind.Provided,
                RealPath  = item.TargetPath,
                Label     = item.Label,
                TypeLabel = item.TypeLabel,
                Icon      = item.Icon,
                Item      = item,
            });

        return places;
    }

    /// <summary>
    /// A drive's display name: <c>"Volume (C:)"</c>, or bare <c>"C:\"</c> when the volume label can't be
    /// had. Reading <see cref="DriveInfo.VolumeLabel"/> on a drive that isn't ready can block on the
    /// hardware, so a caller listing every drive passes <paramref name="ready"/> false and re-labels once
    /// its background probe has confirmed readiness.
    /// </summary>
    public static string DriveLabel(DriveInfo drive, bool ready)
    {
        if (!ready) return drive.Name;
        try
        {
            return string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
        }
        catch { return drive.Name; }
    }

    /// <summary>The "Type" column text for a drive.</summary>
    public static string DriveTypeLabel(DriveInfo drive)
    {
        try
        {
            return drive.DriveType switch
            {
                DriveType.CDRom     => "CD/DVD Drive",
                DriveType.Removable => "Removable Disk",
                DriveType.Network   => "Network Drive",
                DriveType.Ram       => "RAM Disk",
                _                   => "Local Disk",
            };
        }
        catch { return "Local Disk"; }
    }

    /// <summary>A drive's icon kind. <see cref="ThisPcItemIcon.Disk"/> covers fixed volumes; telling an
    /// SSD from a spinning disk needs a device query, so the browser refines that separately.</summary>
    public static ThisPcItemIcon IconFor(DriveInfo drive)
    {
        try
        {
            return drive.DriveType switch
            {
                DriveType.CDRom     => ThisPcItemIcon.Optical,
                DriveType.Removable => ThisPcItemIcon.Removable,
                DriveType.Network   => ThisPcItemIcon.Network,
                _                   => ThisPcItemIcon.Disk,
            };
        }
        catch { return ThisPcItemIcon.Disk; }
    }

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
