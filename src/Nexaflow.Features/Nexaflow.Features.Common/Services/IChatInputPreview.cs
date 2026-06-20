namespace Nexaflow.Features.Common;

/// <summary>
/// Lets the active page mirror what the user is typing in the AI input bar — e.g. the terminal echoing a
/// <c>&gt;</c>-prefixed command at a faux prompt before it is sent, so you see what you're about to run.
/// Discovered by reflection and instantiated per workspace; the shell calls <see cref="OnInputChanged"/>
/// as the bar text changes (and with an empty string on send/clear) for whichever handler claims the
/// active page. Generic by design so any page (e.g. AI chat) can opt in later.
/// </summary>
public interface IChatInputPreview
{
    /// <summary>True if this handler wants input updates while <paramref name="pageVm"/> is active.</summary>
    bool CanPreview(IPageViewModel? pageVm);

    /// <summary>The current bar text (verbatim, including any symbol prefix). Empty = cleared / sent.</summary>
    void OnInputChanged(string text, IPageViewModel? pageVm);
}
