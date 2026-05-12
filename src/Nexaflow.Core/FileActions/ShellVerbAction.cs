using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;

namespace Nexaflow.Features.WinFileSystem.FileActions;

/// <summary>
/// A dynamic <see cref="IFileAction"/> that executes a Windows shell verb
/// (e.g. "open", "edit", "print") by launching the registered handler command.
/// Instances are created at runtime from HKCR data; they are <em>not</em>
/// auto-discovered by <see cref="FileActionRegistry"/> (which would need a
/// parameterless constructor).
/// </summary>
public sealed class ShellVerbAction : IFileAction
{
    private readonly string  _verb;
    private readonly string  _command;   // raw registry command template, for reference
    private readonly string  _contentType;
    private readonly string  _fileTypes;
    private readonly ImageSource? _iconImage;
    private readonly string? _tooltip;

    public ShellVerbAction(
        string verb,
        string friendlyName,
        string command,
        string fileTypes,
        string contentType,
        ImageSource? iconImage,
        string? tooltip = null)
    {
        _verb        = verb;
        DisplayName  = friendlyName;
        _command     = command;
        _fileTypes   = fileTypes;
        _contentType = contentType;
        _iconImage   = iconImage;
        _tooltip     = tooltip;
    }

    // ── IFileAction ───────────────────────────────────────────────────────────

    public bool   IsDestructive        => false;
    public bool   SupportsMultipleFiles => false;
    public string Icon                  => "🔗";
    public string DisplayName           { get; }
    public string SupportedFileTypes    => _fileTypes;
    public bool   AppliesToFolders      => false;
    public string SupportedFolderNames  => string.Empty;
    public bool   AppliesToRoot         => false;
    public bool   AppliesToDrives       => false;
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;

    public string      SupportedContentTypes => _contentType;
    public ImageSource? IconImage            => _iconImage;
    public string?     Tooltip              => _tooltip;

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
}
