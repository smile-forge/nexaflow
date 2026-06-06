using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nexaflow.Features.Common;

/// <summary>A single message in a conversation — from user or from Aria.</summary>
public class ConversationMessage
{
    public string    Id        { get; set; } = Guid.NewGuid().ToString();
    public string    Text      { get; set; } = string.Empty;
    public bool      IsUser    { get; set; }
    public DateTime  Timestamp { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string TimestampDisplay => Timestamp.ToString("HH:mm");
}

/// <summary>A page pinned as conversation context, identified by kind + params so it can be
/// recreated when the conversation is reopened.</summary>
public class ContextRef
{
    public string                      PageKind   { get; set; } = string.Empty;
    public Dictionary<string, string>? PageParams { get; set; }

    /// <summary>Version of the assembly that owns this page kind when the ref was captured. Lets a
    /// future load migrate, or flag the context item as unavailable, when the owning feature changes.</summary>
    public string?                     AssemblyVersion { get; set; }
}

/// <summary>A persisted conversation record (list of messages with metadata).</summary>
public class ConversationRecord
{
    public string                      Id        { get; set; } = Guid.NewGuid().ToString();
    public DateTime                    StartedAt { get; set; } = DateTime.Now;
    public string                      Title     { get; set; } = "New conversation";
    public List<ConversationMessage>   Messages  { get; set; } = [];

    /// <summary>File paths carried as conversation context (e.g. files a prior agent run read/created).</summary>
    public List<string>                Attachments { get; set; } = [];

    /// <summary>Context pages pinned to this conversation, persisted so the panel repopulates on reopen.</summary>
    public List<ContextRef>            Context     { get; set; } = [];

    [JsonIgnore]
    public string DateDisplay => StartedAt.ToString("MMM d, HH:mm");

    /// <summary>
    /// Derives a slug title from the first user message: its two longest words plus a short unique
    /// code (e.g. <c>"fermat-drinks-a4f9k"</c>). Acts as the conversation's human-readable id.
    /// </summary>
    public void DeriveTitle()
        => Title = GenerateSlug(Messages.FirstOrDefault(m => m.IsUser)?.Text);

    private static readonly Random _rng = new();
    private const string Alphanumeric = "abcdefghijklmnopqrstuvwxyz0123456789";

    private static string GenerateSlug(string? input)
    {
        var words = Regex.Matches(input ?? string.Empty, "[A-Za-z]+")
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length > 3)
            .Distinct()
            .OrderByDescending(w => w.Length)
            .Take(2)
            .ToList();

        string a = words.Count > 0 ? words[0] : "chat";
        string b = words.Count > 1 ? words[1] : "session";

        var code = new StringBuilder(5);
        for (int i = 0; i < 5; i++) code.Append(Alphanumeric[_rng.Next(Alphanumeric.Length)]);
        return $"{a}-{b}-{code}";
    }
}
