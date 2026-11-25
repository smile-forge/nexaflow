using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;
using Nexaflow.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// A dynamic <see cref="IFileAction"/> that executes a Windows shell verb
/// (e.g. "open", "edit", "print") by launching the registered handler command.
/// Instances are created at runtime from HKCR data; they are <em>not</em>
/// auto-discovered by <see cref="FileActionManager"/> (which would need a
/// parameterless constructor).
/// </summary>
public sealed class ShellVerbAction : IFileAction
{
    private readonly ShellPath _paths;
    private readonly string    _verb;
    private readonly string    _command;        // raw registry command template, for reference
    private readonly string    _experienceId;
    private readonly ShellIconRef? _iconResource;
    private readonly string?   _tooltip;

    /// <param name="files">The workspace's file system, which says where Windows finds the file.</param>
    public ShellVerbAction(
        IVirtualFileSystem files,
        string verb,
        string friendlyName,
        string command,
        string experienceId,
        ShellIconRef? iconResource = null,
        string? tooltip            = null)
    {
        _paths        = new ShellPath(files);
        _verb         = verb;
        DisplayName   = friendlyName;
        _command      = command;
        _experienceId = experienceId;
        _iconResource = iconResource;
        _tooltip      = tooltip;
    }

    // ── IFileAction ───────────────────────────────────────────────────────────

    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public IconRef Icon                 => IconRef.Emoji("🔗");
    public string DisplayName           { get; }
    public static string? StaticExperienceId => null;
    public string ExperienceId          => _experienceId;
    public string ExperienceDescription => Str.Format("WindowsFileSystem.Experiences.ShellVerbFormat", _verb);

    public string AutomationId => $"ShellVerb_{_verb}";
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;

    public ShellIconRef? IconResource => _iconResource;
    public string?       Tooltip      => _tooltip;

    /// <summary>A registered verb hands the file to whatever program claims it, which may expect the
    /// file's neighbours to be there too. Offered only where the whole folder really is on disk.</summary>
    public bool RequiresFullyBackedPath => true;

    public async Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            Process.Start(await StartInfoAsync(filePath, ct));
            return true;
        }
        catch { return false; }
    }

    /// <summary>What Windows is asked to run: the verb, on the file by a path Windows can open.</summary>
    internal async ValueTask<ProcessStartInfo> StartInfoAsync(string filePath, CancellationToken ct = default)
        => new(await _paths.RealizeAsync(filePath, ct)) { Verb = _verb, UseShellExecute = true };

    public async Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        bool ok = false;
        foreach (var p in filePaths) ok |= await PerformActionAsync(p, ct);
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

    public static IFileAction Rehydrate(Dictionary<string, string> p, IVirtualFileSystem files) =>
        new ShellVerbAction(
            files,
            verb:         p.GetValueOrDefault("verb",         string.Empty),
            friendlyName: p.GetValueOrDefault("friendlyName", string.Empty),
            command:      p.GetValueOrDefault("command",      string.Empty),
            experienceId: p.GetValueOrDefault("experienceId", string.Empty));
}
