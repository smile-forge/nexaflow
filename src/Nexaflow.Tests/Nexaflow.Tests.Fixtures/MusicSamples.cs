namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// ABC notation fixtures — a music file that opens as one engraved block rather than as a document.
///
/// <para>
/// Two tunes, because the two questions differ. <c>tune.abc</c> is a whole one with the header fields an
/// engraver prints, a repeat, beamed runs and a lyric line, so opening it exercises the reading, the
/// engraving and the prose around the score. <c>bare.abc</c> is the smallest thing that is still a tune:
/// four notes and the two fields ABC requires. A file type is worth a fixture at both ends, and the small
/// one is what catches a viewer that only works once there is enough on the page to hide behind.
/// </para>
/// <para>
/// Deliberately no fence and no markdown. That is the point of the file type: what is on disk is ABC, and
/// a round trip through the editor has to give the same bytes back.
/// </para>
/// </summary>
internal sealed class MusicSamples : ISampleSet
{
    public string SubDirectory => "music";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("tune.abc", Tune),
        SampleFile.Text("bare.abc", Bare),
    ];

    private const string Tune =
        """
        X:1
        T:Speed the Plough
        R:reel
        C:Trad.
        O:England
        M:4/4
        L:1/8
        K:G
        |:GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|
        GABc dedB|dedB dedB|c2ec B2dB|A2AG A2:|
        w:one two three four five six sev-en eight
        """;

    private const string Bare =
        """
        X:1
        K:C
        CDEF|
        """;
}
