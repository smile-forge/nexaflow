using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What may be done where the pointer is, offered on a right-click: what the pieces under it answer to, what the language
/// drawn there offers, and — this control's own contribution — cutting, copying and pasting, and setting markdown's own
/// words heavy, slanted, struck through, as code, as a heading, a quote or a block of code.
///
/// <para>
/// Formatting is markdown's, so it is offered only where the caret is in markdown's own words: inside a formula a heading
/// means nothing, and inside a diagram a quote would break a line. What it writes is markdown — the marks round what is
/// chosen, or the marker at the start of the block — applied as an edit like any other, so it is one step to take back.
/// </para>
/// </summary>
public sealed partial class MarkdownSurface
{
    /// <summary>The verbs this control answers from its own menu.</summary>
    internal static class Menus
    {
        public const string Cut = "cut";
        public const string Paste = "paste";
        public const string Bold = "bold";
        public const string Italic = "italic";
        public const string Strike = "strike";
        public const string Code = "inline-code";
        public const string Heading1 = "heading-1";
        public const string Heading2 = "heading-2";
        public const string Heading3 = "heading-3";
        public const string Quote = "quote";
        public const string CodeBlock = "code-block";
    }

    private Popup? _ribbon;

    /// <inheritdoc/>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (e.Handled) return;

        e.Handled = ShowRibbon(e.GetPosition(_shown));
    }

    /// <summary>
    /// Shows what may be done at <paramref name="at"/> — a point on the document as drawn — and says whether there was
    /// anything. A press outside what is picked out puts the caret where it landed first, which is what every editor does:
    /// a reader right-clicking a word means that word.
    /// </summary>
    public bool ShowRibbon(Point at)
    {
        Focus();

        var zoom = _shown.Zoom;
        var offset = _shown.Laid.Root.OffsetAt(new Point(at.X / zoom, at.Y / zoom));
        var state = _shown.Current;

        if (!state.HasSelection || !state.Selection.Any(range => offset >= range.Start && offset <= range.End))
            _shown.MoveCaretTo(offset);

        if (_shown.BuildRibbon(at) is not { } ribbon) return false;

        _ribbon ??= new Popup
        {
            StaysOpen = false,
            AllowsTransparency = true,
            Placement = PlacementMode.MousePoint,
            PlacementTarget = this,
        };

        _ribbon.IsOpen = false;
        _ribbon.Child = ribbon;
        _ribbon.IsOpen = true;

        return true;
    }

    /// <summary>What this control adds to the menu: the clipboard, and markdown's own formatting where that means anything.</summary>
    private IEnumerable<LayoutIntent> Offered()
    {
        var chosen = _shown.Current.HasSelection;

        if (!IsReadOnly && chosen) yield return new LayoutIntent(Menus.Cut, null, "Cut");
        if (chosen) yield return new LayoutIntent(LayoutVerbs.Copy);
        if (!IsReadOnly) yield return new LayoutIntent(Menus.Paste, null, "Paste");

        if (IsReadOnly || !InWords()) yield break;

        yield return new LayoutIntent(Menus.Bold, null, "Bold");
        yield return new LayoutIntent(Menus.Italic, null, "Italic");
        yield return new LayoutIntent(Menus.Strike, null, "Strike through");
        yield return new LayoutIntent(Menus.Code, null, "Code");
        yield return new LayoutIntent(Menus.Heading1, null, "Heading 1");
        yield return new LayoutIntent(Menus.Heading2, null, "Heading 2");
        yield return new LayoutIntent(Menus.Heading3, null, "Heading 3");
        yield return new LayoutIntent(Menus.Quote, null, "Quote");
        yield return new LayoutIntent(Menus.CodeBlock, null, "Code block");
    }

    /// <summary>
    /// Does what was chosen from this control's own part of the menu, and says whether it was one of those.
    /// </summary>
    private bool Chose(string verb)
    {
        switch (verb)
        {
            case Menus.Cut: return Cut();
            case LayoutVerbs.Copy: return CopySelection();
            case Menus.Paste: return Paste();

            case Menus.Bold: return Marked("**");
            case Menus.Italic: return Marked("*");
            case Menus.Strike: return Marked("~~");
            case Menus.Code: return Marked("`");

            case Menus.Heading1: return Reblocked(block => MarkdownBlockFormat.SetHeading(block, 1));
            case Menus.Heading2: return Reblocked(block => MarkdownBlockFormat.SetHeading(block, 2));
            case Menus.Heading3: return Reblocked(block => MarkdownBlockFormat.SetHeading(block, 3));
            case Menus.Quote: return Reblocked(block => MarkdownBlockFormat.ToggleLinePrefix(block, "> "));
            case Menus.CodeBlock: return Reblocked(MarkdownBlockFormat.ToggleCodeFence);

            default: return false;
        }
    }

    /// <summary>Puts <paramref name="marks"/> round what is chosen, or either side of the caret with the caret between them.</summary>
    private bool Marked(string marks)
    {
        if (IsReadOnly || !InWords()) return false;

        _shown.Wrap(marks, marks);

        return true;
    }

    /// <summary>Rewrites the block the caret is in as <paramref name="change"/> says — a heading's hashes, a quote's marks, a fence.</summary>
    private bool Reblocked(Func<string, (string Text, int Caret)> change)
    {
        if (IsReadOnly || !InWords() || Holding(_shown.Caret) is not { } block) return false;

        var source = _shown.Markdown;
        var written = source.Substring(block.Start, block.Length).TrimEnd('\n', '\r');
        var (text, caret) = change(written);

        Write(new EditState(source[..block.Start] + text + source[(block.Start + written.Length)..], block.Start + caret));

        return true;
    }

    /// <summary>
    /// Whether the caret is in markdown's own words — a paragraph, a heading, a quote, an item — rather than in another
    /// language's source, code, a table or a document's front matter, where formatting would mean something else or break it.
    /// </summary>
    private bool InWords()
    {
        if (!string.IsNullOrEmpty(SingleBlock) || InFormula()) return false;

        return Holding(_shown.Caret) is not { } block
               || block.Kind is not (MarkdownKinds.Fence or MarkdownKinds.Math or MarkdownKinds.Code or MarkdownKinds.Html
                                     or MarkdownKinds.FrontMatter or MarkdownKinds.Table or MarkdownKinds.Rule);
    }

    /// <summary>The block of the document the caret is in, counting the caret at the end of a block as in it.</summary>
    private ContentPart? Holding(int caret)
    {
        foreach (var block in Read.Children)
            if (!block.Derived && block.Role != Roles.Trivia && caret >= block.Start && caret <= block.End)
                return block;

        return null;
    }
}
