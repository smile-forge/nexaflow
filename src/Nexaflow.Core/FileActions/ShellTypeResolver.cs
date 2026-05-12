using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Nexaflow.Features.WinFileSystem.FileActions;

/// <summary>
/// Resolves shell verb handlers and metadata for a given file extension by
/// walking HKEY_CLASSES_ROOT exactly as Windows Explorer does.
/// </summary>
internal static class ShellTypeResolver
{
    // ── Public types ──────────────────────────────────────────────────────────

    public sealed record VerbInfo(
        /// <summary>Raw verb key name used with UseShellExecute (e.g. "open", "edit").</summary>
        string Verb,
        /// <summary>Short button label — capitalised verb key name (e.g. "Open", "Edit").</summary>
        string FriendlyName,
        /// <summary>
        /// Longer description resolved from MUIVerb / the verb key's default value.
        /// Used as the button tooltip. Null when no description is available.
        /// </summary>
        string? Tooltip,
        string Command,
        string? DefaultIconSpec);

    public sealed record ExtensionInfo(
        string ProgId,
        /// <summary>Human-readable type name from the default value of the ProgID key.</summary>
        string ProgIdDescription,
        string ContentType,
        string? DefaultIconSpec,
        IReadOnlyList<VerbInfo> Verbs);

    // ── Cache ────────────────────────────────────────────────────────────────

    private static readonly ConcurrentDictionary<string, ExtensionInfo?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns shell information for <paramref name="extension"/> (e.g. ".txt"),
    /// or <c>null</c> if the extension is not registered.
    /// Results are cached. Safe to call from a background thread.
    /// </summary>
    public static ExtensionInfo? Resolve(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        if (!extension.StartsWith('.')) extension = "." + extension;
        return _cache.GetOrAdd(extension, ResolveCore);
    }

    // ── Implementation ───────────────────────────────────────────────────────

