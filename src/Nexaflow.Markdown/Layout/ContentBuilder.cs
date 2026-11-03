using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout;

/// <summary>How a builder sets the raw characters of source it could not read: their face, their size, and their ink.</summary>
public sealed record LayoutCharacters(LayoutFace Face, double Size, LayoutInk Ink);

/// <summary>
/// What hosts content being laid out, said to its builder beside the content itself: where the content stands, what lays out
/// whatever it holds written in another language, and what the host found the content to name — the pictures its parts point at,
/// and how its links are to look. The one thing a builder asks of whatever shows it.
/// </summary>
/// <param name="Standing">Where the content stands in what holds it.</param>
/// <param name="Nesting">What lays out content written in another language inside it.</param>
public sealed record LayoutHost(LayoutStanding Standing, LayoutNesting Nesting)
{
    /// <summary>The picture a part names, as the host found it — null where it found none.</summary>
    public Func<ContentPart, LayoutPicture?> Pictures { get; init; } = static _ => null;

    /// <summary>How the host wants a link to look — null where it says nothing.</summary>
    public Func<ContentPart, LayoutLinkLook?> Links { get; init; } = static _ => null;

    /// <summary>
    /// What the stretch being shown as written (<see cref="EditState.Raw"/>) said before the reader began the step they are writing in
    /// it, as the host's undo history has it — null where it has nothing to say.
    /// </summary>
    public Func<string?> ShownBefore { get; init; } = static () => null;

    /// <summary>
    /// What the reader's folding says of one of the blocks of the content laid — hidden in a section folded away, or the heading of
    /// one — as the outline of what the host shows says (<see cref="ContentOutline"/>); nothing, for content laid inside another.
    /// </summary>
    public Func<ContentPart, LayoutFold> Folds { get; init; } = static _ => default;

    /// <summary>
    /// What of the content's height the reader reaches, and the places in it that must stand laid besides, for a builder that can lay part
    /// of what it reads and leave room for the rest — null where all of it is to be laid. A builder that heeds it says what it laid
    /// (<see cref="Laid.Reached"/>).
    /// </summary>
    public LayoutReach? Reach { get; init; }

    /// <summary>
    /// Whether a line too long for the room carries on in rows beneath — for text, a page's source read as a page or a text fence in its
    /// box, rather than code, whose lines are kept. A builder that heeds it is as tall as the rows it breaks its lines onto.
    /// </summary>
    public bool WrapsLines { get; init; }

    /// <summary>
    /// Whether what a holder holds written in another language is set as a page sets what it shows as written — in the same box, laid in
    /// the room inside it — rather than drawing itself, as pictures do: text's way and code's, asked of a fence before it is laid.
    /// </summary>
    public Func<ContentPart, bool> BoxedAsWritten { get; init; } = static _ => false;
}

/// <summary>
/// What the reader's folding says of one of the blocks a builder lays: that it stands in a section folded away and is not laid, that it
/// heads one — with how many blocks wait behind it — or neither.
/// </summary>
/// <param name="Hidden">Whether it stands in a section folded away.</param>
/// <param name="Folded">For the heading of a section folded away, the name it answers to — what opening it again asks by.</param>
/// <param name="Behind">For such a heading, how many blocks wait behind it.</param>
public readonly record struct LayoutFold(bool Hidden, string? Folded = null, int Behind = 0);

/// <summary>
/// What every builder onto the renderer-free layout tree has in common, which is one promise: <strong>laying something out always
/// produces a layout.</strong> What the reader meant, where it can be read; the source shown as its own characters where it cannot; and
/// the same again with the reason attached where the attempt threw — a builder is reading text somebody is in the middle of typing, so
/// it is handed nonsense continually, and "the content vanished" is never the right way to say so.
///
/// <para>
/// The promise is kept here rather than asked of each builder, because a promise each builder makes for itself is one each builder can
/// break: <see cref="Build"/> is theirs and may return null or throw; <see cref="Lay"/> is not theirs, and cannot.
/// </para>
/// <para>
/// A builder is handed content that has already been read and worked over, and the medium it is laid out in, and turns the one into a
/// layout made in the other. It owns nothing and outlives nothing: reading the source is the parser's, what the source means together is
/// the pipeline's, and what happens when somebody types into the picture is the host's.
/// </para>
/// </summary>
public abstract class ContentBuilder
{
    private readonly LayoutHost _host;

