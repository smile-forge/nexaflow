using Google.GenAI;

namespace Nexaflow.Providers.Gemini;

/// <summary>The models a key reaches, as each Gemini provider type lists those of its kind.</summary>
internal static class GeminiModels
{
    /// <summary>The names <paramref name="keep"/> takes, without the API's "models/" prefix, sorted.</summary>
    public static async Task<IReadOnlyList<string>> ListAsync(Client client, Func<string, bool> keep, CancellationToken ct)
    {
        var pager = await client.Models.ListAsync(cancellationToken: ct);
        var names = new List<string>();

        await foreach (var m in pager.WithCancellation(ct))
        {
            if (m.Name is null) continue;
            var name = m.Name.StartsWith("models/", StringComparison.Ordinal) ? m.Name["models/".Length..] : m.Name;
            if (keep(name)) names.Add(name);
        }

        names.Sort();
        return names;
    }
}