    private static ExtensionInfo? ResolveCore(string ext)
    {
        try
        {
            using var extKey = Registry.ClassesRoot.OpenSubKey(ext);
            if (extKey is null) return null;

            string contentType = (extKey.GetValue("Content Type") as string) ?? string.Empty;

            // ── Step 1: resolve the primary (default) ProgID ──────────────────
            string? defaultProgId = extKey.GetValue(null) as string;
            bool    hasDefaultHandler = false;

            if (!string.IsNullOrEmpty(defaultProgId))
            {
                using var probe = Registry.ClassesRoot.OpenSubKey(defaultProgId);
                hasDefaultHandler = probe is not null;
                if (!hasDefaultHandler) defaultProgId = null;
            }

            // ── Step 2: collect valid OpenWithProgIds ─────────────────────────
            var openWithProgIds = CollectOpenWithProgIds(extKey, defaultProgId);

            if (!hasDefaultHandler && openWithProgIds.Count == 0) return null;

            // ── Step 3: primary ProgID drives type-level metadata ─────────────
            string primaryProgId = defaultProgId ?? openWithProgIds[0];
            using var primaryKey = Registry.ClassesRoot.OpenSubKey(primaryProgId);
            if (primaryKey is null) return null;

            string progIdDescription = StripAccelerators(
                (primaryKey.GetValue(null) as string) ?? string.Empty);

            string? defaultIconSpec = null;
            using (var iconKey = primaryKey.OpenSubKey("DefaultIcon"))
                defaultIconSpec = iconKey?.GetValue(null) as string;

            // ── Step 4: decide which verbs to collect ─────────────────────────
            //
            // Rule A — default handler exists: show ALL its verbs.
            //           OpenWithProgIds entries are ignored for verb listing.
            //
            // Rule B — no default handler, exactly one valid OpenWithProgId:
            //           show ALL that ProgID's verbs.
            //
            // Rule C — no default handler, multiple valid OpenWithProgIds:
            //           show only the declared shell-default verb for each ProgID
            //           (first verb if none is declared). No deduplication.

            var verbs = new List<VerbInfo>();

            if (hasDefaultHandler)
            {
                // Rule A
                ReadVerbs(primaryKey, defaultIconSpec, onlyDefault: false, verbs);
            }
            else if (openWithProgIds.Count == 1)
            {
                // Rule B
                using var pk = Registry.ClassesRoot.OpenSubKey(openWithProgIds[0]);
                if (pk is not null)
                    ReadVerbs(pk, GetDefaultIcon(pk) ?? defaultIconSpec, onlyDefault: false, verbs);
            }
            else
            {
                // Rule C — one best verb per ProgID, no dedup across ProgIDs
                foreach (var progId in openWithProgIds)
                {
                    using var pk = Registry.ClassesRoot.OpenSubKey(progId);
                    if (pk is null) continue;
                    ReadVerbs(pk, GetDefaultIcon(pk) ?? defaultIconSpec, onlyDefault: true, verbs);
                }
            }

            return new ExtensionInfo(primaryProgId, progIdDescription, contentType, defaultIconSpec, verbs);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns all valid ProgIDs from OpenWithProgids (key exists, has shell verbs),
    /// excluding <paramref name="skipProgId"/> if already handled as the default.
    /// </summary>
    private static IReadOnlyList<string> CollectOpenWithProgIds(RegistryKey extKey, string? skipProgId)
    {
        var result = new List<string>();
        try
        {
            using var owpKey = extKey.OpenSubKey("OpenWithProgids");
            if (owpKey is null) return result;

            foreach (var name in owpKey.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (string.Equals(name, skipProgId, StringComparison.OrdinalIgnoreCase)) continue;

                using var pk = Registry.ClassesRoot.OpenSubKey(name);
                if (pk is null) continue;

                using var sk = pk.OpenSubKey("shell");
                if (sk is not null && sk.GetSubKeyNames().Length > 0)
                    result.Add(name);
            }
        }
        catch { }
        return result;
    }

    private static string? GetDefaultIcon(RegistryKey progKey)
    {
        using var ik = progKey.OpenSubKey("DefaultIcon");
        return ik?.GetValue(null) as string;
    }

    /// <summary>
    /// Reads verbs from <c>progKey\shell</c> and appends them to <paramref name="result"/>.
    /// When <paramref name="onlyDefault"/> is <c>true</c>, only the shell-default verb is
    /// emitted (or the first valid verb if no default is declared).
    /// </summary>
    private static void ReadVerbs(
        RegistryKey  progKey,
        string?      defaultIconSpec,
        bool         onlyDefault,
        List<VerbInfo> result)
    {
        try
        {
            using var shellKey = progKey.OpenSubKey("shell");
            if (shellKey is null) return;

            var defaultVerb = shellKey.GetValue(null) as string;
            var verbNames   = shellKey.GetSubKeyNames();
            if (verbNames.Length == 0) return;

            // Order: declared default verb first, then the rest alphabetically as Windows returns them
            var ordered = new List<string>();
            if (!string.IsNullOrEmpty(defaultVerb) &&
                Array.Exists(verbNames, v => string.Equals(v, defaultVerb, StringComparison.OrdinalIgnoreCase)))
            {
                ordered.Add(defaultVerb);
            }
            foreach (var v in verbNames)
                if (!ordered.Contains(v, StringComparer.OrdinalIgnoreCase))
                    ordered.Add(v);

            foreach (var verbName in ordered)
            {
                using var verbKey = shellKey.OpenSubKey(verbName);
                if (verbKey is null) continue;

                // Determine whether a usable handler exists
                string? command = null;
                using (var cmdKey = verbKey.OpenSubKey("command"))
                    command = cmdKey?.GetValue(null) as string;

                bool hasDelegateExecute =
                    verbKey.GetValue("DelegateExecute") is not null ||
                    verbKey.OpenSubKey("command")?.GetValue("DelegateExecute") is not null;
                bool hasExplorerCommand =
                    verbKey.GetValue("ExplorerCommandHandler") is not null;

                if (command is null && !hasDelegateExecute && !hasExplorerCommand)
                    continue;

                // ── Button label: always the capitalised verb key name ─────────
                string friendlyName = Capitalize(verbName);

                // ── Tooltip: resolved long description ────────────────────────
                // Priority: "MUIVerb" named value → default value of verb key.
                // Both may be an @-resource reference resolved via SHLoadIndirectString.
                string? tooltip = null;

                if (verbKey.GetValue("MUIVerb") is string muiNamed && !string.IsNullOrEmpty(muiNamed))
                    tooltip = muiNamed.TrimStart().StartsWith('@')
                        ? LoadIndirectString(muiNamed)
                        : muiNamed;

                if (tooltip is null &&
                    verbKey.GetValue(null) is string defVal && !string.IsNullOrEmpty(defVal))
                {
                    tooltip = defVal.TrimStart().StartsWith('@')
                        ? LoadIndirectString(defVal)   // null if unresolvable — don't show raw @-string
                        : defVal;
                }

                if (tooltip is not null)
                {
                    tooltip = StripAccelerators(tooltip);
                    // Don't surface a tooltip identical to the label
                    if (string.Equals(tooltip, friendlyName, StringComparison.OrdinalIgnoreCase))
                        tooltip = null;
                }

                // Per-verb icon (falls back to the ProgID-level DefaultIcon)
                string? verbIconSpec = null;
                using (var viKey = verbKey.OpenSubKey("Icon"))
                    verbIconSpec = viKey?.GetValue(null) as string;
                if (string.IsNullOrEmpty(verbIconSpec)) verbIconSpec = defaultIconSpec;

                result.Add(new VerbInfo(verbName, friendlyName, tooltip, command ?? string.Empty, verbIconSpec));

                if (onlyDefault) return;  // Rule C: one verb per ProgID
            }
        }
        catch { }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Capitalize(string s)
        => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string StripAccelerators(string s)
    {
        if (!s.Contains('&')) return s;
        var sb = new StringBuilder(s.Length);
        int i  = 0;
        while (i < s.Length)
        {
            if (s[i] == '&' && i + 1 < s.Length)
            {
                if (s[i + 1] == '&') { sb.Append('&'); i += 2; }
                else                 { sb.Append(s[i + 1]); i += 2; }
            }
            else { sb.Append(s[i]); i++; }
        }
        return sb.ToString();
    }

    // ── SHLoadIndirectString ─────────────────────────────────────────────────

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(
        string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

    private static string? LoadIndirectString(string muiRef)
    {
        try
        {
            var sb = new StringBuilder(512);
            int hr = SHLoadIndirectString(muiRef, sb, sb.Capacity, IntPtr.Zero);
            if (hr == 0 && sb.Length > 0) return sb.ToString();
        }
        catch { }
        return null;
    }
}
