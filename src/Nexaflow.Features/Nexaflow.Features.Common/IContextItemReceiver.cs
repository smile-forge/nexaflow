namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by a page view-model that accepts another open page as a live context item (the
/// conversation page). Lets the shell hand a page to "whatever can receive it" without referencing the
/// feature's concrete view-model type.
/// </summary>
public interface IContextItemReceiver
{
    void AddContextItem(Page page);
}
