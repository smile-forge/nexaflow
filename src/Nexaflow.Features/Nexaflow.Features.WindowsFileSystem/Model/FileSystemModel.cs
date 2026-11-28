using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Operations;
using Nexaflow.Features.WindowsFileSystem.Services;

using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;

namespace Nexaflow.Features.WindowsFileSystem.Model;

/// <summary>
/// One file-system tab, named in no UI toolkit: where it is and what it lists, moving about, making items, the actions
/// a selection offers and what the assistant is given. A view-model of any toolkit shows one of these; the client tools and
/// the drop target act on it.
/// </summary>
public sealed class FileSystemModel
{
    public FileSystemModel(IShellServices shell, IReadOnlyDictionary<Type, IFeatureConfig> configs, IVirtualFileSystem vfs,
                           FileSystemFeatureRegistry registry, FileOperationQueue operations, FileCompanions companions)
    {
        Shell         = shell;
        Vfs           = vfs;
        Registry      = registry;
        Operations    = operations;
        Companions    = companions;
        ActionManager = new FileActionManager(registry);
        ExternalApps  = configs.TryGetValue(typeof(ExternalAppsConfig), out var config) && config is ExternalAppsConfig apps
                            ? apps : new ExternalAppsConfig();

        Location   = new FolderLocation();
        Listing    = new FolderListing(Location, vfs, shell, companions);
        Navigation = new FolderNavigation(Location, Listing, vfs, shell, registry, new DefaultFileOpener(registry));
        Creation   = new FileCreation(Location, vfs, shell);
        Actions    = new SelectionActions(Location, ActionManager, vfs);
        Assistance = new FolderAssistance(this);

        FileMapManager.Instance.RegisterKnownExperiences(ActionManager.AllExperiences);
    }

    public IShellServices Shell { get; }

    /// <summary>The runtime's file system, which the tab reads through: archive files browse like folders, and a
    /// connected location like any other.</summary>
    public IVirtualFileSystem Vfs { get; }

    public FileSystemFeatureRegistry Registry { get; }

    /// <summary>Copy, move and delete work in flight. One queue per workspace runtime, so an operation started in this tab
    /// is visible from every other file-browser tab and outlives this one.</summary>
    public FileOperationQueue Operations { get; }

    /// <summary>The companion files the features have declared — hidden in the list, carried by every operation.</summary>
    public FileCompanions Companions { get; }

    /// <summary>Every action, create kind and viewer experience the features declare.</summary>
    public FileActionManager ActionManager { get; }

    /// <summary>The user's external apps, as Options edits them.</summary>
    public ExternalAppsConfig ExternalApps { get; }

    public FolderLocation Location { get; }
    public FolderListing Listing { get; }
    public FolderNavigation Navigation { get; }
    public FileCreation Creation { get; }
    public SelectionActions Actions { get; }
    public FolderAssistance Assistance { get; }

    /// <summary>What is selected in the list — the rows a view last said were.</summary>
    public IReadOnlyList<FileSystemEntry> Selection { get; set; } = [];

    /// <summary>Surfaces a file-operation error (a failed drag-drop move, say) as a toast.</summary>
    public void ReportError(string message) => Shell.ShowError(message);
}
