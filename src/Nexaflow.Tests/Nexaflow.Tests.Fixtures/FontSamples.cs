namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Font fixtures for the Font viewer: a code-generated minimal-but-valid TrueType font and its WOFF
/// wrapping (same font, zlib-compressed), so the viewer's default-open route and the WOFF decode path
/// are both exercised end-to-end. Both are byte-exact <see cref="SampleFile.Raw"/> samples built by
/// <see cref="MinimalFont"/> — no <c>Random</c>, no external tooling, no third-party font bytes.
/// </summary>
internal sealed class FontSamples : ISampleSet
{
    public string SubDirectory => "font";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Raw("nexaflow-test.ttf", MinimalFont.BuildTtf()),
        SampleFile.Raw("nexaflow-test.woff", MinimalFont.BuildWoff()),
    ];
}
