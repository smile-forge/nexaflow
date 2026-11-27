using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

using System.Windows.Media.Imaging;
using Nexaflow.Interaction;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Visuals.Common.Dropping;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What was written here, so it can be taken back; and the clipboard, which is the application's.
///
/// <para>
/// <strong>The history is the engine's.</strong> An edit is whatever the language the caret was in made of a key, and what
/// it came to is a document — so a step back is that document as it was, put back and read again. This control only says
/// where it can be asked for (<see cref="EditHistory"/>).
/// </para>
/// <para>
/// <strong>The clipboard is the application's, and what goes on it is the engine's.</strong> The engine says what a copy
/// holds, because what is picked out and the language it was written in are both its. This control asks for that copy to be
/// put somewhere (<see cref="CopyingEvent"/>), and where no host with something to say about what may leave it answers, puts it
/// on the clipboard its window offers (<see cref="ClipboardRouter"/>). A paste reads that clipboard — or is handed what was
/// pasted, as an <see cref="IPasteTarget"/> — and asks what it comes to (<see cref="PastingEvent"/>), a host that makes
/// something of what arrived answering first.
/// </para>
/// </summary>
public sealed partial class NexaTextSurface
{
    /// <summary>What was written here, which a host with buttons for undo and redo asks whether either can be done.</summary>
    public EditHistory History => _engine.History;

    /// <summary>Whether there is anything to take back.</summary>
    public bool CanUndo => _engine.History.CanUndo;

    /// <summary>Whether anything taken back could be written again.</summary>
    public bool CanRedo => _engine.History.CanRedo;

    /// <summary>Takes back the last thing written.</summary>
    public void Undo() => _engine.Undo();

    /// <summary>Writes again what was last taken back.</summary>
    public void Redo() => _engine.Redo();

    /// <summary>
    /// What the engine says happened to what is written, said again to the page. A document loaded in its place is shown from where
    /// a reader starts it; anything else leaves the reader where they are.
    /// </summary>
    private void Written(ContentSourceChange change)
    {
        if (change.Kind != ContentChangeKind.Written && !IsReadOnly && IsKeyboardFocusWithin) _shown.ShowCaret();

        if (change.Kind == ContentChangeKind.Loaded) Settled();
        else Prompted();

        RaiseEvent(new ContentSourceChangedEventArgs(SourceChangedEvent, change) { Source = this });
    }

    // ── The clipboard ───────────────────────────────────────────────────────

    /// <summary>
    /// Raised to put something on the clipboard: what was chosen, as markdown, as plain words and as marked-up text. A host
    /// with its own say over what may leave marks it handled; otherwise it goes on the clipboard the window offers. A cut only
    /// takes the words away once the copy is there.
    /// </summary>
    public static readonly RoutedEvent CopyingEvent = EventManager.RegisterRoutedEvent(
        "Copying", RoutingStrategy.Bubble, typeof(EventHandler<ContentCopyingEventArgs>), typeof(NexaTextSurface));

    /// <summary>
    /// Raised with what was pasted, to be told what it comes to. A host that makes something of it — a picture, a file — says so
    /// and marks it handled; otherwise the words and markdown in it are written.
    /// </summary>
    public static readonly RoutedEvent PastingEvent = EventManager.RegisterRoutedEvent(
        "Pasting", RoutingStrategy.Bubble, typeof(EventHandler<ContentPastingEventArgs>), typeof(NexaTextSurface));

    /// <summary>Raised when something dragged in from elsewhere is let go here, to be told what it comes to. Whoever answers says, or takes it.</summary>
    public static readonly RoutedEvent DroppingEvent = EventManager.RegisterRoutedEvent(
        "Dropping", RoutingStrategy.Bubble, typeof(EventHandler<ContentDroppingEventArgs>), typeof(NexaTextSurface));

    public event EventHandler<ContentCopyingEventArgs> Copying
    {
        add => AddHandler(CopyingEvent, value);
        remove => RemoveHandler(CopyingEvent, value);
    }

