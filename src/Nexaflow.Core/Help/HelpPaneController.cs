using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Help;

/// <summary>
/// The shell's Help button (and F1): opens the help for the page the reader is using in the pane beside it,
/// splitting the window if it has to, and keeps that help on whatever page the reader moves to — until they move
/// within help themselves, which pins it. Pressed while the help already shows the page in use, it closes.
/// <para>
/// A window has at most one Help tab (its parameters say where to look, not which tab it is). The subject is the page
/// the reader is using: the focused pane's, or — when the focus is on the help itself — the other pane's.
/// </para>
/// </summary>
/// <param name="host">The window whose panes it works on.</param>
/// <param name="isPinned">Whether the reader has taken a Help tab somewhere themselves; by default, asked of the tab's
/// view model. Tests supply their own rather than realising a view.</param>
internal sealed class HelpPaneController(IHelpPaneHost host, Func<Page, bool>? isPinned = null)
{
    private static string HelpKind => HelpPageRegistration.StaticPageKind;

    public void Toggle()
    {
        var (subjectPane, subject) = Subject();
        var help = FindHelp(out var helpPane);

        if (help is not null)
        {
            if (helpPane!.ActivePage == help && (subject is null || Shows(help, subject)))
            {
                host.Close(help);
                return;
            }

            // Beside the page it explains, re-pointed at it — unless it shares that page's pane, where showing it would
            // hide the page it is about: then it moves beside it, below.
            if (!ReferenceEquals(helpPane, subjectPane))
            {
                Retarget(help, subject);
                host.Activate(help);
                return;
            }
            host.Close(help);
        }

        var target = host.LeafPanes.FirstOrDefault(p => !ReferenceEquals(p, subjectPane))
                     ?? host.SplitBeside(subjectPane ?? host.FocusedPane);
        host.OpenInPane(target, HelpKind, ParamsFor(subject));
    }

    /// <summary>A pane's shown page changed: an open, unpinned Help tab in the other pane follows it.</summary>
    public void OnActivePageChanged(Pane pane)
    {
        var help = FindHelp(out var helpPane);
        if (help is null || ReferenceEquals(pane, helpPane)) return;

        var page = pane.ActivePage;
        if (page is null || string.IsNullOrEmpty(page.PageKind) || IsHelp(page)) return;
        if (IsPinned(help) || Shows(help, page)) return;

        Retarget(help, page);
    }

    // The page the reader is using, and its pane.
    private (Pane? Pane, Page? Page) Subject()
    {
        var focused = host.FocusedPane;
        if (focused.ActivePage is { } page && !IsHelp(page)) return (focused, page);

        var other = host.LeafPanes.FirstOrDefault(p => !ReferenceEquals(p, focused));
        return other?.ActivePage is { } beside && !IsHelp(beside) ? (other, beside) : (other, null);
    }

    private Page? FindHelp(out Pane? pane)
    {
        foreach (var candidate in host.LeafPanes)
            if (candidate.Pages.FirstOrDefault(IsHelp) is { } help)
            {
                pane = candidate;
                return help;
            }
        pane = null;
        return null;
    }

    private static bool IsHelp(Page page) => string.Equals(page.PageKind, HelpKind, StringComparison.OrdinalIgnoreCase);

    private static bool Shows(Page help, Page subject)
        => string.Equals(help.PageParams?.GetValueOrDefault("topic"), subject.PageKind, StringComparison.OrdinalIgnoreCase);

    private bool IsPinned(Page help) => (isPinned ?? PinnedByReader)(help);

    private static bool PinnedByReader(Page help) => (help.Content as HelpView)?.ViewModel is HelpViewModel { IsPinned: true };

    private static Dictionary<string, string> ParamsFor(Page? subject)
        => subject?.PageKind is { Length: > 0 } kind ? new() { ["topic"] = kind } : [];

    // Params first, so a Help tab not yet shown reads the new topic when it is; a shown one is told now.
    private static void Retarget(Page help, Page? subject)
    {
        var pageParams = ParamsFor(subject);
        help.PageParams = pageParams;
        (help.Content as HelpView)?.Reinitialize(pageParams);
    }
}
