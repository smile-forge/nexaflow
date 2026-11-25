using System.Collections.Generic;
using Nexaflow.Interaction;
using Nexaflow.Localization;

namespace Nexaflow.Features.Compressed.ViewModels;

/// <summary>Files dropped anywhere on the page are added to the archive, where its format can be changed.</summary>
public sealed partial class CompressedViewModel : IDropTarget
{
    private static readonly string[] SourceKinds = [DragKinds.Files, DragKinds.FileContents];

    public IReadOnlyList<string> AcceptedKinds => SourceKinds;

    public DropOffer? OfferDrop(DropRequest request)
        => CanModify ? new DropOffer(DropEffect.Copy, Str.Get("Compressed.Drop.AddToArchive")) : DropOffer.Refused;

    public void AcceptDrop(DropRequest request)
    {
        var sources = request.Payload.FilesOnDisk();
        if (sources.Count > 0) _ = AddSourcesAsync(sources);
    }
}
