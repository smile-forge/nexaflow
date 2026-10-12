namespace Nexaflow.AiHost.Models;

/// <summary>
/// Admission against the GPU's budget: whether a model fits, and which loaded models must go first. Only models
/// neither resident nor in use may go, least recently used first, and only as many as the room needs; when even
/// all of them would not make room, nothing goes and the model is refused — a load that would over-commit the card
/// fails in the middle of somebody's reply instead.
/// </summary>
internal static class VramBudget
{
    public sealed record Loaded(string Id, long Bytes, bool Resident, bool InUse, long LastUsed);

    /// <returns>The models to unload, in order — empty when it fits as things are — or null when it cannot fit.</returns>
    public static IReadOnlyList<string>? Plan(long budget, IReadOnlyCollection<Loaded> loaded, long needed)
    {
        long free = budget - loaded.Sum(m => m.Bytes);
        var unload = new List<string>();
        foreach (var spare in loaded.Where(m => !m.Resident && !m.InUse).OrderBy(m => m.LastUsed))
        {
            if (free >= needed) break;
            unload.Add(spare.Id);
            free += spare.Bytes;
        }
        return free >= needed ? unload : null;
    }
}
