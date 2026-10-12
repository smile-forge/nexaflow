using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;

namespace Nexaflow.Features.Images.Editing.Recipe;

/// <summary>
/// Tells the file browser that a photo's recipe belongs to it: hidden beside the photo, which shows an "edited"
/// badge, and carried along whenever the photo is copied, moved, renamed or deleted — so its edits go where it goes.
/// The swap file a save writes for a moment is a companion too, so it never flickers into the list.
/// </summary>
public sealed class ImageRecipeCompanions : IFileCompanionProvider
{
    public IReadOnlyList<FileCompanionKind> Companions { get; } =
    [
        new(ImageRecipeStore.Extension, Badge: "✎", BadgeTooltip: Str.Get("Images.Companion.EditedTip")),
        new(ImageRecipeStore.PendingExtension),
    ];
}
