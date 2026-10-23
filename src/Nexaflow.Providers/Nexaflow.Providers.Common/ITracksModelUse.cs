namespace Nexaflow.Providers.Common;

/// <summary>
/// A provider whose model stays loaded between requests — in this process, in the AI host, or in a local server — and
/// so costs memory while nothing asks it anything. It reports each use through <see cref="ModelUse"/>; the app loads it
/// ahead (<see cref="IAiProvider.WarmupAsync"/>) only when it is used most days, and lets it go
/// (<see cref="IAiProvider.CooldownAsync"/>) once it has stood idle, unless it was loaded ahead.
/// </summary>
public interface ITracksModelUse
{
    ModelUseTracker ModelUse { get; }

    /// <summary>Whether its backend unloads the model by itself once it has stood idle — Ollama's keep-alive — so the app
    /// only counts its uses and loads it ahead, never releasing it.</summary>
    bool ReleasesItself => false;
}
