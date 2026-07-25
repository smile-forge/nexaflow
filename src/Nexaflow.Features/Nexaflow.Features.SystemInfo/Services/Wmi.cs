using System.Management;

namespace Nexaflow.Features.SystemInfo.Services;

/// <summary>
/// Thin, fault-tolerant wrapper over WMI/CIM queries. Every helper swallows the (many) ways a query
/// can fail — namespace missing, access denied without elevation, provider not present on this SKU —
/// and returns an empty/default result so a single unavailable class never blanks the whole dashboard.
/// </summary>
internal static class Wmi
{
    /// <summary>Enumerates instances of <paramref name="className"/> in the default <c>root\cimv2</c> scope.</summary>
    public static IEnumerable<ManagementObject> Query(string className)
        => Query(@"root\cimv2", $"SELECT * FROM {className}");

    /// <summary>
    /// Runs an arbitrary WQL query against <paramref name="scope"/>; yields nothing on any failure.
    /// <para>
    /// WMI evaluates <b>lazily</b> — <c>searcher.Get()</c> only describes the query, and "invalid class",
    /// "access denied" and "provider not present" all surface from the first <c>MoveNext</c>. So the
    /// enumeration is stepped by hand with its own guard; a plain <c>foreach</c> inside a try/catch around
    /// <c>Get()</c> would let those throw straight through to the caller and blank the dashboard.
    /// </para>
    /// </summary>
    public static IEnumerable<ManagementObject> Query(string scope, string wql)
    {
        ManagementObjectSearcher searcher;
        ManagementObjectCollection results;
        try
        {
            searcher = new ManagementObjectSearcher(scope, wql);
            results  = searcher.Get();
        }
        catch
        {
            yield break;
        }

        using (searcher)
        using (results)
        {
            var e = results.GetEnumerator();
            using (e as IDisposable)
            {
                while (true)
                {
                    ManagementBaseObject? current;
                    try
                    {
                        if (!e.MoveNext()) yield break;   // the query really runs here
                        current = e.Current;
                    }
                    catch
                    {
                        yield break;
                    }
                    if (current is ManagementObject mo) yield return mo;
                }
            }
        }
    }

    /// <summary>The first instance of <paramref name="className"/>, or null.</summary>
    public static ManagementObject? First(string className) => Query(className).FirstOrDefault();

    public static string? Str(this ManagementObject mo, string prop)
    {
        try { return mo[prop]?.ToString()?.Trim() is { Length: > 0 } s ? s : null; }
        catch { return null; }
    }

    public static T? Val<T>(this ManagementObject mo, string prop) where T : struct
    {
        try { return mo[prop] is { } v ? (T)Convert.ChangeType(v, typeof(T)) : null; }
        catch { return null; }
    }

    /// <summary>Reads a <c>uint[]</c>-style property as a set of ints (Device Guard's security-property arrays).</summary>
    public static HashSet<int> IntSet(this ManagementObject mo, string prop)
    {
        try
        {
            if (mo[prop] is System.Collections.IEnumerable seq and not string)
                return seq.Cast<object>().Select(Convert.ToInt32).ToHashSet();
        }
        catch { /* absent or wrong shape */ }
        return [];
    }

    /// <summary>Parses a WMI <c>CIM_DATETIME</c> (<c>yyyymmddHHMMSS.ffffff±UUU</c>) into local time.</summary>
    public static DateTime? CimDate(this ManagementObject mo, string prop)
    {
        var raw = mo.Str(prop);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return ManagementDateTimeConverter.ToDateTime(raw); }
        catch { return null; }
    }
}
