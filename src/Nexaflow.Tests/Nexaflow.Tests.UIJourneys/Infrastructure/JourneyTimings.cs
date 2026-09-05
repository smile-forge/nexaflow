using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

/// <summary>
/// Appends one row per timed unit of work to <c>artifacts/journey-timings.csv</c>, so a run can be
/// compared with earlier ones rather than only with an impression of how long it felt.
/// <para>
/// Every row carries the version of the <c>Nexaflow.exe</c> that was driven, which is the identity that
/// makes the file useful across releases: sorting by <c>unit</c> then <c>appVersion</c> shows whether a
/// given piece of work got slower, and <c>msPerItem</c> keeps that honest when the item count changes
/// underneath it (a sample family gains fixtures over time, so its total is not comparable on its own).
/// </para>
/// <para>
/// <b>App launch is never in a row.</b> A caller starts its clock inside the test body, after
/// <c>UITestBase</c> has launched and raised the window, so what is recorded is the work — not the
/// several seconds of process start that vary with a cold disk cache and say nothing about the code.
/// </para>
/// <para>
/// Best-effort by design: the file lives under the gitignored <c>artifacts/</c>, and every failure to
/// write one is swallowed. A timing log that can fail a test would be worse than no timing log.
/// </para>
/// </summary>
internal static class JourneyTimings
{
    private const string Header = "timestampUtc,appVersion,scope,unit,items,elapsedMs,msPerItem,failures";

    private static readonly Lazy<string?> LogPath = new(ResolveLogPath);
    private static readonly Lazy<string> Version = new(ResolveAppVersion);
    private static readonly Lock Gate = new();

    /// <summary>
    /// Records one unit of timed work. <paramref name="scope"/> groups rows (usually the test class),
    /// <paramref name="unit"/> names the thing timed within it, and <paramref name="items"/> is what the
    /// elapsed time was spent on — files opened, controls checked — so the per-item cost is meaningful.
    /// </summary>
    public static void Record(string scope, string unit, int items, TimeSpan elapsed, int failures)
    {
        var path = LogPath.Value;
        if (path is null) return;

        var ms = elapsed.TotalMilliseconds;
        var row = string.Join(',',
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Version.Value,
            scope,
            unit,
            items.ToString(CultureInfo.InvariantCulture),
            ms.ToString("F0", CultureInfo.InvariantCulture),
            (items > 0 ? ms / items : 0).ToString("F0", CultureInfo.InvariantCulture),
            failures.ToString(CultureInfo.InvariantCulture));

        try
        {
            lock (Gate)
            {
                if (!File.Exists(path)) File.WriteAllText(path, Header + Environment.NewLine);
                File.AppendAllText(path, row + Environment.NewLine);
            }
        }
        catch { /* a timing log is never worth failing a test over */ }
    }

    /// <summary>A one-line summary of a row, for <c>TestContext.WriteLine</c> — the same numbers, where
    /// whoever is watching the run will actually see them.</summary>
    public static string Describe(string unit, int items, TimeSpan elapsed)
        => $"{unit}: {items} file(s) in {elapsed.TotalMilliseconds:F0} ms"
         + (items > 0 ? $" ({elapsed.TotalMilliseconds / items:F0} ms/file)" : "");

    private static string? ResolveLogPath()
    {
        try
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (!File.Exists(Path.Combine(dir.FullName, "Nexaflow.slnx"))) continue;
                var artifacts = Path.Combine(dir.FullName, "artifacts");
                Directory.CreateDirectory(artifacts);
                return Path.Combine(artifacts, "journey-timings.csv");
            }
        }
        catch { /* fall through */ }
        return null;
    }

    /// <summary>The FileVersion of the exe the journeys drive — the release a row's numbers belong to.</summary>
    private static string ResolveAppVersion()
    {
        try { return FileVersionInfo.GetVersionInfo(UITestBase.FindAppExe()).FileVersion ?? "unknown"; }
        catch { return "unknown"; }
    }
}
