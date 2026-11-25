using Nexaflow.Features.Common;

namespace Nexaflow.Session.Services;

/// <summary>
/// The views open pages are drawn with, as the session sees them: the toolkit makes and keeps one per page while the page is
/// shown, and the session tells it when a page has closed for good, so its view goes with it.
/// </summary>
public interface IPageViews
{
    /// <summary>Lets <paramref name="page"/>'s view go: the page has closed and will not be shown again.</summary>
    void Release(Page page);
}
