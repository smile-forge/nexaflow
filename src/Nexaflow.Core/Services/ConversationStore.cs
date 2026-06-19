using Nexaflow.Features.Common;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Nexaflow.Core.Services;

/// <summary>
/// Canonical on-disk store for a workspace's conversations. Each conversation lives in its own
/// sub-folder named by its id (<c>&lt;dir&gt;/&lt;id&gt;/conversation.json</c>), matching the layout
/// <see cref="AIService"/> writes. Pure, directory-parameterised (no singletons) so it's unit-testable
/// and reusable by the per-workspace markdown export.
/// </summary>
public static class ConversationStore
{
    private const string FileName = "conversation.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Reads every conversation under <paramref name="conversationsDir"/>, newest first. Skips
    /// corrupt files. Returns an empty list when the directory doesn't exist.</summary>
    public static List<ConversationRecord> Load(string conversationsDir)
    {
        var result = new List<ConversationRecord>();
        if (!Directory.Exists(conversationsDir)) return result;

        foreach (var dir in Directory.EnumerateDirectories(conversationsDir))
        {
            var file = Path.Combine(dir, FileName);
            if (!File.Exists(file)) continue;
            try
            {
                var rec = JsonSerializer.Deserialize<ConversationRecord>(File.ReadAllText(file), JsonOpts);
                if (rec is not null) result.Add(rec);
            }
            catch { /* skip corrupt files */ }
        }

        result.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
        return result;
    }

    /// <summary>Writes <paramref name="conv"/> to <c>&lt;conversationsDir&gt;/&lt;id&gt;/conversation.json</c>.</summary>
    public static void Save(string conversationsDir, ConversationRecord conv)
    {
        var dir = Path.Combine(conversationsDir, conv.Id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, FileName), JsonSerializer.Serialize(conv, JsonOpts));
    }

    /// <summary>Deletes the conversation folder for <paramref name="id"/> (no-op when absent).</summary>
    public static void Delete(string conversationsDir, string id)
    {
        var dir = Path.Combine(conversationsDir, id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// Exports every conversation under <paramref name="conversationsDir"/> to one markdown file each in
    /// <paramref name="destDir"/> (created if needed). File names derive from the conversation title,
    /// sanitised and de-duplicated. Returns the number of files written.
    /// </summary>
    public static int ExportAll(string conversationsDir, string destDir)
    {
        var conversations = Load(conversationsDir);
        Directory.CreateDirectory(destDir);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var conv in conversations)
        {
            var name = UniqueFileName(FileNameFor(conv), used);
            File.WriteAllText(Path.Combine(destDir, name + ".md"), ToMarkdown(conv));
        }
        return conversations.Count;
    }

    /// <summary>Renders one conversation as markdown: a title, the start time, then each message
    /// under a "You"/"Assistant" heading. Deterministic (invariant culture, LF line endings).</summary>
    public static string ToMarkdown(ConversationRecord conv)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(conv.Title).Append("\n\n");
        sb.Append("_Started ").Append(Stamp(conv.StartedAt)).Append("_\n\n");

        foreach (var m in conv.Messages)
        {
            sb.Append("## ").Append(m.IsUser ? "You" : "Assistant")
              .Append(" — ").Append(Stamp(m.Timestamp)).Append("\n\n");
            sb.Append(m.Text.Replace("\r\n", "\n").TrimEnd()).Append("\n\n");
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string Stamp(DateTime dt)
        => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A filesystem-safe base name for a conversation (its title, or id when blank).</summary>
    private static string FileNameFor(ConversationRecord conv)
    {
        var raw = string.IsNullOrWhiteSpace(conv.Title) ? conv.Id : conv.Title;
        var safe = new string(raw.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? conv.Id : safe;
    }

    /// <summary>Appends " (2)", " (3)", … when a base name is already taken (case-insensitive).</summary>
    private static string UniqueFileName(string baseName, HashSet<string> used)
    {
        var name = baseName;
        for (int i = 2; !used.Add(name); i++)
            name = $"{baseName} ({i})";
        return name;
    }
}
