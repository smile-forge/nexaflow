using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Interaction;
using Nexaflow.Localization;

namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>A tab dropped anywhere on a conversation joins its context; files are attached to it.</summary>
public partial class ConversationViewModel : IDropTarget
{
    private static readonly string[] ContextKinds = [Page.DragKind, DragKinds.Files, DragKinds.FileContents];

    public IReadOnlyList<string> AcceptedKinds => ContextKinds;

    public DropOffer? OfferDrop(DropRequest request)
        => new(DropEffect.Copy, Str.Get("AIChat.Conversation.Context.DropAdds"));

    public Task AcceptDropAsync(DropRequest request)
    {
        if (request.Payload.Read(Page.DragKind) is Page page)
            AddContextItem(page);
        else
            foreach (var file in request.Payload.FilesOnDisk()) AddAttachment(file);
        return Task.CompletedTask;
    }
}
