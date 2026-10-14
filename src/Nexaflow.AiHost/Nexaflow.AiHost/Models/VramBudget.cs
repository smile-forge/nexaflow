namespace Nexaflow.AiHost.Models;

/// <summary>
/// Admission against a GPU's budget: whether a model fits, and which loaded models must go first. Only models nothing
/// is using may go, least recently used first, and only as many as the room needs — those outside the resident set,
/// and for a picture, residents after them. When models in use stand in the way, it can wait until they are not;
/// when even all that may go would not make room, nothing goes and the model is refused — a load that would
/// over-commit the card fails in the middle of somebody's reply instead.
/// </summary>
internal static class VramBudget
{
    public sealed record Loaded(string Id, long Bytes, bool Resident, bool InUse, long LastUsed);

    /// <summary>What admitting a model takes: the models to unload, in order — none when it fits as things are — or,
    /// when only models in use stand in the way, waiting until they are not.</summary>
    public sealed record Admission(IReadOnlyList<string> Unload, bool Waits);

    /// <param name="displaceResidents">Residents may go too, after every other model that may: room for a picture.</param>
    /// <returns>What admitting it takes, or null when it cannot fit even with everything gone that may go.</returns>
    public static Admission? Plan(long budget, IReadOnlyCollection<Loaded> loaded, long needed, bool displaceResidents = false)
    {
        long free = budget - loaded.Sum(m => m.Bytes);
        var mayGo = loaded.Where(m => !m.Resident || displaceResidents).OrderBy(m => m.Resident).ThenBy(m => m.LastUsed).ToList();
        var unload = new List<string>();
        foreach (var spare in mayGo.Where(m => !m.InUse))
        {
            if (free >= needed) break;
            unload.Add(spare.Id);
            free += spare.Bytes;
        }
        if (free >= needed) return new Admission(unload, Waits: false);
        return free + mayGo.Where(m => m.InUse).Sum(m => m.Bytes) >= needed ? new Admission([], Waits: true) : null;
    }
}
