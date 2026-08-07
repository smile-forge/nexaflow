namespace Nexaflow.Features.OneDrive.Models;

/// <summary>One configured OneDrive account with a local sync folder.</summary>
/// <param name="Id">Stable, path-segment-safe identity derived from the account's registry key
/// (<c>onedrive.Personal</c>, <c>onedrive.Business1</c>). It becomes the virtual root the browser
/// navigates and is baked into saved tab state, so it must not follow the display name.</param>
/// <param name="Label">What to call it — the account's display name, its email, or a bare fallback.</param>
/// <param name="FolderPath">The local sync root.</param>
/// <param name="IsBusiness">A work/school account rather than a personal one; only affects the label.</param>
public sealed record SyncAccount(string Id, string Label, string FolderPath, bool IsBusiness);
