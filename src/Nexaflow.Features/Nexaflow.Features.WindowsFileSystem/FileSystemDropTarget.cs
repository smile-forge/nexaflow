using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using System;
using System.IO;
using System.Windows;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// Accepts file drag-drop onto <see cref="FileSystemView"/>, copying (or moving, with Shift)
/// dropped files into either the hovered folder node or the current directory. It decides nothing
/// about how: destinations, name clashes and the transfer itself belong to the operation queue.
/// </summary>
public sealed class FileSystemDropTarget : IDropTarget
{
    private readonly FileSystemViewModel _viewModel;

    public FileSystemDropTarget(FileSystemViewModel viewModel)
        => _viewModel = viewModel;

    public bool CanAcceptDrop(IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop);

    public string GetDropDescription(IDataObject data, string? targetFolderName, bool isMove)
    {
        var name = targetFolderName
            ?? (!string.IsNullOrEmpty(_viewModel.CurrentPath)
                ? Path.GetFileName(_viewModel.CurrentPath)
                : null)
            ?? "here";
        return $"{(isMove ? "Move" : "Copy")} to {name}";
    }

    /// <summary>
    /// Hands the dropped paths to the operation queue and returns.
    /// <para>
    /// Returning immediately is the whole point. This runs inside the OLE <c>IDropTarget::Drop</c>
    /// callback, so anything done here happens on the UI thread with no message pump — a 200 GB folder
    /// froze the window for hours, and a window that is not pumping cannot be given a second drop at
    /// all, so the next two folders someone dragged over went nowhere. Three drops now make three
    /// queued operations.
    /// </para>
    /// The drop list is read here and now because the data object is only valid for the length of this
    /// call; everything after that is the queue's problem.
    /// </summary>
    public void Drop(IDataObject data, string destinationPath, bool move)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] sources) return;
        _viewModel.Operations.EnqueueDrop(sources, destinationPath, move);
    }
}
