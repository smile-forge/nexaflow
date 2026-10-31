namespace Nexaflow.Markdown.Ast;

/// <summary>
/// How a reading is read: the parse its units are read by, how far a unit is read before it looks for somewhere to stop, and what its
/// language says about a body written inside it in another language when something is written inside that.
/// </summary>
/// <param name="Parse">The windowed parse every unit is read by (<see cref="ContentStretch"/>).</param>
/// <param name="SoftMaxLength">
/// How far a unit is read before it looks for somewhere to stop — <see cref="int.MaxValue"/> for a reading read as one unit.
/// </param>
/// <param name="Bounds">What the language says about a body it holds when something is written inside it, or null where it says nothing.</param>
public sealed record ContentReader(Func<ContentStretch, ContentParse> Parse, int SoftMaxLength, IBodyBounds? Bounds = null);

/// <summary>
/// The reading of a body written in another language inside a reading — a fence's code, the markdown in a fence, a label in a diagram in
/// that markdown: read from its own characters (<see cref="ContentSpace"/>) by its own language, cut into units of its own, and holding
/// the readings of whatever is written inside it in turn. Nothing about depth is special.
///
/// <para>
/// <strong>Attached to the piece holding it</strong> — kept on the unit of the reading holding it, at the place the holder starts in that
/// unit (<see cref="ContentUnit.Bodies"/>) — so it is found from the holder rather than by what it says, and goes wherever the unit goes.
/// A change written somewhere else leaves it as it is; one written inside it is written into it (<see cref="Edited"/>), reading again
/// only the units of its own the change reached; and where the reading holding it is read again round it, it is carried to the holder
/// that comes back in the same language at the same place, the change mapped through (<see cref="ContentUnits.Edited"/>).
/// </para>
/// <para>
/// <strong>Immutable</strong>, as units are: a change hands back another, sharing every unit it did not read.
/// </para>
/// </summary>
public sealed class ContentBody
{
    private ContentBody(string language, ContentSpace space, ContentUnits units, int lead, int width, ContentReader reader)
    {
        this.Language = language;
        this.Space = space;
        this.Units = units;
        this.Lead = lead;
        this.Width = width;
        this.Reader = reader;
    }

    /// <summary>The word the holder names its language by.</summary>
    public string Language { get; }

    /// <summary>The body's own characters, and where they stand in the holder's.</summary>
    public ContentSpace Space { get; }

    /// <summary>What it read to, a unit at a time — all of it.</summary>
    public ContentUnits Units { get; }

    /// <summary>Where the body starts, counted from where its holder starts.</summary>
    public int Lead { get; }

    /// <summary>How many of the holder's characters the body stands for, the line ending closing its last line included.</summary>
    public int Width { get; }

    /// <summary>How it is read.</summary>
    public ContentReader Reader { get; }

    /// <summary>What it read to, as one tree in its own characters' offsets.</summary>
    public ContentNode Tree => this.Units.Tree(this.Space);

    /// <summary>
    /// The body standing <paramref name="lead"/> characters past where its holder starts and <paramref name="width"/> long, its own
    /// characters <paramref name="space"/>, read whole by <paramref name="reader"/> in the language called <paramref name="language"/>.
    /// </summary>
    public static ContentBody Read(string language, ContentSpace space, int lead, int width, ContentReader reader) =>
        new(language, space, ReadOn(new ContentUnits(language), space, reader), lead, width, reader);

    /// <summary>
    /// The body once its own characters from <paramref name="from"/> to <paramref name="to"/> are written over, moving those after along
    /// by <paramref name="by"/>: <paramref name="space"/> its own characters as they are now, standing <paramref name="lead"/> past where
    /// its holder starts and <paramref name="width"/> long — read again only where the change reached, and everything it holds carried
    /// with it.
    /// </summary>
    public ContentBody Edited(int from, int to, int by, ContentSpace space, int lead, int width)
    {
        var (units, _, _) = this.Units.Edited(from, to, by, space, this.Reader.Parse, this.Reader.SoftMaxLength, bounds: this.Reader.Bounds);

        return new(this.Language, space, ReadOn(units, space, this.Reader), lead, width, this.Reader);
    }

    /// <summary>The same body holding <paramref name="body"/> at <paramref name="at"/>, a holder's place among its own characters.</summary>
    public ContentBody Holding(int at, ContentBody body) =>
        new(this.Language, this.Space, this.Units.Holding(at, body), this.Lead, this.Width, this.Reader);

    /// <summary>
    /// The reading of the body held by the holder starting at <paramref name="at"/> among this body's own characters, read by
    /// <paramref name="reader"/> — and this body holding it; or null where nothing starting there holds a body in
    /// <paramref name="language"/>.
    /// </summary>
    public (ContentBody Holding, ContentBody Body)? Reading(int at, string language, ContentReader reader) =>
        this.Units.Reading(at, language, reader, this.Space) is var (units, body)
            ? (new ContentBody(this.Language, this.Space, units, this.Lead, this.Width, this.Reader), body)
            : null;

    /// <summary><paramref name="units"/> read on to the end of <paramref name="space"/>.</summary>
    private static ContentUnits ReadOn(ContentUnits units, ContentSpace space, ContentReader reader)
    {
        while (!units.Done) units = units.ReadOn(reader.Parse, space, reader.SoftMaxLength);

        return units;
    }
}
