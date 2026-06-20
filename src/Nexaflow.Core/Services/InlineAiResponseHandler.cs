using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// Renders the AI agent's activity in a dissolvable banner hosted inside an <see cref="IChatEngagement"/>
/// page (e.g. under the console), instead of the modal overlay. One instance per engagement page, created
/// and wired by the shell. Reuses the shared state machine from <see cref="AiResponseHandlerBase"/>; it only
/// adds banner visibility, dissolve-on-page-action, and escalation to the modal overlay when an answer is
/// too tall to fit (but only while the page is the active tab — a background result stays in the banner).
/// </summary>
public partial class InlineAiResponseHandler : AiResponseHandlerBase
{
    private readonly IChatEngagement _engagement;
    private readonly Page _page;

    public InlineAiResponseHandler(ShellViewModel shell, IChatEngagement engagement, Page page)
        : base(shell)
    {
        _engagement = engagement;
        _page       = page;
        _engagement.EngagementInvalidated += OnEngagementInvalidated;
    }

    /// <summary>Whether the banner is shown (bound by <c>AiResponseBanner.xaml</c>).</summary>
    [ObservableProperty] private bool _bannerVisible;

    protected override void Open()  => BannerVisible = true;
    protected override void Close() => BannerVisible = false;

    /// <summary>Pin the engagement page (not whatever's active) when continuing as a conversation.</summary>
    protected override Page? OriginTab() => _page;

    /// <summary>The page took an action (e.g. the user ran a command) → dissolve the banner.</summary>
    private void OnEngagementInvalidated() => Dispatch(Dismiss);

    /// <summary>
    /// True only while the engagement page is the active tab — escalation pops the shared modal overlay,
    /// so a banner on a background tab must NOT escalate (its full answer stays in the scrollable banner
    /// until the user opens the tab).
    /// </summary>
    public bool CanEscalate => _page.IsActive;

    /// <summary>
    /// Moves the current final answer into the modal overlay and dissolves the banner. Called by the banner
    /// view when a Message-state answer is taller than the banner can show. No-op unless <see cref="CanEscalate"/>.
    /// </summary>
    public void Escalate() => Dispatch(() =>
    {
        if (!CanEscalate) return;
        _shell.Ai.ShowFinal(AiResponseText);
        Close();
    });
}
