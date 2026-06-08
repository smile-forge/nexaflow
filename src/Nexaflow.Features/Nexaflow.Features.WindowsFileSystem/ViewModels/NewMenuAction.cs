using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;

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
    public string  Icon                  => "➕";
    public string  DisplayName           => "New";
    public bool    RequiresRefresh       => false;
    public bool    CanPerformAction      => true;
    public bool    AppliesToRoot         => true;
    public bool    AppliesToDrives       => false;
    public string? Tooltip               => "Create a new file or folder";
    public bool    IsRibbonPinnable      => false;   // synthetic menu — can't be rehydrated from the ribbon

    public bool PerformAction(string folderPath)              { openCreateOverlay(); return false; }
    public bool PerformAction(IEnumerable<string> folderPaths) { openCreateOverlay(); return false; }
}