    public event EventHandler<ContentPastingEventArgs> Pasting
    {
        add => AddHandler(PastingEvent, value);
        remove => RemoveHandler(PastingEvent, value);
    }

    /// <summary>Raised before anything written here changes, for a host that may refuse it.</summary>
    public static readonly RoutedEvent ChangingEvent = EventManager.RegisterRoutedEvent(
        "Changing", RoutingStrategy.Bubble, typeof(EventHandler<ContentChangingEventArgs>), typeof(NexaTextSurface));

    public event EventHandler<ContentChangingEventArgs> Changing
    {
        add => AddHandler(ChangingEvent, value);
        remove => RemoveHandler(ChangingEvent, value);
    }

    /// <summary>Raised to keep a picture of one block — what its corner's Save asks for. Whoever keeps it marks it handled.</summary>
    public static readonly RoutedEvent BlockSavingEvent = EventManager.RegisterRoutedEvent(
        "BlockSaving", RoutingStrategy.Bubble, typeof(EventHandler<ContentBlockSavingEventArgs>), typeof(NexaTextSurface));

    public event EventHandler<ContentBlockSavingEventArgs> BlockSaving
    {
        add => AddHandler(BlockSavingEvent, value);
        remove => RemoveHandler(BlockSavingEvent, value);
    }

    public event EventHandler<ContentDroppingEventArgs> Dropping
    {
        add => AddHandler(DroppingEvent, value);
        remove => RemoveHandler(DroppingEvent, value);
    }

    /// <summary>
    /// Asks for what is chosen to be put on the clipboard — the whole of it, where nothing is — and says whether it was. What
    /// a copy holds is the engine's to say, because what is chosen and the language it is written in are both its.
    /// </summary>
    public bool CopySelection() => _engine.Copied();

    /// <summary>Copies what is chosen, then takes it away — but only once it has been put somewhere, so nothing is lost.</summary>
    public bool Cut() => _engine.Cut();

    // ── What the engine asks of this ────────────────────────────────────────

    /// <inheritdoc/>
    Task<bool>? IContentEvents.OnCopy(ContentCopy copy)
    {
        var asked = new ContentCopyingEventArgs(CopyingEvent, copy);
        RaiseEvent(asked);

        return asked.Handled ? Task.FromResult(true) : ClipboardRouter.ClipboardFor(this)?.WriteAsync(MarkdownClipboard.Payload(copy));
    }

    /// <inheritdoc/>
    bool IContentEvents.OnPaste()
    {
        // Shown nowhere that offers a clipboard, a host may still answer from what it holds.
        if (ClipboardRouter.ClipboardFor(this) is not { } clipboard) return PasteTaken(ComposedPayload.Empty);

        _ = PasteFromAsync(clipboard);
        return true;
    }

