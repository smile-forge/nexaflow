namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Rich-text fixtures for the RTF viewer: hand-written documents carrying what the viewer has to get right — a centred
/// bold heading, an italic and a coloured run, a character past seven bits written as RTF escapes it, a table and a link;
/// and a letter written the way a word processor writes one — so the default-open route and the drawing are exercised on
/// real documents.
/// </summary>
internal sealed class RtfSamples : ISampleSet
{
    public string SubDirectory => "rtf";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("sample.rtf", SampleRtf),
        SampleFile.Text("letter.rtf", Letter),
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

    /// <summary>
    /// A letter as a word processor writes one: an indent set once and kept for every paragraph after it, a symbol inserted from
    /// Wingdings as a field, a page break, and a last line with no paragraph mark after it.
    /// </summary>
    private const string Letter =
        """
        {\rtf1\ansi\deff0{\fonttbl{\f0 Times New Roman;}{\f1\fcharset2 Wingdings;}}
        \f0\fs24
        \qr January 13, 2026\par
        \par
        \ql Dear Mr. Smith,\par
        \fi720 Thank you for your letter {\field{\*\fldinst SYMBOL 74 \\f "Wingdings" \\s 12}{\fldrslt\f1\fs24}}.\par
        \page
        Sincerely,\par
        Jane Doe\par
        (555) 123-4567
        }
        """;
}
