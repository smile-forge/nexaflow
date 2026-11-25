using System.Windows;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Services;

/// <summary><see cref="IFilePickers"/> drawn by WPF: the shell's themed picker windows, owned by the asking window's own.</summary>
internal sealed class WpfFilePickers : IFilePickers
{
    public string? PickOpenFile(IWindowHost? owner, WorkspaceRuntime workspace, IReadOnlyList<string>? extensions, string? initialPath)
        => FileBrowserWindow.Show(initialPath, extensions, WindowOf(owner), workspace);

    public IReadOnlyList<string> PickOpenFiles(IWindowHost? owner, WorkspaceRuntime workspace, IReadOnlyList<string>? extensions, string? initialPath)
        => FileBrowserWindow.ShowMany(initialPath, extensions, WindowOf(owner), workspace);

    public string? PickFolder(IWindowHost? owner, WorkspaceRuntime workspace, string? initialPath)
        => FolderBrowserWindow.Show(initialPath, WindowOf(owner), workspace);

    public string? PickSaveFile(IWindowHost? owner, WorkspaceRuntime workspace, string defaultFileName, IReadOnlyList<SaveFileType> types,
                                string? initialPath, long bytes)
        => FileBrowserWindow.ShowSave(defaultFileName, types, initialPath, bytes, WindowOf(owner), workspace);

    /// <summary>The WPF window a shell window's host draws in: every host here is a <see cref="ShellViewModel"/>.</summary>
    private static Window? WindowOf(IWindowHost? owner) => (owner as ShellViewModel)?.Window;
}
