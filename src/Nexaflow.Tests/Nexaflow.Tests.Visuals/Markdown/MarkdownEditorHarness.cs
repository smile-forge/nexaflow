using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Reflection;
using Nexaflow.Visuals.Text.Markdown;
using System.Linq;
using System.Windows.Media;
using System.Collections.Generic;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Driving a real <see cref="MarkdownSurface"/> that is written in, in a shown (off-screen) window — keys and text raised
/// as WPF raises them, and the clipboard answered as the application answers it.
///
/// <para>
/// Everything is said in terms of the document and an offset into what the host handed it, because that is what a test
/// knows: it wrote the markdown. Put the caret here, type that — nothing about the surface underneath has to be known.
/// </para>
/// </summary>
internal static class MarkdownEditorHarness
{
    /// <summary>
    /// Runs whatever the editor has queued. A heading scrolled to waits for the page to be laid out, so nothing has
    /// happened yet when the call returns. Waiting at a <em>lower</em> priority than the work is what makes this a wait
    /// rather than a race.
    /// </summary>
    public static void Pump() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

    /// <summary>Shows an editor loaded with <paramref name="markdown"/>, runs <paramref name="test"/>, closes it.</summary>
    /// <param name="configure">
    /// Applied before the text is loaded, for the properties that change what the document even is —
    /// <see cref="MarkdownSurface.SingleBlock"/> decides whether the text is framed as maths, so setting it afterwards
    /// would mean loading the text once as the wrong thing.
    /// </param>
    public static void Run(string markdown, Action<MarkdownSurface> test, Action<MarkdownSurface>? configure = null)
    {
        var editor = new MarkdownSurface
        {
            IsReadOnly = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Width = 600,
            Height = 400,
        };
        configure?.Invoke(editor);

        // Shown but never activated. The editor has to be in a shown window for keys to have somewhere to come from, but
        // taking the foreground as well snatches focus from whoever is using the machine.
        var window = new Window
        {
            Width = 640, Height = 480, Content = editor, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = -2000,
        };

        // What the application does for every window, done for this one with a clipboard of its own — so a test that
        // copies does not reach into the machine's.
        window.AddHandler(MarkdownSurface.CopyingEvent, new EventHandler<ContentCopyingEventArgs>((_, e) =>
        {
            Clipboard = e.Data;
            e.Handled = true;
        }));
        window.AddHandler(MarkdownSurface.PastingEvent, new EventHandler<ContentPastingEventArgs>((_, e) =>
        {
            e.Data = Clipboard;
            e.Handled = Clipboard is not null;
        }));

        try
        {
            Clipboard = null;
            window.Show();
            editor.UpdateLayout();
            editor.Markdown = markdown;
            editor.UpdateLayout();

            editor.Focus();
            Keyboard.Focus(editor);

            test(editor);
        }
        finally { window.Close(); }
    }

    /// <summary>What a test's window was last asked to put on the clipboard, and hands back when asked to paste.</summary>
    public static IDataObject? Clipboard { get; set; }

