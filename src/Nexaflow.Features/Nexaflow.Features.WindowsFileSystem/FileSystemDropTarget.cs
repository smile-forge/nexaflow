using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// Accepts file drag-drop onto <see cref="FileSystemView"/>, copying (or moving, with Shift)
/// dropped files into either the hovered folder node or the current directory.
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

    public void Drop(IDataObject data, string destinationPath, bool move)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] sources) return;

        var    failures = new List<string>();
        string verb     = move ? "move" : "copy";

        foreach (var source in sources)
        {
            // Source vanished between drag and drop — nothing to do, not worth a complaint.
            bool isFile = File.Exists(source);
            bool isDir  = Directory.Exists(source);
            if (!isFile && !isDir) continue;

            var dest = Path.Combine(destinationPath, Path.GetFileName(
                source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

            try
            {
                if (isFile)
                {
                    if (move) FileTransfer.MoveFile(source, dest);
                    else      FileTransfer.CopyFile(source, dest);
                }
                else
                {
                    if (move) FileTransfer.MoveDirectory(source, dest);
                    else      FileTransfer.CopyDirectory(source, dest);
                }
            }
            catch (FileOperationException ex)
            {
                failures.Add(ex.Message);
            }
        }

        _viewModel.Refresh();

        if (failures.Count > 0)
            _viewModel.ReportError(string.Join(Environment.NewLine, failures));
    }
}
