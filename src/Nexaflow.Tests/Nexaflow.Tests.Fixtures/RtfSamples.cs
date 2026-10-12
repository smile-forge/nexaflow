namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Rich-text fixtures for the RTF viewer: one hand-written document carrying what the viewer has to get right — a
/// centred bold heading, an italic and a coloured run, a character past seven bits written as RTF escapes it, a table
/// and a link — so the default-open route and the drawing are exercised on a real document.
/// </summary>
internal sealed class RtfSamples : ISampleSet
{
    public string SubDirectory => "rtf";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("sample.rtf", SampleRtf),
    ];

    private const string SampleRtf =
        """
        {\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss Arial;}{\f1\froman Times New Roman;}}{\colortbl ;\red192\green0\blue0;}
        \pard\qc\b\fs32 Quarterly report\b0\fs22\par
        \pard The {\i importer} was fixed in {\cf1 alpha42}.\par
        Caf\'e9 prices rose by three percent.\par
        \trowd\cellx2400\cellx4800 Region\cell Sales\cell\row
        \trowd\cellx2400\cellx4800 North\cell 120\cell\row
        \pard See {\field{\*\fldinst HYPERLINK "https://example.com"}{\fldrslt the site}}.\par
        }
        """;
}
