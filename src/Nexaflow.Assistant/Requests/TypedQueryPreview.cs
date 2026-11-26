using Nexaflow.Features.Common;
using Nexaflow.Session.Services;

namespace Nexaflow.Assistant.Requests;

/// <summary>What the words typed so far point at: the symbol of the query handler that clearly owns them, or that the assistant is
/// listening — none owns them, so the AI answers — and the words that handler would complete them with.</summary>
/// <param name="Symbol">The owning handler's symbol; null when none owns the words.</param>
/// <param name="Listening">No handler owns the words: the assistant answers them.</param>
/// <param name="Completion">What the owning handler would complete the words with; null when it offers nothing.</param>
public sealed record QueryPreview(string? Symbol, bool Listening, string? Completion)
{
    /// <summary>Nothing is typed, so nothing is pointed at.</summary>
    public static readonly QueryPreview None = new(null, false, null);
}

/// <summary>
/// Works out, as the user types into the AI bar, what the words point at: a pause after the last key, then the workspace's query
/// handlers are scored against the words and the page in front of the user, and the one that clearly owns them is asked how it would
/// complete them. A completion that arrives after the words have changed is dropped.
/// <para>Called on the UI thread, where the handlers and the page live; <see cref="Changed"/> is raised there.</para>
/// </summary>
public sealed class TypedQueryPreview : IDisposable
{
    /// <summary>How long typing pauses before the words are scored.</summary>
    internal static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(150);

    private readonly Func<IAIService> _assistant;
    private readonly Func<IPageViewModel?> _page;
    private readonly IUiTimer _pause;
    private string _text = "";

    /// <summary>Counts the changes to the words: a completion asked for under an older count has been overtaken.</summary>
    private int _version;

    /// <param name="ui">The UI thread, which the pause is timed on.</param>
    /// <param name="assistant">The window's workspace's assistant, whose handlers score the words.</param>
    /// <param name="page">The page in front of the user when the words are scored.</param>
    public TypedQueryPreview(IUiThread ui, Func<IAIService> assistant, Func<IPageViewModel?> page)
    {
        _assistant = assistant;
        _page      = page;
        _pause     = ui.CreateTimer(Pause, Score, UiTimerPriority.Background, once: true);
    }

    /// <summary>What the words typed so far point at.</summary>
    public QueryPreview Current { get; private set; } = QueryPreview.None;

    /// <summary>Raised when <see cref="Current"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The words in the bar are now <paramref name="text"/>: what they point at is worked out once typing pauses. A completion
    /// offered for the words before no longer fits them, and goes at once.</summary>
    public void Update(string text)
    {
        _version++;
        _text = text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _pause.Stop();
            Show(QueryPreview.None);
            return;
        }
        Show(Current with { Completion = null });
        _pause.Restart();
    }

    /// <summary>Stops working out what the words point at — something else fills the bar, as speech does — leaving <see cref="Current"/> as it is.</summary>
    public void Cancel()
    {
        _version++;
        _pause.Stop();
    }

    public void Dispose() => _pause.Dispose();

    private void Score()
    {
        var text    = _text;
        var version = _version;
        var page    = _page();
        var (_, winner, effective, prefixed) = _assistant().ScoreHandlers(text, page);

        Show(winner?.Symbol is { } symbol ? new QueryPreview(symbol, Listening: false, Completion: null)
                                          : new QueryPreview(null, Listening: true, Completion: null));

        // Only the handler that clearly owns the words offers a completion.
        if (winner is not null) _ = CompleteAsync(winner, effective, prefixed, page, version);
    }

    private async Task CompleteAsync(IQueryHandler handler, string effective, bool prefixed, IPageViewModel? page, int version)
    {
        string? completion;
        try { completion = await handler.CompleteAsync(effective, prefixed, page); }
        catch (Exception) { return; }   // a handler that cannot complete the words offers nothing

        if (version != _version) return;   // the words changed while it worked
        Show(Current with { Completion = string.IsNullOrEmpty(completion) ? null : completion });
    }

    private void Show(QueryPreview preview)
    {
        if (preview == Current) return;
        Current = preview;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
