using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Nexaflow.Core.Services;

/// <summary>One catalogued assembly file as the cache saw it: identity plus the two cheap facts that change
/// whenever it is rebuilt.</summary>
public sealed class FeatureFileStamp
{
    public string Name { get; set; } = "";
    public long Length { get; set; }
    public long WriteUtcTicks { get; set; }
}

/// <summary>
/// The validity stamp for <see cref="FeatureCatalog"/>'s on-disk index: the feature-assembly set as it was
/// when the index was built.
/// </summary>
/// <remarks>
/// <para>
/// The stamp used to be the app version alone, on the theory that an app update is the only thing that changes
/// the DLLs in a release install. Two builds can carry the same version, though — a rebuilt Release run, a
/// hotfix, a developer installing over their own build — and the index was then trusted verbatim, so every page
/// kind, file handler and config added since silently ceased to exist. An unregistered page kind renders as an
/// empty tab, which is indistinguishable from a broken feature.
/// </para>
/// <para>
/// Capturing this costs one directory enumeration and <b>no</b> assembly loads, so the lazy-loading guarantee is
/// untouched: <see cref="Directory"/>'s enumerator already carries each entry's length and write time on
/// Windows, so no file is opened and nothing is hashed. It is the same <c>Nexaflow.Features.*.dll</c> sweep the
/// full scan opens with, and an order of magnitude cheaper than deserializing the ~90 KB index it validates.
/// </para>
/// </remarks>
public static class FeatureCatalogStamp
{
    public const string FeatureDllPattern = "Nexaflow.Features.*.dll";

    /// <summary>
    /// The current stamp for <paramref name="exeDir"/>: every feature assembly plus Core, name-ordered so two
    /// captures of the same directory compare equal regardless of enumeration order.
    /// </summary>
    public static List<FeatureFileStamp> Capture(string exeDir, string coreFile)
    {
        var stamps = new List<FeatureFileStamp>();
        try
        {
            var dir = new DirectoryInfo(exeDir);
            foreach (var f in dir.EnumerateFiles(FeatureDllPattern))
                stamps.Add(Of(f));

            // Core carries registrations too, and does not match the feature pattern.
            if (!stamps.Exists(s => string.Equals(s.Name, coreFile, StringComparison.OrdinalIgnoreCase)))
            {
                var core = new FileInfo(Path.Combine(exeDir, coreFile));
                if (core.Exists) stamps.Add(Of(core));
            }
        }
        catch (Exception ex)
        {
            // An unreadable directory yields an empty stamp, which matches nothing — so the catalog rescans
            // rather than trusting an index it cannot verify.
            Debug.WriteLine($"[FeatureCatalog] stamp capture failed: {ex.Message}");
            return [];
        }

        stamps.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return stamps;
    }

    /// <summary>
    /// True when two stamps describe the same assembly set. An empty or absent stamp never matches: an index
    /// written before stamping existed, or captured from a directory that could not be read, must be rebuilt.
    /// </summary>
    public static bool Matches(IReadOnlyList<FeatureFileStamp>? cached, IReadOnlyList<FeatureFileStamp>? current)
    {
        if (cached is null || current is null) return false;
        if (cached.Count == 0 || current.Count == 0) return false;
        if (cached.Count != current.Count) return false;

        for (var i = 0; i < cached.Count; i++)
        {
            var a = cached[i];
            var b = current[i];
            if (a.Length != b.Length ||
                a.WriteUtcTicks != b.WriteUtcTicks ||
                !string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static FeatureFileStamp Of(FileInfo f) => new()
    {
        Name          = f.Name,
        Length        = f.Length,
        WriteUtcTicks = f.LastWriteTimeUtc.Ticks,
    };
}
