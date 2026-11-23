namespace Nexaflow.Features.Common;

/// <summary>
/// A page that only says why it is empty: a page kind whose feature is missing, a page with nothing to open yet. Left
/// wordless it is indistinguishable from a feature that loaded and drew nothing, so it says so.
/// </summary>
public sealed record PlaceholderViewModel(string Headline, string? Detail = null) : IPageViewModel
{
    public string GetContext() => Detail is null ? Headline : $"{Headline}\n{Detail}";
}