    /// <summary>Raises a genuine keystroke's worth of text on the editor.</summary>
    public static void RaiseTextInput(MarkdownSurface editor, string text)
    {
        var composition = new TextComposition(InputManager.Current, editor, text);
        editor.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice, composition)
        {
            RoutedEvent = TextCompositionManager.TextInputEvent,
        });
    }

    /// <summary>Types <paramref name="text"/> into the editor one character at a time.</summary>
    public static void Type(MarkdownSurface editor, string text)
    {
        foreach (var character in text) RaiseTextInput(editor, character.ToString());
    }

    /// <summary>
    /// Presses <paramref name="key"/> on the editor — a real <see cref="Keyboard.PreviewKeyDownEvent"/>, because Space
    /// and Enter are decided by the key and never reach the document as typed text.
    /// </summary>
    /// <remarks>
    /// Unmodified presses only. <see cref="Keyboard.Modifiers"/> is read from the real keyboard, not carried on the event,
    /// so a synthetic Shift or Ctrl cannot be pressed from in here — anything that turns on a modifier belongs in a UI
    /// journey, where the keys are real.
    /// </remarks>
    public static void RaiseKey(MarkdownSurface editor, Key key)
    {
        var source = PresentationSource.FromVisual(editor)
                     ?? throw new InvalidOperationException("the editor must be in a shown window");

        editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    /// <summary>
    /// Puts the caret <paramref name="at"/> characters into the markdown the editor was handed — which in one block of a
    /// language is that language's own text, not the delimiters the editor put round it to draw it.
    /// </summary>
    public static void PlaceCaret(MarkdownSurface editor, int at) =>
        editor.Shown.TakeCaret(Framing(editor) + Math.Clamp(at, 0, editor.Markdown.Length));

    /// <summary>Where the host's own text starts in what the editor holds.</summary>
    private static int Framing(MarkdownSurface editor) =>
        string.IsNullOrEmpty(editor.SingleBlock) ? 0 : editor.Shown.Markdown.IndexOf('\n') + 1;

    /// <summary>Puts the caret at the start of the <paramref name="block"/>th block of the document.</summary>
    public static void CaretAtStartOf(MarkdownSurface editor, int block) => editor.Shown.TakeCaret(Blocked(editor, block).Start);

    /// <summary>Puts the caret at the end of the <paramref name="block"/>th block of the document.</summary>
    public static void CaretAtEndOf(MarkdownSurface editor, int block)
    {
        var (start, length) = Blocked(editor, block);
        editor.Shown.TakeCaret(start + length);
    }

    /// <summary>Where the <paramref name="index"/>th block of the document is written, its line endings left off.</summary>
    private static (int Start, int Length) Blocked(MarkdownSurface editor, int index)
    {
        var source = editor.Shown.Markdown;
        var blocks = MarkdownBlocks.Split(source);
        var from = 0;

        for (var at = 0; at < blocks.Count; at++)
        {
            var found = source.IndexOf(blocks[at], from, StringComparison.Ordinal);
            Assert.IsTrue(found >= 0, $"block {at} is in the document");

            if (at == index) return (found, blocks[at].TrimEnd('\n', '\r').Length);

            from = found + blocks[at].Length;
        }

        Assert.Fail($"the document has no block {index}");
        return default;
    }

    /// <summary>Whether the editor has the keyboard.</summary>
    public static bool HasKeyboard(MarkdownSurface editor) => editor.IsKeyboardFocusWithin;

    /// <summary>How far down the editor is scrolled, and how much there is to scroll.</summary>
    public static (double Offset, double Extent, double Viewport) Scrolled(MarkdownSurface editor)
    {
        var scroller = Drawn<ScrollViewer>(editor)!;

        return (scroller.VerticalOffset, scroller.ExtentHeight, scroller.ViewportHeight);
    }

    /// <summary>The text the editor is showing, as a reader reads it — every run of words on the page, in order.</summary>
    public static string Showing(MarkdownSurface editor) =>
        string.Join("\n", editor.Shown.Laid.Root.SelfAndDescendants()
            .Where(piece => piece.Words is not null)
            .Select(piece => piece.Words!.Glyphs.Text));

    /// <summary>How tall a line of the document's body text is drawn — what a change of text size shows up as.</summary>
    public static double BodyHeight(MarkdownSurface editor) =>
        editor.Shown.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Kind == MarkdownPieces.Words && piece.Words is { Maps: true })
            .Bounds.Height * editor.Shown.Zoom;

    /// <summary>Sweeps a selection over everything the editor is showing.</summary>
    public static void SelectAll(MarkdownSurface editor) => editor.SelectAll();

    /// <summary>What is picked out in the editor, and whether anything is.</summary>
    public static (bool Any, string Text) Picked(MarkdownSurface editor) =>
        (editor.Shown.Current.HasSelection, editor.Shown.SelectedText);

    /// <summary>The first thing of its kind the editor drew, or null where it drew none.</summary>
    public static T? Drawn<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Drawn<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }

    /// <summary>The <paramref name="index"/>th block of another language in the document — a diagram, a formula, a tune.</summary>
    public static DocumentBlock Block(MarkdownSurface editor, int index = 0) => new(editor, index);

    /// <summary>Every block of another language the document drew, in the order written.</summary>
    internal static IReadOnlyList<ContentPart> Blocks(MarkdownSurface editor) =>
        [.. editor.Shown.Laid.Root.SelfAndDescendants()
            .Select(piece => piece.Part as ContentPart)
            .OfType<ContentPart>()
            .Where(part => part.Kind is MarkdownKinds.Fence or MarkdownKinds.Math or MarkdownKinds.Formula)
            .Distinct()
            .OrderBy(part => part.Start)];
}

