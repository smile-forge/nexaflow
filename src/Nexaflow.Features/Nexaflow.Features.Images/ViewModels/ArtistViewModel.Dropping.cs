using Nexaflow.Interaction;
using Nexaflow.Localization;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// Pictures dropped anywhere on Artist: each a layer above the active one, or — on the start screen, which has no
/// picture to layer them on — opened.
/// </summary>
public sealed partial class ArtistViewModel : IDropTarget
{
    private static readonly string[] PictureKinds = [DragKinds.Files, DragKinds.FileContents, DragKinds.Image];

    /// <summary>The name a picture dragged with no file of its own is kept under.</summary>
    private const string NamelessPicture = "picture.png";

    public IReadOnlyList<string> AcceptedKinds => PictureKinds;

    public DropOffer? OfferDrop(DropRequest request)
    {
        bool holdsPicture = request.Payload.CarriesFiles()
            ? request.Payload.FileNames().Any(ImageFileTypes.IsImage)
            : request.Payload.Contains(DragKinds.Image);

        return holdsPicture
            ? new DropOffer(DropEffect.Copy, Str.Get(IsEditing ? "Images.Drop.AddAsLayer" : "Images.Drop.OpenInArtist"))
            : null;
    }

    public void AcceptDrop(DropRequest request)
    {
        var payload = request.Payload;
        IReadOnlyList<string> pictures =
            payload.CarriesFiles() ? [.. payload.FilesOnDisk().Where(ImageFileTypes.IsImage)]
            : payload.Image() is { } png ? DropStaging.Default.Save([new CarriedFile(NamelessPicture, false, into => into.Write(png))])
            : [];
        if (pictures.Count == 0) return;

        if (IsEditing)
            _ = Pictures.AddFilesAsync(pictures);
        else if (pictures.Count == 1 && payload.Contains(DragKinds.Files))
            Open(pictures[0]);
        else
            _ = Start.OpenAsLayersAsync(pictures);   // several, or with no place to be saved back to: a canvas of their own
    }
}
