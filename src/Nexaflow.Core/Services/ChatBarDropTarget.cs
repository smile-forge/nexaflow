using Nexaflow.Features.Common;
using Nexaflow.Interaction;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services;

/// <summary>
/// The AI input bar as a drop target: whatever an <see cref="IChatDropHandler"/> takes becomes text at the caret. Set on
/// the box itself, so a handler is asked before the box's own text editing and words it does not take are still the box's.
/// </summary>
internal sealed class ChatBarDropTarget(
    Func<IReadOnlyList<IChatDropHandler>> handlers,
    Func<IPageViewModel?> activePage,
    Action<string> insert) : IDropTarget
{
    public IReadOnlyList<string> AcceptedKinds => [.. handlers().SelectMany(handler => handler.AcceptedKinds)];

    public DropOffer? OfferDrop(DropRequest request)
        => Takers(request.Payload).Any()
            ? new DropOffer(DropEffect.Copy, Str.Get("Shell.Drop.InsertInBar"))
            : null;

    public void AcceptDrop(DropRequest request)
    {
        var page = activePage();
        foreach (var handler in Takers(request.Payload))
        {
            if (handler.BuildInsertText(request.Payload, page) is not { Length: > 0 } text) continue;
            insert(text);
            return;
        }
    }

    private IEnumerable<IChatDropHandler> Takers(IDragPayload payload)
    {
        var page = activePage();
        return handlers().Where(handler => payload.ContainsAny(handler.AcceptedKinds) && handler.CanHandle(page));
    }
}
