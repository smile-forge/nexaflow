using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Media;

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
    private readonly ImageSource?          _iconImage;

    public CustomAction(ExternalAppDefinition def, ImageSource? iconImage = null)
    {
        _def       = def;
        _iconImage = iconImage;
    }

    /// <summary>The backing definition (same instance held in <see cref="ExternalAppsConfig.Apps"/>)
    /// — lets the "Modify" command deep-link the Options editor to this app.</summary>
    internal ExternalAppDefinition Definition => _def;

    // ── IFileAction ───────────────────────────────────────────────────────────

    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => _def.MultiFile != MultiFileMode.SingleFileOnly;
    public string Icon                  => "🛠";
    public string DisplayName           => _def.DisplayName;
    public static string? StaticExperienceId => null;
    public string ExperienceId          => $"/customapp/{NormalizeExt(_def.Extension)}";
    public string ExperienceDescription => $"External app: {_def.DisplayName}";
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;

    public ImageSource? IconImage => _iconImage;
    public string?      Tooltip   => string.IsNullOrEmpty(_def.ApplicationPath) ? null : _def.ApplicationPath;

    public bool PerformAction(string filePath) => Launch(new[] { filePath });

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var paths = filePaths.ToList();
        if (paths.Count == 0) return false;
        if (paths.Count == 1) return Launch(paths);

        if (_def.MultiFile == MultiFileMode.OneLaunchPerFile)
        {
            bool ok = false;
            foreach (var p in paths) ok |= Launch(new[] { p });
            return ok;
        }

        // SingleLaunchAllFiles (or SingleFileOnly slipped through — treat as single launch
        // with full list rather than silently dropping the action).
        return Launch(paths);
    }

    private bool Launch(IReadOnlyList<string> paths)
    {
        if (string.IsNullOrWhiteSpace(_def.ApplicationPath)) return false;
        try
        {
            var args = ActionTemplateExpander.Expand(_def.Arguments,        paths);
            var wd   = ActionTemplateExpander.Expand(_def.WorkingDirectory, paths);
            // Unspecified working directory → the launched app runs in the file's own folder,
            // not Nexaflow's binary directory (the process default).
            if (string.IsNullOrWhiteSpace(wd) && paths.Count > 0)
                wd = System.IO.Path.GetDirectoryName(paths[0]) ?? string.Empty;
            var psi  = new ProcessStartInfo
            {
                FileName         = Environment.ExpandEnvironmentVariables(_def.ApplicationPath),
                Arguments        = args,
                WorkingDirectory = wd,
                UseShellExecute  = true,
            };
            Process.Start(psi);
            return true;
        }
        catch { return false; }
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

    public static IFileAction Rehydrate(Dictionary<string, string> p)
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

        return new CustomAction(def, Services.ExternalAppRegistry.ResolveIcon(def));
    }
}
