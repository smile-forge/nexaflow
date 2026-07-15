using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Nexaflow.Features.Dotnet.Services;

/// <summary>
/// Remembers which project the user chose to <c>dotnet run</c> for a given solution. A solution records no
/// startup project of its own, and <see cref="SolutionReader.RunnableProjects"/> can only guess, so without
/// this the guess would be re-imposed on every folder visit (the viewlet is rebuilt each time).
/// <para>
/// Same shape as <see cref="NugetCheckCache"/>: a single JSON map keyed by full solution path, under
/// LocalAppData. Best-effort — any IO/parse failure just falls back to the guess.
/// </para>
/// </summary>
public static class StartupProjectCache
{
    private static readonly object _gate = new();

    private static readonly string CacheFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Smile", "nexaflow", "cache", "dotnet-startup.json");

    /// <summary>The project path last chosen for <paramref name="solutionPath"/>, if one was.</summary>
    public static bool TryGet(string solutionPath, out string projectPath)
    {
        lock (_gate)
        {
            if (Load().TryGetValue(Key(solutionPath), out var stored))
            {
                projectPath = stored;
                return true;
            }
        }
        projectPath = string.Empty;
        return false;
    }

    /// <summary>Records <paramref name="projectPath"/> as the startup project for <paramref name="solutionPath"/>.</summary>
    public static void Store(string solutionPath, string projectPath)
    {
        lock (_gate)
        {
            var map = Load();
            map[Key(solutionPath)] = Path.GetFullPath(projectPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
                File.WriteAllText(CacheFile, JsonSerializer.Serialize(map));
            }
            catch { /* best-effort — a write failure just means the pick isn't remembered */ }
        }
    }

    private static string Key(string solutionPath) => Path.GetFullPath(solutionPath).ToLowerInvariant();

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(CacheFile)
                && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CacheFile)) is { } map)
                return map;
        }
        catch { /* missing or corrupt → start fresh */ }
        return new();
    }
}
