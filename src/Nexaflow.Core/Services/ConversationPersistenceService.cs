using Nexaflow.Core.Models;
using System.IO;
using System.Text.Json;

namespace Nexaflow.Core.Services;

/// <summary>
/// Loads and saves <see cref="ConversationRecord"/> objects to
/// %AppData%\Aria\Shell\Conversations\{id}\conversation.json.
/// </summary>
public static class ConversationPersistenceService
{
    private static readonly string BaseDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Aria", "Shell", "Conversations");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static async Task SaveAsync(ConversationRecord record)
    {
        try
        {
            var dir  = Path.Combine(BaseDir, record.Id);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "conversation.json");
            await using var fs = File.Open(file, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(fs, record, JsonOpts);
        }
        catch { /* never crash on persistence failures */ }
    }

    /// <summary>Loads all saved conversations, newest first.</summary>
    public static async Task<List<ConversationRecord>> LoadAllAsync()
    {
        var result = new List<ConversationRecord>();
        if (!Directory.Exists(BaseDir)) return result;

        foreach (var dir in Directory.EnumerateDirectories(BaseDir))
        {
            var file = Path.Combine(dir, "conversation.json");
            if (!File.Exists(file)) continue;
            try
            {
                await using var fs = File.OpenRead(file);
                var rec = await JsonSerializer.DeserializeAsync<ConversationRecord>(fs, JsonOpts);
                if (rec is not null) result.Add(rec);
            }
            catch { /* skip corrupt files */ }
        }

        result.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
        return result;
    }
}
