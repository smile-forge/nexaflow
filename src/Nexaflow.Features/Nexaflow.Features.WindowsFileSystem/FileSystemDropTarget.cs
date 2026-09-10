using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using System;
using System.IO;
using System.Windows;
using Nexaflow.Features.WindowsFileSystem.Operations;
using System.Linq;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// Accepts file drag-drop onto <see cref="FileSystemView"/>, copying (or moving, with Shift)
/// dropped files into either the hovered folder node or the current directory. It decides nothing
/// about how: destinations, name clashes and the transfer itself belong to the operation queue.
/// </summary>
public sealed class FileSystemDropTarget : IDropTarget, IDropChoiceTarget
{
    private readonly FileSystemViewModel _viewModel;

    public FileSystemDropTarget(FileSystemViewModel viewModel)
        => _viewModel = viewModel;

    public bool CanAcceptDrop(IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop);

    /// <inheritdoc/>
    public bool IsSelfDrop(IDataObject data, string destinationPath)
    {
        if (string.IsNullOrEmpty(destinationPath)) return false;
        if (data.GetData(DataFormats.FileDrop) is not string[] sources) return false;

        string destination = Trim(destinationPath);
        return sources.Any(source => string.Equals(Trim(source), destination, StringComparison.OrdinalIgnoreCase));

        static string Trim(string path)
            => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string GetDropDescription(IDataObject data, string? targetFolderName, bool isMove)
    {
        return $"{(isMove ? "Move" : "Copy")} to {FolderLabel(targetFolderName)}";
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

    // ── Right-drag: ask, then do (IDropChoiceTarget) ──────────────────────────

    /// <summary>
    /// Reads the dropped paths here and now, for the same reason <see cref="Drop"/> does: this runs
    /// inside the OLE <c>IDropTarget::Drop</c> callback and the data object dies with it, while the
    /// menu the plan feeds is not answered until well after it has returned.
    /// </summary>
    public DropPlan? Capture(IDataObject data, string destinationPath)
    {
        if (string.IsNullOrEmpty(destinationPath)) return null;
        if (data.GetData(DataFormats.FileDrop) is not string[] sources || sources.Length == 0) return null;

        return new DropPlan(sources, destinationPath, FolderLabel(Path.GetFileName(destinationPath)));
    }

    /// <summary>
    /// The same queue call the modifier-driven drop makes, so a chosen move and a Shift-drag move are
    /// the one code path — refusals, "Copy of x" naming and self-copy rejection included.
    /// <para>
    /// Claimed first: a plan carries out once however many times it is asked to, which is what keeps a
    /// stale menu command from running a whole copy a second time.
    /// </para>
    /// </summary>
    public void Execute(DropPlan plan, DropChoice choice)
    {
        if (!plan.TryClaim()) return;
        _viewModel.Operations.EnqueueDrop(plan.Sources, plan.Destination, move: choice == DropChoice.Move);
    }

    /// <summary>
    /// Asked of the same planner that will carry the drop out, so the menu cannot come to disagree with
    /// what actually happens. A move to where the sources already are plans nothing; so does a folder
    /// dropped into itself, either way round.
    /// </summary>
    public bool CanExecute(DropPlan plan, DropChoice choice)
        => FileOperationDestinations.Plan(
               plan.Sources,
               Services.ShellPath.RealForMutation(plan.Destination),
               move: choice == DropChoice.Move,
               out _).Count > 0;

    public string GetChoicePrompt(string? targetFolderName)
        => $"Drop on {FolderLabel(targetFolderName)} to choose";

    /// <summary>The folder's display name, falling back to the current directory and then to "here".</summary>
    private string FolderLabel(string? targetFolderName)
        => !string.IsNullOrEmpty(targetFolderName)
            ? targetFolderName
            : !string.IsNullOrEmpty(_viewModel.CurrentPath)
                ? Path.GetFileName(_viewModel.CurrentPath)
                : "here";
}
