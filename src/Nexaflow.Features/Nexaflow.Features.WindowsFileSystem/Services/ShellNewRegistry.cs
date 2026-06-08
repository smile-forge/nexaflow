using Microsoft.Win32;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Singleton that scans <c>HKEY_CLASSES_ROOT</c> for <c>ShellNew</c> entries on a background
/// thread and caches them, gated on the "Use registry-based file type matching" option
/// (mirrors <see cref="ExternalAppRegistry"/> / <c>FileMapManager</c>). Each cached entry
/// becomes a <see cref="ShellNewCreateAction"/> in the new-file picker.
/// </summary>
public sealed class ShellNewRegistry
{
    public static ShellNewRegistry Instance { get; } = new();
    private ShellNewRegistry() { }

    private volatile IReadOnlyList<ShellNewEntry> _entries = Array.Empty<ShellNewEntry>();
    private volatile bool _enabled;

    /// <summary>Called once at startup with the current registry-mapping flag.</summary>
    public void Initialize(bool useRegistryMapping)
    {
        _enabled = useRegistryMapping;
        if (useRegistryMapping) _ = Task.Run(ScanAsync);
    }

    /// <summary>Called when the registry-mapping option is toggled in Options.</summary>
    public void Update(bool useRegistryMapping)
    {
        if (useRegistryMapping == _enabled) return;
        _enabled = useRegistryMapping;
        if (useRegistryMapping) _ = Task.Run(ScanAsync);
        else _entries = Array.Empty<ShellNewEntry>();
    }

    /// <summary>
    /// Fresh <see cref="IFileCreateAction"/>s for every cached ShellNew entry, or empty when
    /// registry mapping is off / the scan has not completed. Safe to call from the UI thread
    /// (icons are frozen during the scan).
    /// </summary>
    public IReadOnlyList<IFileCreateAction> BuildCreateActions()
        => _enabled
            ? _entries.Select(e => (IFileCreateAction)new ShellNewCreateAction(e)).ToList()
            : Array.Empty<IFileCreateAction>();

    private void ScanAsync()
    {
        var list = new List<ShellNewEntry>();
        try
        {
            using var hkcr = Registry.ClassesRoot;
            foreach (var name in hkcr.GetSubKeyNames())
            {
                if (!name.StartsWith('.')) continue;
                try
                {
                    using var extKey = hkcr.OpenSubKey(name);
                    if (extKey is null) continue;

                    var spec = FindShellNewSpec(hkcr, extKey);
                    if (spec is null) continue;

                    var info = ShellTypeResolver.Resolve(name);
                    string display = !string.IsNullOrWhiteSpace(info?.ProgIdDescription)
                        ? info!.ProgIdDescription
                        : name.TrimStart('.').ToUpperInvariant() + " File";
                    var image = string.IsNullOrEmpty(info?.DefaultIconSpec)
                        ? null
                        : ShellIconLoader.Load(info!.DefaultIconSpec!);

                    list.Add(new ShellNewEntry(name.ToLowerInvariant(), display, image, spec));
                }
                catch { }
            }
        }
        catch { }

        _entries = list
            .GroupBy(e => e.Extension, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Locates the ShellNew key for an extension, checking (in order):
    ///   1. <c>.ext\ShellNew</c>                — classic (.txt, etc.)
    ///   2. <c>.ext\&lt;ProgId&gt;\ShellNew</c> — per-ProgID under the extension (Office: Excel, Word…)
    ///   3. <c>HKCR\&lt;DefaultProgId&gt;\ShellNew</c> — the standalone ProgID key
    /// Microsoft Office registers Excel/Word templates under (2), e.g.
    /// <c>.xlsx\Excel.Sheet.12\ShellNew</c>, which is why a single <c>.ext\ShellNew</c>
    /// lookup never found them.
    /// </summary>
    private static ShellNewSpec? FindShellNewSpec(RegistryKey hkcr, RegistryKey extKey)
    {
        // 1. Classic location directly under the extension.
        var spec = ParseShellNewKey(extKey.OpenSubKey("ShellNew"));
        if (spec is not null) return spec;

        // 2. Under any ProgID subkey of the extension (e.g. .xlsx\Excel.Sheet.12\ShellNew).
        foreach (var sub in extKey.GetSubKeyNames())
        {
            if (string.Equals(sub, "ShellNew", StringComparison.OrdinalIgnoreCase)) continue;
            using var subKey = extKey.OpenSubKey(sub);
            spec = ParseShellNewKey(subKey?.OpenSubKey("ShellNew"));
            if (spec is not null) return spec;
        }

        // 3. The extension's default ProgID key at the HKCR root.
        var progId = extKey.GetValue(null) as string;
        if (!string.IsNullOrEmpty(progId))
        {
            using var progKey = hkcr.OpenSubKey(progId);
            spec = ParseShellNewKey(progKey?.OpenSubKey("ShellNew"));
            if (spec is not null) return spec;
        }

        return null;
    }

    /// <summary>
    /// Parses a <c>ShellNew</c> key into a <see cref="ShellNewSpec"/>, preferring Data, then
    /// FileName, then NullFile. Returns null for Command-based entries (skipped) and for keys
    /// with no recognised value. Disposes <paramref name="sn"/>.
    /// </summary>
    private static ShellNewSpec? ParseShellNewKey(RegistryKey? sn)
    {
        if (sn is null) return null;
        using (sn)
        {
            var values = sn.GetValueNames();

            bool Has(string v) => Array.Exists(values, n => string.Equals(n, v, StringComparison.OrdinalIgnoreCase));

            if (Has("Command")) return null; // needs a shell handler — out of scope

            if (Has("Data"))
            {
                var data = sn.GetValue("Data");
                if (data is byte[] bytes && bytes.Length > 0) return new ShellNewSpec(ShellNewKind.Data, Data: bytes);
                if (data is string s && s.Length > 0)         return new ShellNewSpec(ShellNewKind.Data, DataString: s);
            }
            if (Has("FileName"))
            {
                var fn = sn.GetValue("FileName") as string;
                if (!string.IsNullOrWhiteSpace(fn)) return new ShellNewSpec(ShellNewKind.FileName, FileName: fn);
            }
            if (Has("NullFile") || values.Length == 0)
                return new ShellNewSpec(ShellNewKind.NullFile);

            return null;
        }
    }
}
