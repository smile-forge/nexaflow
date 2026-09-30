using System.Windows.Media.Imaging;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The whole of the way out of the engine: what it asks of whoever is showing the content, one question per member, each
/// answered or not. Where nobody answers, the engine does whatever it does with nothing.
///
/// <para>
/// <strong>The engine raises no events.</strong> It is not a WPF object, and it has no window, no clipboard and no file
/// dialog. Whatever shows the content is all of those, so it implements this and turns each question into an event a host can
/// subscribe to (<see cref="MarkdownSurface"/>). Nothing the engine needs from outside goes any other way.
/// </para>
/// <para>
/// <strong>Asking is not deciding.</strong> What a copy holds, what a paste comes to, whether a change may be made at all —
/// each of those is worked out in the engine, which holds what is picked out, the caret and the language every part of the
/// content is written in. A host says only what a host can: put this somewhere, hand me what is on the clipboard, draw this,
/// turn a page, or refuse.
/// </para>
/// </summary>
public interface IContentEvents
{
    /// <summary>A copy asked to be put on the clipboard, in every form it might be pasted as. True where somebody did.</summary>
    bool OnCopy(MarkdownClipboard.ContentCopy copy);

    /// <summary>
    /// A link to be followed, where it leads out of the content. True where somebody followed it. A link within the same
    /// content is scrolled to without anybody being asked, and a language that answers a press on its own links is asked
    /// before this is.
    /// </summary>
    bool OnNavigate(string url);

    /// <summary>
    /// What is on the clipboard, taken off it and said as plain words and as markdown — the translating is the host's, because
    /// a clipboard holds whatever put something there. Null where nobody answered; both empty where somebody did and there was
    /// nothing here to write.
    /// </summary>
    (string Words, string Markdown)? OnPaste();

    /// <summary>A picture of <paramref name="block"/>, which only whatever painted it can draw. Null where it cannot be drawn.</summary>
    BitmapSource? OnPicture(ContentPart block);

    /// <summary>
    /// A picture of <paramref name="block"/> asked to be kept — its corner's Save. True where somebody kept it. A file goes
    /// somewhere, and where is nobody's but a host's.
    /// </summary>
    bool OnBlockSave(ContentPart block);

    /// <summary>A page further up or down, which is as tall as whatever shows the content. True where it moved.</summary>
    bool OnPage(bool up);

    /// <summary>
    /// The content about to change, from <paramref name="from"/> to <paramref name="to"/>. False refuses it and the content
    /// stays as it was — for a host that owns what is written and will not have it written over.
    /// </summary>
    bool OnBeforeChange(EditState from, EditState to);

    /// <summary>
    /// What is picked out, every time it changes — a stretch dragged over, a thing pressed, nothing left.
    ///
    /// <para>
    /// Told rather than asked, because a host acts on a selection somewhere else: a method chosen in a class view is brought
    /// into view in the editor beside it, which is a different control and none of this one's business.
    /// </para>
    /// </summary>
    void OnSelect(ContentSelectionChange change);
}
