using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat.ViewModels;

public partial class AiChatViewModel : ObservableObject, IPageViewModel
{
    // Conversation history (left sidebar)
    public ObservableCollection<ConversationRecord> Conversations { get; } = [];

    // Active conversation
    [ObservableProperty] private ConversationRecord? _activeConversation;
    [ObservableProperty] private bool _isStreaming;

    /// Messages of the active conversation, ordered by timestamp.
    public ObservableCollection<ConversationMessage> Messages { get; } = [];

    // Events
    public event EventHandler? ScrollRequested;

    private readonly IAIService _aiService;

    public AiChatViewModel(IAIService aiService)
    {
        _aiService = aiService;
        _ = LoadConversationsAsync();
    }

    // Public API used by ShellViewModel

    /// Adds a user message and an Aria reply to the active (or new) conversation,
    /// persists to disk, then fires ScrollRequested.
    public async Task AddExchangeAsync(string userText, string ariaText)
    {
        EnsureActiveConversation();

        AppendMessage(userText, isUser: true);
        AppendMessage(ariaText, isUser: false);

        ActiveConversation!.DeriveTitle();
        await _aiService.SaveAsync(ActiveConversation);

        OnScrollRequested();
    }

    /// Switches the displayed conversation to the given record.
    [RelayCommand]
    public void SelectConversation(ConversationRecord record)
    {
        ActiveConversation = record;
        Messages.Clear();
        foreach (var m in record.Messages.OrderBy(m => m.Timestamp))
            Messages.Add(m);
        OnScrollRequested();
    }

    /// Starts a brand new conversation (clears display).
    [RelayCommand]
    public void NewConversation()
    {
        ActiveConversation = null;
        Messages.Clear();
    }

    // Helpers

    public void EnsureActiveConversation()
    {
        if (ActiveConversation is not null) return;

        var record = new ConversationRecord();
        ActiveConversation = record;
        Conversations.Insert(0, record);
        Messages.Clear();
    }

    private ConversationMessage AppendMessage(string text, bool isUser)
    {
        var msg = new ConversationMessage
        {
            Text      = text,
            IsUser    = isUser,
            Timestamp = DateTime.Now
        };
        ActiveConversation!.Messages.Add(msg);
        Messages.Add(msg);
        return msg;
    }

    private async Task LoadConversationsAsync()
    {
        var records = await _aiService.LoadAllAsync();
        foreach (var r in records)
            Conversations.Add(r);

        if (Conversations.Count > 0)
            SelectConversation(Conversations[0]);
    }

    private void OnScrollRequested() => ScrollRequested?.Invoke(this, EventArgs.Empty);

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        if (ActiveConversation is not { Messages.Count: > 0 } conv)
        {
            var count = Conversations.Count;
            return count == 0
                ? "AI Chat tab: no conversations yet."
                : $"AI Chat tab: {count} conversation(s), none currently active.";
        }
        return string.Join("\n", conv.Messages.TakeLast(6)
            .Select(m => (m.IsUser ? "User" : "Aria") + ": " + m.Text));
    }
}