/// <summary>
/// One block of another language in a document being written — a diagram, a formula — looked at on its own: its text,
/// the caret in it, what it could not read, and the presses and choices made on it.
///
/// <para>
/// <strong>In the document's own offsets.</strong> A block is laid at the offset its source starts at, so everything it
/// drew names the characters of the document, and the caret, a hole and a word found by searching the source all agree
/// without anything being moved. <see cref="Latex"/> is the block's own text, for a test that wrote nothing else.
/// </para>
/// <para>
/// Found again each time it is asked, by its place among the blocks: an edit reads the whole document again, and the
/// parts it was read into last time are not the ones there now.
/// </para>
/// </summary>
internal sealed class DocumentBlock(MarkdownSurface editor, int index)
{
    private MarkdownElement Element => editor.Shown;

    /// <summary>The part of the document the block was written as, delimiters and all.</summary>
    public ContentPart Holder
    {
        get
        {
            var blocks = MarkdownEditorHarness.Blocks(editor);
            Assert.IsTrue(blocks.Count > index, $"the document drew block {index} of another language");

            return blocks[index];
        }
    }

    /// <summary>Where the block's own text begins in the document.</summary>
    public int Start => Body.Start;

    /// <summary>Where the block's own text ends in the document — before the line ending its closing delimiter stands after.</summary>
    public int End
    {
        get
        {
            var (start, length) = ContentNesting.Own(Body);
            return start + length;
        }
    }

    private ContentPart Body => Holder.Part(Roles.Body) ?? Holder;

    /// <summary>The whole document, as the block's offsets are counted.</summary>
    public string Source => Element.Markdown;

    /// <summary>
    /// Where the text a host would call the block's begins: in one block of a language, the host's own text; in a
    /// document, the block's.
    /// </summary>
    public int Origin => string.IsNullOrEmpty(editor.SingleBlock) ? Start : Element.Markdown.IndexOf('\n') + 1;

    /// <summary>The block's own text — a formula's LaTeX, a diagram's lines — as a host would be handed it.</summary>
    public string Latex => string.IsNullOrEmpty(editor.SingleBlock) ? Element.Markdown.Substring(Start, End - Start) : editor.Markdown;

    /// <summary>What is laid, which a block is pieces of.</summary>
    public Laid Laid => Element.Laid;

    /// <summary>What the block could not read.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics
    {
        get
        {
            var holder = Holder;
            return [.. Element.Laid.Trouble.Where(trouble => trouble.Start >= holder.Start && trouble.Start + trouble.Length <= holder.End)];
        }
    }

    public bool IsReadOnly => Element.IsReadOnly;

    /// <summary>Where the caret is, in the document.</summary>
    public int Caret => Element.Caret;

    /// <summary>Whether the caret is in this block.</summary>
    public bool HasCaret => Element.HasCaret && Caret >= Start && Caret <= End;

    /// <summary>The stretch shown as written, counted from <see cref="Origin"/>.</summary>
    public (int Start, int Length)? ShownAsWritten =>
        Element.ShownAsWritten is { } shown ? (shown.Start - Origin, shown.Length) : null;

    public int SelectionLength => Element.SelectionLength;

    public string SelectedText => Element.SelectedText;

    public IReadOnlyList<(int Start, int Length)> Selection => Element.Selection;

    public void TakeCaret(int offset) => Element.TakeCaret(offset);

    public bool MoveCaret(bool forward, bool extend = false) => Element.MoveCaret(forward, extend);

    public void Select(int start, int length) => Element.Select(start, length);

    /// <summary>Picks out the whole of the block's own text.</summary>
    public void SelectAll() => Element.Restore(Element.Current.Select(Origin, Latex.Length));

    public void BeginPointerSelect(Point at) => Element.BeginPointerSelect(at);

    public void BeginPointerSelect(Point at, ModifierKeys modifiers) => Element.BeginPointerSelect(at, modifiers);

    public void ExtendPointerSelect(Point at) => Element.ExtendPointerSelect(at);

    public void EndPointerSelect() => Element.EndPointerSelect();

    /// <summary>What the pointer is over a point on the page.</summary>
    public Cursor? PointerCursor(Point at) => Element.PointerCursor(at);

    /// <summary>A picture of the block as it is on the page.</summary>
    public System.Windows.Media.Imaging.BitmapSource? Picture(Brush? ground = null) => editor.Picture(Holder, ground);
}
