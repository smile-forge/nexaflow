namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Sample video fixtures for the Video viewer. A real codec-encoded clip can't be produced in this
/// dependency-free library, so this is a structurally-valid but minimal MP4 (an ISO-BMFF <c>ftyp</c>
/// box): enough for the file-type map to route <c>.mp4</c> to the Video viewer and for the viewer to
/// open and fail gracefully (the VM surfaces an error rather than throwing when a clip can't decode).
/// </summary>
public sealed class VideoSamples : ISampleSet
{
    public string SubDirectory => "video";

    public IReadOnlyList<SampleFile> Files =>
    [
        SampleFile.Raw("minimal.mp4", MinimalMp4()),
    ];

    // ISO Base Media 'ftyp' box: size(0x18) + 'ftyp' + major brand 'isom' + minor 0x200 + brands isom/mp41.
    private static byte[] MinimalMp4() =>
    [
        0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
        (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0x00, 0x00, 0x02, 0x00,
        (byte)'i', (byte)'s', (byte)'o', (byte)'m', (byte)'m', (byte)'p', (byte)'4', (byte)'1',
    ];
}
