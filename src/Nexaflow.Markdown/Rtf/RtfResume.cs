namespace Nexaflow.Markdown.Rtf;

/// <summary>
/// Where an RTF reading stood when it stopped between two paragraphs (<see cref="Ast.ContentStretch"/>): everything the
/// document had declared and every group still open, copied — so a reading started from it reads what follows exactly as
/// the reading that stopped would have gone on to.
///
/// <para>
/// Compared by what it says, never by where it was said: two readings stopping in states alike read alike from there on,
/// which is what lets a document read again after an edit stop reading once it is back where it was. How many fields came
/// before is carried and not compared, because it only tells one link from another.
/// </para>
/// </summary>
internal sealed class RtfResume : IEquatable<RtfResume>
{
    public required RtfFontTable Fonts { get; init; }

    public required uint?[] Colours { get; init; }

    public required int CodePage { get; init; }

    public required int DefaultFont { get; init; }

    public required int DefaultStop { get; init; }

    public required int Fields { get; init; }

    /// <summary>The colour being read into the colour table, where one was left part-read.</summary>
    public required (int Red, int Green, int Blue, bool Coloured) Mixing { get; init; }

    /// <summary>Every group still open, the outermost first and the one being read last.</summary>
    public required RtfGroup[] Groups { get; init; }

    /// <summary>The rows' cell definitions, by how deep in tables they are.</summary>
    public required IReadOnlyDictionary<int, RtfCellDefinition[]> Defined { get; init; }

    public required RtfCellDefinition Defining { get; init; }

    public required string Property { get; init; }

    public required bool ShapePictured { get; init; }

    public bool Equals(RtfResume? other) =>
        other is not null
        && CodePage == other.CodePage
        && DefaultFont == other.DefaultFont
        && DefaultStop == other.DefaultStop
        && Mixing == other.Mixing
        && ShapePictured == other.ShapePictured
        && Property == other.Property
        && Defining == other.Defining
        && Colours.AsSpan().SequenceEqual(other.Colours)
        && Groups.Length == other.Groups.Length
        && Groups.Zip(other.Groups).All(pair => pair.First.Alike(pair.Second))
        && Defined.Count == other.Defined.Count
        && Defined.All(row => other.Defined.TryGetValue(row.Key, out var cells) && row.Value.AsSpan().SequenceEqual(cells))
        && Fonts.Alike(other.Fonts);

    public override bool Equals(object? obj) => Equals(obj as RtfResume);

    public override int GetHashCode() => HashCode.Combine(CodePage, DefaultFont, Groups.Length, Colours.Length, Defined.Count);
}
