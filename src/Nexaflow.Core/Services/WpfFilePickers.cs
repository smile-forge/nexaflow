using System.Windows;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Services;

/// <summary><see cref="IFilePickers"/> drawn by WPF: the shell's themed picker windows, modal and owned by the asking window's own,
/// so each answers when it closes.</summary>
internal sealed class WpfFilePickers : IFilePickers
{
    public Task<string?> PickOpenFileAsync(IWindowHost? owner, WorkspaceRuntime workspace, IReadOnlyList<string>? extensions, string? initialPath)
        => Task.FromResult(FileBrowserWindow.Show(initialPath, extensions, WindowOf(owner), workspace));

    public Task<IReadOnlyList<string>> PickOpenFilesAsync(IWindowHost? owner, WorkspaceRuntime workspace, IReadOnlyList<string>? extensions, string? initialPath)
        => Task.FromResult(FileBrowserWindow.ShowMany(initialPath, extensions, WindowOf(owner), workspace));

    public Task<string?> PickFolderAsync(IWindowHost? owner, WorkspaceRuntime workspace, string? initialPath)
        => Task.FromResult(FolderBrowserWindow.Show(initialPath, WindowOf(owner), workspace));

    public Task<string?> PickSaveFileAsync(IWindowHost? owner, WorkspaceRuntime workspace, string defaultFileName, IReadOnlyList<SaveFileType> types,
                                           string? initialPath, long bytes)
        => Task.FromResult(FileBrowserWindow.ShowSave(defaultFileName, types, initialPath, bytes, WindowOf(owner), workspace));

    /// <summary>The WPF window a shell window's host draws in: every host here is a <see cref="ShellViewModel"/>.</summary>
    private static Window? WindowOf(IWindowHost? owner) => (owner as ShellViewModel)?.Window;
}
