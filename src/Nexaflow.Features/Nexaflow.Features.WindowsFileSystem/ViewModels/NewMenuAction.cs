using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.ViewModels;

/// <summary>
/// The strip's "New" button, modelled as a folder action because it operates on the current
/// folder (it appears for an empty, no-file-selected selection alongside the other folder actions).
/// Performing it opens the create overlay via the supplied callback rather than mutating the file
/// system, so it reports <see cref="RequiresRefresh"/> = false. Not <c>ICacheable</c>: the
/// view-model constructs it directly (it needs the overlay callback), so the registry never
/// discovers it.
/// </summary>
internal sealed class NewMenuAction(Action openCreateOverlay) : IFolderAction
{
    public bool    IsDestructive         => false;
    public bool    SupportsMultipleFiles => false;
    public IconRef Icon                  => IconRef.Emoji("➕");
    public string  DisplayName           => Str.Get("WindowsFileSystem.Actions.New");
    public bool    RequiresRefresh       => false;
    public bool    CanPerformAction      => true;
    public bool    AppliesToRoot         => true;
    public bool    AppliesToDrives       => false;
    public string? Tooltip               => Str.Get("WindowsFileSystem.Actions.New.Tooltip");
    public bool    IsRibbonPinnable      => false;   // synthetic menu — can't be rehydrated from the ribbon

    public Task<bool> PerformActionAsync(string folderPath, CancellationToken ct = default)              { openCreateOverlay(); return Task.FromResult(false); }
    public Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, CancellationToken ct = default) { openCreateOverlay(); return Task.FromResult(false); }
}
