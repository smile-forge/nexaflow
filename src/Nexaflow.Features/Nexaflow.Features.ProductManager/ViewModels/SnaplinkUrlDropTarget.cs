using System;
using System.Collections.Generic;
using Nexaflow.Interaction;
using Nexaflow.Localization;

namespace Nexaflow.Features.ProductManager.ViewModels;

/// <summary>The "drop a URL here" box of the Link URL tab: the address dropped on it, or words taken for one.</summary>
internal sealed class SnaplinkUrlDropTarget(Action<string> setUrl) : IDropTarget
{
    private static readonly string[] AddressKinds = [DragKinds.Link, DragKinds.Text];

    public IReadOnlyList<string> AcceptedKinds => AddressKinds;

    public DropOffer? OfferDrop(DropRequest request)
        => new(DropEffect.Copy, Str.Get("ProductManager.Snaplinks.DropSetsUrl"));

    public Task AcceptDropAsync(DropRequest request)
    {
        if ((request.Payload.Link() ?? request.Payload.Text()) is { } url) setUrl(url);
        return Task.CompletedTask;
    }
}
