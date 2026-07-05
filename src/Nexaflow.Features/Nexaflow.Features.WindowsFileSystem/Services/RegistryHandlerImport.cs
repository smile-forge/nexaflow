using Microsoft.Win32;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Enumerates the Windows "open" handlers registered in HKCR and projects them into
/// <see cref="ExternalAppDefinition"/>s, deduplicated by executable. Offered when the user turns off the
/// registry-handlers toggle so the live shell-verb buttons can be kept as persistent External Apps.
/// Pure registry reads (via <see cref="ShellTypeResolver"/>) — safe to run on a background thread.
/// </summary>
internal static class RegistryHandlerImport
{
    /// <summary>
    /// Scans every HKCR <c>.ext</c> with an "open" verb, resolves the handler executable, and returns one
    /// definition per unique exe scoped (by <see cref="CriteriaType.Extension"/>) to all extensions it
    /// opens. rundll32/dllhost and non-<c>.exe</c> handlers are skipped (the launcher starts a process by
    /// path). Deterministic apart from the assigned ids.
    /// </summary>
    public static IReadOnlyList<ExternalAppDefinition> EnumerateOpenHandlers()
    {
        // exe path → (display name, set of extensions it opens)
        var byExe = new Dictionary<string, (string Name, SortedSet<string> Exts)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var hkcr = Registry.ClassesRoot;
            foreach (var name in hkcr.GetSubKeyNames())
            {
                if (!name.StartsWith('.')) continue;

                var info = ShellTypeResolver.Resolve(name);
                var open = info?.Verbs.FirstOrDefault(v =>
                    string.Equals(v.Verb, "open", StringComparison.OrdinalIgnoreCase));
                if (open is null || string.IsNullOrWhiteSpace(open.Command)) continue;

                var exe = ExtractExecutable(open.Command);
                if (exe is null) continue;

                if (!byExe.TryGetValue(exe, out var entry))
                {
                    entry = (FriendlyName(exe), new SortedSet<string>(StringComparer.OrdinalIgnoreCase));
                    byExe[exe] = entry;
                }
                entry.Exts.Add(name.ToLowerInvariant());   // shared set reference — mutation sticks
            }
        }
        catch { }

        return byExe
            .Select(kv => new ExternalAppDefinition
            {
                Id              = Guid.NewGuid().ToString("N"),
                DisplayName     = kv.Value.Name,
                ApplicationPath = kv.Key,
                Arguments       = "#filepath",
                MultiFile       = MultiFileMode.SingleFileOnly,
                Criteria        = kv.Value.Exts
                    .Select(e => new FileSelectionCriteria { Type = CriteriaType.Extension, Value = "*" + e })
                    .ToList(),
            })
            .OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Extracts the executable path from a shell verb command template
    /// (e.g. <c>"C:\App\app.exe" "%1"</c> → <c>C:\App\app.exe</c>). Returns null for non-<c>.exe</c>
    /// handlers and the generic rundll32/dllhost shims, which can't be launched by path.
    /// </summary>
    internal static string? ExtractExecutable(string command)
    {
        var cmd = command.Trim();
        if (cmd.Length == 0) return null;

        string exe;
        if (cmd[0] == '"')
        {
            int end = cmd.IndexOf('"', 1);
            if (end < 0) return null;
            exe = cmd[1..end];
        }
        else
        {
            int sp = cmd.IndexOf(' ');
            exe = sp < 0 ? cmd : cmd[..sp];
        }

        exe = Environment.ExpandEnvironmentVariables(exe).Trim();
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;

        var fileName = Path.GetFileName(exe);
        if (fileName.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("dllhost.exe",  StringComparison.OrdinalIgnoreCase))
            return null;

        return exe;
    }

    /// <summary>Best-effort human name for an exe: file description → product name → file stem.</summary>
    private static string FriendlyName(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var name = info.FileDescription;
            if (string.IsNullOrWhiteSpace(name)) name = info.ProductName;
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        }
        catch { }
        return Path.GetFileNameWithoutExtension(exePath);
    }
}
