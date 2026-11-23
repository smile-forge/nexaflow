namespace Nexaflow.Features.Common;

/// <summary>
/// A page's preview drawn from the page itself: the view marked as drawing <c>PagePreview&lt;TPage&gt;</c> binds to
/// <see cref="Page"/>. For a preview that shows the page's own state, live, rather than a snapshot of it.
/// </summary>
public sealed record PagePreview<TPage>(TPage Page) where TPage : IPageViewModel;