    /// <param name="reading">The content, read and worked over before it ever got here.</param>
    /// <param name="state">What is being written, and where — see <see cref="State"/>.</param>
    /// <param name="style">What it is drawn in, said in the medium's own handles.</param>
    /// <param name="isReadOnly">Whether the block is only being looked at.</param>
    /// <param name="medium">What it is laid out in — what shapes its words and makes its outlines.</param>
    /// <param name="host">What hosts it: where it stands, what lays out what it holds in another language, and what was found for it.</param>
    protected ContentBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                     LayoutHost host)
    {
        Reading = reading;
        State = state;
        Style = style;
        IsReadOnly = isReadOnly;
        Medium = medium;
        _host = host;
    }

    /// <summary>Where the content stands in what holds it — on its own, as a block among a document's, or in a line of words.</summary>
    protected LayoutStanding StandsAs => _host.Standing;

    /// <summary>The picture <paramref name="part"/> names, as the host found it — or null where it found none.</summary>
    protected LayoutPicture? PictureOf(ContentPart part) => _host.Pictures(part);

    /// <summary>How the host wants the link <paramref name="part"/> is to look — or null where it says nothing.</summary>
    protected LayoutLinkLook? LinkOf(ContentPart part) => _host.Links(part);

    /// <summary>
    /// What the stretch being shown as written said before the step being written in it began — so what it sizes can stand still while
    /// it is typed — or null where the host has nothing to say.
    /// </summary>
    protected string? ShownBefore => _host.ShownBefore();

    /// <summary>What the reader's folding says of the block <paramref name="part"/> (<see cref="LayoutHost.Folds"/>).</summary>
    protected LayoutFold FoldOf(ContentPart part) => _host.Folds(part);

    /// <summary>The content this is laying out, read and worked over before it ever got here.</summary>
    protected ContentReading Reading { get; }

    /// <summary>
    /// What is being written, and where. A builder reads one thing from it — the stretch being shown as its own characters rather than
    /// as what it says, because somebody is typing in it.
    /// </summary>
    protected EditState State { get; }

    /// <summary>What this showing of the content is drawn in.</summary>
    protected LayoutStyle Style { get; }

    /// <summary>Whether the block is only being looked at.</summary>
    protected bool IsReadOnly { get; }

    /// <summary>Whether somebody is writing in the block rather than only reading it — content being written draws what is still to be written.</summary>
    protected bool Writing => !IsReadOnly;

    /// <summary>What the layout is made in.</summary>
    protected ILayoutMedium Medium { get; }

    /// <summary>
    /// How wide it may be laid out, in the content's own units — infinity where nothing says. The one size a builder is told, because
    /// where a line breaks is a layout decision and only what measures the text can make it.
    /// </summary>
    protected double Room { get; private set; } = double.PositiveInfinity;

    /// <summary>What of the content the reader reaches, where the host asks for only that much to be laid (<see cref="LayoutHost.Reach"/>) — null where it asks for all of it.</summary>
    protected LayoutReach? ReaderReach => _host.Reach;

    /// <summary>Whether the host asks for a line too long for the room to carry on in rows beneath (<see cref="LayoutHost.WrapsLines"/>).</summary>
    protected bool WrapsLines => _host.WrapsLines;

    /// <summary>Lays the source out. Never null, and never throws.</summary>
    public Laid Lay(double room = double.PositiveInfinity)
    {
        Room = Within(double.IsNaN(room) || room <= 0 ? double.PositiveInfinity : room);

        try
        {
            return Build() ?? AsSource([]);
        }
        catch (Exception error)
        {
            // The message is the reader's, so it says what happened to their content rather than which method threw. The part blamed
            // is the whole tree, because a builder that fell over has no opinion about which part of it was to blame.
            return AsSource([(Reading.Root, string.Format(CultureInfo.CurrentCulture, Style.Says("Text.Content.CouldNotSet"), error.Message))]);
        }
    }

    /// <summary>
    /// How much of the room it is handed this content is laid out within. Most take all of it; a plot keeps to a panel a reader can
    /// take in, and a symbol is drawn at the size its modules say whatever room it lands in.
    /// </summary>
    protected virtual double Within(double room) => room;

    /// <summary>
    /// What <paramref name="holder"/> holds written in another language, laid out at <paramref name="room"/> standing as
    /// <paramref name="standing"/> says, or null where nothing read it — the one thing a builder asks of whatever hosts it.
    /// </summary>
    protected LayoutInset? Nested(ContentPart holder, double room, LayoutStanding standing) => _host.Nesting(holder, room, standing);

    /// <summary>Whether what <paramref name="holder"/> holds is set in the box a page sets what it shows as written in (<see cref="LayoutHost.BoxedAsWritten"/>).</summary>
    protected bool BoxedAsWritten(ContentPart holder) => _host.BoxedAsWritten(holder);

    /// <summary>
    /// Lay the content out, or null where none of it is this builder's to draw. Null is for "nothing here is mine to draw", not for
    /// trouble: content that is partly drawable comes back as a layout with trouble on it.
    /// </summary>
    protected abstract Laid? Build();

    /// <summary>How this content sets raw characters, for the source it could not read.</summary>
    protected abstract LayoutCharacters Characters { get; }

    /// <summary>
    /// The block as it is written, each part blamed standing over its own characters and why written beneath — what a builder shows for
    /// what it cannot draw. It names parts of the tree it was given, never characters.
    /// </summary>
    protected Laid AsSource(IReadOnlyList<(ContentPart Part, string Reason)> blamed) => AsSource(blamed, Room);

    /// <summary>The same, wrapped to <paramref name="room"/> rather than to all the room this content was given.</summary>
    protected Laid AsSource(IReadOnlyList<(ContentPart Part, string Reason)> blamed, double room)
    {
        var characters = Characters;
        return SourceShown.Lay(Reading.Root, blamed, Medium, characters.Face, characters.Size, characters.Ink, Style, room);
    }

    /// <summary>The whole block as it is written, and why none of it could be drawn.</summary>
    protected Laid AsSource(string reason) => AsSource([(Reading.Root, reason)]);
}
