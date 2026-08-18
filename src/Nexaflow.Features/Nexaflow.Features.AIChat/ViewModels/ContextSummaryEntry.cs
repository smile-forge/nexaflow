using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>
/// One entry in the collapsed context banner's summary row: what it is, and how much the AI can reach
/// through it. No page, no command — the collapsed row states what the model can see, and everything you
/// can DO about it lives in the expanded chips. The risk rides along because a collapsed banner must not
/// be a way to stop seeing that a high-risk scope is pinned.
/// </summary>
public sealed record ContextSummaryEntry(string Icon, string Title, ContextSecurityRisk Risk);
