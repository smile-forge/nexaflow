using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexaflow.Visuals.Common.Drafts;

/// <summary>
/// A draft's Save as: a file made for the user and kept in a temp folder — a picture drawn, a recording, a clip, a cut-out —
/// kept where the user picks, starting in the folder it was offered. How it is kept is the viewer's: a copy of the file, or
/// the file with what goes with it, such as a picture's edits. Once kept, <see cref="Kept"/> says where, for the viewer's
/// tab to become that file. <see cref="DraftBanner"/> shows it.
/// </summary>
public sealed partial class DraftSaving : ObservableObject
{
    private readonly string? _offeredFolder;
    private readonly Func<string, IReadOnlyList<string>, string?, Task<string?>> _pick;
    private readonly Action<string> _failed;
    private readonly Func<string, string, Task> _keep;

    /// <param name="path">The draft, in its temp folder.</param>
    /// <param name="offeredFolder">Where Save as starts; null for wherever the picker starts.</param>
    /// <param name="pick">Asks the user where to keep it — a suggested name, the extensions offered, the folder to start in —
    /// and answers null when they cancel.</param>
    /// <param name="failed">Tells the user it could not be kept, and why.</param>
    /// <param name="keep">Writes the draft at the path picked; a copy of the file when not given.</param>
    public DraftSaving(string path, string? offeredFolder, Func<string, IReadOnlyList<string>, string?, Task<string?>> pick,
                       Action<string> failed, Func<string, string, Task>? keep = null)
    {
        Path           = path;
        _offeredFolder = offeredFolder;
        _pick          = pick;
        _failed        = failed;
        _keep          = keep ?? Copy;
    }

    /// <summary>The draft, in its temp folder.</summary>
    public string Path { get; }

    /// <summary>Raised with where the draft was kept, once it is.</summary>
    public event Action<string>? Kept;

    /// <summary>A save under way: Save as waits for it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    private bool _isSaving;

    /// <summary>Asks where to keep the draft — with its own extension, so it stays what it is — and keeps it there.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private async Task SaveAs()
    {
        var extension = System.IO.Path.GetExtension(Path);
        if (await _pick(System.IO.Path.GetFileNameWithoutExtension(Path), [extension], _offeredFolder) is not { } picked) return;
        var target = System.IO.Path.ChangeExtension(picked, extension);

        IsSaving = true;
        try
        {
            await _keep(Path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _failed(ex.Message);
            return;
        }
        finally
        {
            IsSaving = false;
        }
        Kept?.Invoke(target);
    }

    private bool CanSaveAs => !IsSaving;

    /// <summary>A copy of the draft at <paramref name="to"/>, written over a file the user chose to replace.</summary>
    public static Task Copy(string from, string to) => Task.Run(() => File.Copy(from, to, overwrite: true));
}
