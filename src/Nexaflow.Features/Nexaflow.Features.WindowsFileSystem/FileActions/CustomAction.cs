using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// A dynamic <see cref="IFileAction"/> built at runtime from a user-defined
/// <see cref="ExternalAppDefinition"/>. Like <see cref="ShellVerbAction"/> it is
/// <em>not</em> auto-discovered by <see cref="FileActionManager"/> — instances
/// are produced by <see cref="Services.ExternalAppRegistry"/> per selection.
/// </summary>
public sealed class CustomAction : IFileAction
{
    private readonly ExternalAppDefinition _def;
    private readonly ShellPath             _paths;
    private readonly ShellIconRef?         _iconResource;

    /// <param name="files">The workspace's file system, which says where the program finds the files.</param>
    public CustomAction(ExternalAppDefinition def, IVirtualFileSystem files, ShellIconRef? iconResource = null)
    {
        _def          = def;
        _paths        = new ShellPath(files);
        _iconResource = iconResource;
    }

    /// <summary>The backing definition (same instance held in <see cref="ExternalAppsConfig.Apps"/>)
    /// — lets the "Modify" command deep-link the Options editor to this app.</summary>
    internal ExternalAppDefinition Definition => _def;

    // ── IFileAction ───────────────────────────────────────────────────────────

    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => _def.MultiFile != MultiFileMode.SingleFileOnly;
    public IconRef Icon                 => IconRef.Emoji("🛠");
    public string DisplayName           => _def.DisplayName;

    public string AutomationId => $"ExternalApp_{_def.Id}";
    public static string? StaticExperienceId => null;
    public string ExperienceId          => $"/customapp/{NormalizeExt(_def.Extension)}";
    public string ExperienceDescription => Str.Format("WindowsFileSystem.Experiences.ExternalAppFormat", _def.DisplayName);
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;

    public ShellIconRef? IconResource => _iconResource;
    public string?       Tooltip      => string.IsNullOrEmpty(_def.ApplicationPath) ? null : _def.ApplicationPath;

    /// <summary>A user-configured program is opaque to us — it may open sibling files, write next to the
    /// input, or use the working directory. Only offered where the folder genuinely exists on disk.</summary>
    public bool RequiresFullyBackedPath => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default) => LaunchAsync([filePath], ct);

    public async Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        var paths = filePaths.ToList();
        if (paths.Count == 0) return false;
        if (paths.Count == 1) return await LaunchAsync(paths, ct);

        if (_def.MultiFile == MultiFileMode.OneLaunchPerFile)
        {
            bool ok = false;
            foreach (var p in paths) ok |= await LaunchAsync([p], ct);
            return ok;
        }

        // SingleLaunchAllFiles (or SingleFileOnly slipped through — treat as single launch
        // with full list rather than silently dropping the action).
        return await LaunchAsync(paths, ct);
    }

    private async Task<bool> LaunchAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        try
        {
            if (await StartInfoAsync(paths, ct) is not { } start) return false;
            Process.Start(start);
            return true;
        }
        catch { return false; }
    }

    /// <summary>What Windows is asked to run: the program, its arguments with the files' paths, and the folder it runs in;
    /// null when no program is named. The program receives plain paths and may look around them, so it is handed ones
    /// Windows opens — only reachable where the folder is fully backed (<see cref="RequiresFullyBackedPath"/>).</summary>
    internal async ValueTask<ProcessStartInfo?> StartInfoAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_def.ApplicationPath)) return null;
        IReadOnlyList<string> real = await _paths.RealizeAsync(paths, ct);

        var args = ActionTemplateExpander.Expand(_def.Arguments,        real);
        var wd   = ActionTemplateExpander.Expand(_def.WorkingDirectory, real);
        // Unspecified working directory → the launched app runs in the file's own folder,
        // not Nexaflow's binary directory (the process default).
        if (string.IsNullOrWhiteSpace(wd) && real.Count > 0)
            wd = System.IO.Path.GetDirectoryName(real[0]) ?? string.Empty;
        return new ProcessStartInfo
        {
            FileName         = Environment.ExpandEnvironmentVariables(_def.ApplicationPath),
            Arguments        = args,
            WorkingDirectory = wd,
            UseShellExecute  = true,
        };
    }

    private static string NormalizeExt(string ext)
    {
        if (string.IsNullOrEmpty(ext) || ext == "*") return "any";
        return ext.TrimStart('.').ToLowerInvariant();
    }

    // ── Ribbon pin rehydration ───────────────────────────────────────────────

    public Dictionary<string, string>? GetReinitParams() => new()
    {
        ["extension"]       = _def.Extension,
        ["displayName"]     = _def.DisplayName,
        ["applicationPath"] = _def.ApplicationPath,
        ["arguments"]       = _def.Arguments,
        ["workingDir"]      = _def.WorkingDirectory,
        ["iconPath"]        = _def.IconPath,
        ["multiFile"]       = _def.MultiFile.ToString(),
    };

    public static IFileAction Rehydrate(Dictionary<string, string> p, IVirtualFileSystem files)
    {
        var def = new ExternalAppDefinition
        {
            Extension        = p.GetValueOrDefault("extension",       string.Empty),
            DisplayName      = p.GetValueOrDefault("displayName",     string.Empty),
            ApplicationPath  = p.GetValueOrDefault("applicationPath", string.Empty),
            Arguments        = p.GetValueOrDefault("arguments",       string.Empty),
            WorkingDirectory = p.GetValueOrDefault("workingDir",      string.Empty),
            IconPath         = p.GetValueOrDefault("iconPath",        string.Empty),
            MultiFile        = Enum.TryParse<MultiFileMode>(
                                   p.GetValueOrDefault("multiFile", nameof(MultiFileMode.SingleFileOnly)),
                                   out var mf) ? mf : MultiFileMode.SingleFileOnly,
        };

        return new CustomAction(def, files, ExternalAppRegistry.ResolveIcon(def));
    }
}
