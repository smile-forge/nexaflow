using System.Text.Json.Serialization;

namespace Nexaflow.Core.Models;

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

/// <summary>A persisted conversation record (list of messages with metadata).</summary>
public class ConversationRecord
{
    public string                      Id        { get; set; } = Guid.NewGuid().ToString();
    public DateTime                    StartedAt { get; set; } = DateTime.Now;
    public string                      Title     { get; set; } = "New conversation";
    public List<ConversationMessage>   Messages  { get; set; } = [];

    [JsonIgnore]
    public string DateDisplay => StartedAt.ToString("MMM d, HH:mm");

    /// <summary>Derives a title from the first user message.</summary>
    public void DeriveTitle()
    {
        var first = Messages.FirstOrDefault(m => m.IsUser)?.Text;
        if (first is { Length: > 0 })
            Title = first.Length > 40 ? first[..37] + "…" : first;
    }
}
