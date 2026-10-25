using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.IO.Common;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Nexaflow.Features.Images;

/// <summary>
/// Claims a <see cref="DraftFile"/> that is a picture — one drawn, or cut out, for the user — from
/// <see cref="IShellServices.HandleObject"/> and opens it as a draft Images tab, with Save as to keep it; and keeps a
/// draft picture where the user picks, with its edits.
/// </summary>
public sealed class ImageDraftHandler(IShellServices shell) : IGenericObjectHandler
{
    public bool CanHandleObject(object obj) => obj is DraftFile { IsValid: true } draft && ImageFileTypes.IsImage(draft.Path);

    public void Handle(object obj)
    {
        if (obj is not DraftFile draft) return;
        shell.OpenTab(ImageTabRegistration.StaticPageKind, new Dictionary<string, string>
        {
            ["paths"]                     = draft.Path,
            [DraftFile.PageParameterName] = draft.OfferedFolder ?? string.Empty,
        });
    }

    /// <summary>
    /// Keeps a draft picture at <paramref name="to"/> with its edits: the picture, and the recipe beside it where it has one
    /// (written out first), so it opens where it is kept edited as the draft was — and never with the edits of a picture it
    /// replaced.
    /// </summary>
    public static async Task KeepAsync(ImageRecipeStore recipes, string from, string to)
    {
        await recipes.FlushAsync();
        await Task.Run(() =>
        {
            File.Copy(from, to, overwrite: true);
            var edits = ImageRecipeStore.SidecarPath(from);
            var kept  = ImageRecipeStore.SidecarPath(to);
            if (File.Exists(edits)) File.Copy(edits, kept, overwrite: true);
            else if (File.Exists(kept)) File.Delete(kept);
        });
    }
}
