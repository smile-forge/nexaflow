using Nexaflow.Features.Common;
using Nexaflow.Core.ViewModels;
using System;
using System.IO;
using System.Windows;

namespace Nexaflow.Core.FileSystem;

/// <summary>
/// Accepts file drag-drop onto <see cref="FileSystemView"/>, copying dropped
/// files into either the hovered folder node or the current directory.
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

        foreach (var source in sources)
        {
            try
            {
                if (File.Exists(source))
                {
                    var dest = Path.Combine(destinationPath, Path.GetFileName(source));
                    if (move)
                        File.Move(source, dest, overwrite: false);
                    else
                        File.Copy(source, dest, overwrite: false);
                }
                else if (Directory.Exists(source))
                {
                    var dest = Path.Combine(destinationPath, Path.GetFileName(source));
                    if (move)
                        Directory.Move(source, dest);
                    else
                        CopyDirectory(source, dest);
                }
            }
            catch (Exception)
            {
                // Skip individual failures; partial operations are better than nothing.
            }
        }

        _viewModel.Refresh();
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }
}
