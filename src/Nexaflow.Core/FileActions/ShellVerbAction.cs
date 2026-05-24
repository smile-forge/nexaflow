using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;

namespace Nexaflow.Core.FileActions;

/// <summary>
/// A dynamic <see cref="IFileAction"/> that executes a Windows shell verb
/// (e.g. "open", "edit", "print") by launching the registered handler command.
/// Instances are created at runtime from HKCR data; they are <em>not</em>
/// auto-discovered by <see cref="FileActionManager"/> (which would need a
/// parameterless constructor).
/// </summary>
public sealed class ShellVerbAction : IFileAction
{
    private readonly string    _verb;
    private readonly string    _command;        // raw registry command template, for reference
    private readonly string    _experienceId;
    private readonly ImageSource? _iconImage;
    private readonly string?   _tooltip;

    public ShellVerbAction(
        string verb,
        string friendlyName,
        string command,
        string experienceId,
        ImageSource? iconImage  = null,
        string? tooltip         = null)
    {
        _verb         = verb;
        DisplayName   = friendlyName;
        _command      = command;
        _experienceId = experienceId;
        _iconImage    = iconImage;
        _tooltip      = tooltip;
    }

    // ── IFileAction ───────────────────────────────────────────────────────────

    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public string Icon                  => "🔗";
    public string DisplayName           { get; }
    public string ExperienceId          => _experienceId;
    public string ExperienceDescription => $"Shell verb: {_verb}";
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;

    public ImageSource? IconImage => _iconImage;
    public string?      Tooltip   => _tooltip;

    public bool PerformAction(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath)
            {
                Verb            = _verb,
                UseShellExecute = true
            });
            return true;
        }
        catch { return false; }
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        bool ok = false;
        foreach (var p in filePaths) ok |= PerformAction(p);
        return ok;
    }

    // ── Ribbon pin rehydration ───────────────────────────────────────────────
    // ShellVerbAction is constructed at runtime per registered shell verb, so
    // it's not in FeatureManager's singleton set. To survive a ribbon pin we
    // serialise the essential state here and provide a static factory to
    // recreate the instance at click time. Icon/tooltip are not persisted —
    // the rehydrated button works correctly, just without its custom icon.

    public Dictionary<string, string>? GetReinitParams() => new()
    {
        ["verb"]         = _verb,
        ["friendlyName"] = DisplayName,
        ["command"]      = _command,
        ["experienceId"] = _experienceId,
    };

    public static IFileAction Rehydrate(Dictionary<string, string> p) =>
        new ShellVerbAction(
            verb:         p.GetValueOrDefault("verb",         string.Empty),
            friendlyName: p.GetValueOrDefault("friendlyName", string.Empty),
            command:      p.GetValueOrDefault("command",      string.Empty),
            experienceId: p.GetValueOrDefault("experienceId", string.Empty));
}