    /// <summary>What the clipboard holds of <see cref="PasteKinds"/>, read and written here. A copied file that cannot be written out is nothing to paste.</summary>
    private async Task PasteFromAsync(IShellClipboard clipboard)
    {
        IDragPayload pasted;
        try { pasted = await clipboard.ReadAsync([.. PasteKinds]); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { return; }

        PasteTaken(pasted);
    }

    /// <summary>
    /// What was pasted, said as words and markdown — by a host that makes something of it (<see cref="PastingEvent"/>), or else
    /// read from it here — and written at the caret. False where it came to nothing.
    /// </summary>
    private bool PasteTaken(IDragPayload pasted)
    {
        var asked = new ContentPastingEventArgs(PastingEvent, pasted);
        RaiseEvent(asked);

        var words    = asked.Handled ? asked.Words    : MarkdownClipboard.ReadPlainText(pasted);
        var markdown = asked.Handled ? asked.Markdown : MarkdownClipboard.ReadBestMarkdown(pasted);
        if (!asked.Handled && string.IsNullOrEmpty(words) && string.IsNullOrEmpty(markdown)) return false;

        _engine.WritePasted(words ?? string.Empty, markdown ?? string.Empty);
        return true;
    }

    /// <inheritdoc/>
    LayoutPicture? IContentEvents.OnPicture(ContentPart block) => CapturePicture(block, Background) is { } picture ? WpfPicture.Of(picture) : null;

    /// <inheritdoc/>
    bool IContentEvents.OnBlockSave(ContentPart block)
    {
        var asked = new ContentBlockSavingEventArgs(BlockSavingEvent, block);
        RaiseEvent(asked);

        return asked.Handled;
    }

    /// <inheritdoc/>
    bool IContentEvents.OnNavigate(string url) => OpenLink(url);

    /// <inheritdoc/>
    bool IContentEvents.OnPage(bool up) => Paged(up);

    /// <inheritdoc/>
    bool IContentEvents.OnBeforeChange(EditState from, EditState to)
    {
        var asked = new ContentChangingEventArgs(ChangingEvent, from, to);
        RaiseEvent(asked);

        return !asked.Refused;
    }
}

/// <summary>Something asked to be put on the clipboard, in every way a reader might want it pasted.</summary>
public sealed class ContentCopyingEventArgs(RoutedEvent routed, ContentCopy copy) : RoutedEventArgs(routed)
{
    /// <summary>What would go on the clipboard.</summary>
    public ContentCopy Copy { get; } = copy;
}

/// <summary>
/// Asked what a paste comes to. <see cref="Payload"/> is what was pasted, taken off the clipboard; a host that makes something
/// of it says what in words and in markdown and marks it handled — a clipboard holds whatever a machine put there, and turning
/// that into something a document can be written from is the host's when it is more than words. Answering with neither, having
/// dealt with it another way, still marks it handled.
/// </summary>
public sealed class ContentPastingEventArgs(RoutedEvent routed, IDragPayload payload) : RoutedEventArgs(routed)
{
    /// <summary>What was pasted.</summary>
    public IDragPayload Payload { get; } = payload;

    /// <summary>What it comes to as plain words.</summary>
    public string? Words { get; set; }

    /// <summary>The same as markdown, where what was on it was marked up.</summary>
    public string? Markdown { get; set; }
}

/// <summary>
/// What is written here about to change. A host that owns the words and will not have them written over sets
/// <see cref="Refused"/>, and nothing is written.
/// </summary>
public sealed class ContentChangingEventArgs(RoutedEvent routed, EditState from, EditState to) : RoutedEventArgs(routed)
{
    /// <summary>What is written now.</summary>
    public EditState From { get; } = from;

    /// <summary>What it would become.</summary>
    public EditState To { get; } = to;

    /// <summary>Set to refuse the change.</summary>
    public bool Refused { get; set; }

    private ContentChange? _change;

    /// <summary>What would be written: the change that makes <see cref="To"/> from <see cref="From"/>, in the offsets of what it is now.</summary>
    public ContentChange Change => _change ??= this.To.Document.ChangesFrom(this.From.Document).Forward;
}

/// <summary>One block asked to be kept as a picture. Whoever keeps it marks it handled.</summary>
public sealed class ContentBlockSavingEventArgs(RoutedEvent routed, ContentPart block) : RoutedEventArgs(routed)
{
    /// <summary>The block a picture is wanted of.</summary>
    public ContentPart Block { get; } = block;
}

/// <summary>
/// Something dragged in from elsewhere and let go, asked what it comes to. Whoever answers says it in words and in markdown,
/// or deals with it another way and marks it handled having said neither.
/// </summary>
public sealed class ContentDroppingEventArgs(RoutedEvent routed, IDragPayload data, Point at) : RoutedEventArgs(routed)
{
    /// <summary>What was dragged, as it was carried.</summary>
    public IDragPayload Data { get; } = data;

    /// <summary>Where it was let go, in the control's own coordinates.</summary>
    public Point At { get; } = at;

    /// <summary>What it comes to as plain words.</summary>
    public string? Words { get; set; }

    /// <summary>The same as markdown, where what was dragged was marked up.</summary>
    public string? Markdown { get; set; }
}
