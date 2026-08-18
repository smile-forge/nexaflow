namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>
/// One entry in the collapsed context banner's summary row. Identity only — no page, no command: the
/// collapsed row states what the model can see, and everything you can *do* about it lives in the
/// expanded chips.
/// </summary>
public sealed record ContextSummaryEntry(string Icon, string Title);
