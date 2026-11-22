using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Interaction;
using System;
using System.IO;
using Nexaflow.Features.WindowsFileSystem.Operations;
using System.Linq;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// What a drop of files onto the file browser comes to, once the view has said which folder it is over: copied (or
/// moved, with Shift) there. It decides nothing about how: destinations, name clashes and the transfer itself belong
/// to the operation queue.
/// </summary>
public sealed class FileSystemDropTarget
{
    private readonly FileSystemViewModel _viewModel;

    public FileSystemDropTarget(FileSystemViewModel viewModel)
        => _viewModel = viewModel;

    public bool CanAcceptDrop(IDragPayload payload) => payload.CarriesFiles();

    /// <summary>
    /// True when <paramref name="destinationPath"/> is one of the things being dragged.
    /// <para>
    /// Dropping something onto itself is a gesture that means nothing, and it is almost never meant: a
    /// click that drifted the four pixels Windows calls a drag is enough to make one. Asked during the
    /// hover so the drop never happens, rather than being refused with a complaint afterwards.
    /// </para>
    /// </summary>
    public bool IsSelfDrop(IDragPayload payload, string destinationPath)
    {
        if (string.IsNullOrEmpty(destinationPath)) return false;

        string destination = Trim(destinationPath);
        return payload.Files().Any(source => string.Equals(Trim(source), destination, StringComparison.OrdinalIgnoreCase));

        static string Trim(string path)
            => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>The words beside the pointer: "Copy to Temp", "Move to Temp". <paramref name="targetFolderName"/> is
    /// the folder under it, or null over the background.</summary>
    public string GetDropDescription(string? targetFolderName, bool isMove)
    {
        return isMove
            ? Str.Format("WindowsFileSystem.Drop.MoveToFormat", FolderLabel(targetFolderName))
            : Str.Format("WindowsFileSystem.Drop.CopyToFormat", FolderLabel(targetFolderName));
    }

    /// <summary>
    /// Hands the dropped paths to the operation queue and returns.
    /// <para>
    /// Returning immediately is the whole point. This runs inside the OLE <c>IDropTarget::Drop</c>
    /// callback, so anything done here happens on the UI thread with no message pump — a 200 GB folder
    /// would freeze the window for hours, and a window that is not pumping cannot be given a second drop at
    /// all. Three drops make three queued operations.
    /// </para>
    /// The drop list is read here and now because the payload is only valid for the length of this
    /// call; everything after that is the queue's problem. Files that came as content are written out first and
    /// then moved in, so the queue still owns name clashes and destinations that are not plain folders.
    /// </summary>
    public void Drop(IDragPayload payload, string destinationPath, bool move)
    {
        var sources = payload.FilesOnDisk();
        if (sources.Count == 0) return;

        _viewModel.Operations.EnqueueDrop([.. sources], destinationPath, move || !payload.Contains(DragKinds.Files));
    }

    // ── Right-drag: ask, then do ──────────────────────────────────────────────

    /// <summary>
    /// Reads the dropped paths here and now, for the same reason <see cref="Drop"/> does: this runs
    /// inside the OLE <c>IDropTarget::Drop</c> callback and the payload dies with it, while the
    /// menu the plan feeds is not answered until well after it has returned.
    /// </summary>
    public DropPlan? Capture(IDragPayload payload, string destinationPath)
    {
        if (string.IsNullOrEmpty(destinationPath)) return null;

        var sources = payload.FilesOnDisk();
        if (sources.Count == 0) return null;

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
        => Str.Format("WindowsFileSystem.Drop.ChoicePromptFormat", FolderLabel(targetFolderName));

    /// <summary>The folder's display name, falling back to the current directory and then to "here".</summary>
    private string FolderLabel(string? targetFolderName)
        => !string.IsNullOrEmpty(targetFolderName)
            ? targetFolderName
            : !string.IsNullOrEmpty(_viewModel.CurrentPath)
                ? Path.GetFileName(_viewModel.CurrentPath)
                : Str.Get("WindowsFileSystem.Drop.Here");
}
