using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Nexaflow.Features.Dotnet.Services;

/// <summary>
/// Persists the outdated-package result per target so the slow, process-spawning check runs at most once
/// a day per project instead of on every folder visit. Keyed by full target path; backed by a single
/// JSON file under LocalAppData (a regenerable cache, not roaming config). Best-effort — any IO/parse
/// failure behaves as a cache miss.
/// </summary>
public static class NugetCheckCache
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly object _gate = new();

    private static readonly string CacheFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Smile", "nexaflow", "cache", "nuget-outdated.json");

    private sealed record Entry(DateTimeOffset CheckedUtc, List<NugetUpdateChecker.PackageUpdate> Updates);

    /// <summary>Returns the cached updates when a fresh (&lt; 24h) entry exists for <paramref name="targetPath"/>.</summary>
    public static bool TryGet(string targetPath, out IReadOnlyList<NugetUpdateChecker.PackageUpdate> updates)
    {
        lock (_gate)
        {
            if (Load().TryGetValue(Key(targetPath), out var e) && DateTimeOffset.UtcNow - e.CheckedUtc < MaxAge)
            {
                updates = e.Updates;
                return true;
            }
        }
        updates = [];
        return false;
    }

    /// <summary>Records <paramref name="updates"/> as the current result for <paramref name="targetPath"/>,
    /// timestamped now.</summary>
    public static void Store(string targetPath, IReadOnlyList<NugetUpdateChecker.PackageUpdate> updates)
    {
        lock (_gate)
        {
            var map = Load();
            map[Key(targetPath)] = new Entry(DateTimeOffset.UtcNow, updates.ToList());
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
                File.WriteAllText(CacheFile, JsonSerializer.Serialize(map));
            }
            catch { /* best-effort — a write failure just means we re-check next time */ }
        }
    }

    private static string Key(string targetPath) => Path.GetFullPath(targetPath).ToLowerInvariant();

    private static Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(CacheFile)
                && JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(CacheFile)) is { } map)
                return map;
        }
        catch { /* missing or corrupt → start fresh */ }
        return new();
    }
}
