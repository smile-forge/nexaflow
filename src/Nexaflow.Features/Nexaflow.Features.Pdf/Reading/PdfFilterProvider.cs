using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Filters.Dct.JpegLibrary;
using UglyToad.PdfPig.Filters.Jbig2.PdfboxJbig2;
using UglyToad.PdfPig.Filters.Jpx.OpenJpeg;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// The default filter table with its three unimplemented image codecs filled in. PdfPig ships
/// <c>DCTDecode</c> (JPEG), <c>JBIG2Decode</c> (bilevel scans) and <c>JPXDecode</c> (JPEG 2000) as stubs
/// whose <c>IsSupported</c> is false, and each real decoder lives in its own package so the core stays
/// dependency-free. Without them a scanned document yields no extractable images at all.
/// <para>
/// Written as a decorator rather than by rebuilding the whole name→filter dictionary, so a filter added to
/// a future PdfPig keeps working here instead of silently disappearing from a copied table.
/// </para>
/// </summary>
internal sealed class PdfFilterProvider : IFilterProvider
{
    public static PdfFilterProvider Instance { get; } = new();

    // Stateless decoders, so one instance each serves every document.
    private static readonly IFilter Dct   = new JpegLibraryDctDecodeFilter();
    private static readonly IFilter Jbig2 = new PdfboxJbig2DecodeFilter();
    private static readonly IFilter Jpx   = new OpenJpegJpxDecodeFilter();

    private PdfFilterProvider() { }

    public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary)
        => Substitute(DefaultFilterProvider.Instance.GetFilters(dictionary));

    public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names)
        => Substitute(DefaultFilterProvider.Instance.GetNamedFilters(names));

    public IReadOnlyList<IFilter> GetAllFilters()
        => Substitute(DefaultFilterProvider.Instance.GetAllFilters());

    private static IReadOnlyList<IFilter> Substitute(IReadOnlyList<IFilter> filters)
        => filters.Select(static f => f switch
        {
            DctDecodeFilter   => Dct,
            Jbig2DecodeFilter => Jbig2,
            JpxDecodeFilter   => Jpx,
            _                 => f,
        }).ToList();
}
