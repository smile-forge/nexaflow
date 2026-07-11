using System.IO;
using System.IO.Compression;
using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// SVG fixtures for the SVG viewer: a tiny hand-written vector document and its gzip (<c>.svgz</c>)
/// wrapping — the same bytes — so the viewer's default-open route and the gzip decode path are both
/// exercised. The <c>.svgz</c> is byte-exact (<see cref="SampleFile.Raw"/>) so its decode is deterministic;
/// .NET's <see cref="GZipStream"/> writes no timestamp, so the compressed bytes are stable across runs.
/// </summary>
internal sealed class SvgSamples : ISampleSet
{
    public string SubDirectory => "svg";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("sample.svg", SampleSvg),
        SampleFile.Raw("sample.svgz", Gzip(SampleSvg)),
    ];

    private const string SampleSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="120" height="120" viewBox="0 0 120 120">
          <rect x="0" y="0" width="120" height="120" fill="#1e88e5"/>
          <circle cx="60" cy="60" r="40" fill="#ffca28"/>
          <path d="M30 90 L60 30 L90 90 Z" fill="#e53935"/>
        </svg>
        """;

    private static byte[] Gzip(string svg)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(svg.Replace("\r\n", "\n"));
            gz.Write(bytes, 0, bytes.Length);
        }
        return ms.ToArray();
    }
}
