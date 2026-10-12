using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.Images.FileActions;

/// <summary>
/// Removes an image's edits — its recipe — after a confirmation, so it shows as it is on disk again. Offered only
/// for images that have a recipe beside them (<see cref="RequiredCompanionSuffix"/>).
/// </summary>
/// <remarks>
/// A viewer that has the image open sees its recipe go (it watches the sidecar) and follows, with the edits still a
/// step back in its undo history for as long as it stays open.
/// </remarks>
public sealed class RevertImageEditsAction : IFileAction, ICacheable
{
    private readonly IShellServices _shell;

    public RevertImageEditsAction(IShellServices shell) => _shell = shell;

    public bool    IsDestructive          => true;
    public bool    SupportsMultipleFiles  => true;
    public string  Icon                   => "↺";
    public string  DisplayName            => Str.Get("Images.Action.RevertEdits");
    public string? Tooltip                => Str.Get("Images.Action.RevertEditsTip");
    public static string? StaticExperienceId => "/image/edit/revert";
    public string  ExperienceId           => "/image/edit/revert";
    public string  ExperienceDescription  => Str.Get("Images.Action.RevertEditsDescription");
    public bool    RequiresRefresh        => false;   // refreshed once the confirmation is answered
    public bool    CanPerformAction       => true;
    public bool    RequiresFullyBackedPath => true;
    public string? RequiredCompanionSuffix => ImageRecipeStore.Extension;

    public bool PerformAction(string filePath) => PerformAction([filePath]);

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var edited = filePaths.Where(p => File.Exists(ImageRecipeStore.SidecarPath(p))).ToList();
        if (edited.Count == 0) return false;

        string message = edited.Count == 1
            ? Str.Format("Images.Action.RevertConfirmOneFormat", Path.GetFileName(edited[0]))
            : Str.Format("Images.Action.RevertConfirmManyFormat", edited.Count);

        _shell.ShowConfirmation(
            title:     Str.Get("Images.Action.RevertConfirmTitle"),
            message:   message,
            onConfirm: () =>
            {
                using var store = new ImageRecipeStore();
                foreach (var path in edited) store.Delete(path);
                _shell.RequestRefresh();
            },
            onCancel:  () => { });

        return false;   // the revert waits on the confirmation
    }
}
