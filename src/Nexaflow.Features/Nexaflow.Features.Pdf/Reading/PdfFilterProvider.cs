using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig.Filters;

using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// The default filter table with its three unimplemented image codecs filled in. PdfPig ships
/// <c>DCTDecode</c> (JPEG), <c>JBIG2Decode</c> (bilevel scans) and <c>JPXDecode</c> (JPEG 2000) as stubs
/// whose <c>IsSupported</c> is false, so the core stays dependency-free. All three are Nexaflow.Media's decoders,
/// through <see cref="PdfDctFilter"/>, <see cref="PdfJbig2Filter"/> and <see cref="PdfJpxFilter"/>. Without them a
/// scanned document yields no extractable images at all.
/// <para>
/// Written as a decorator rather than by rebuilding the whole name→filter dictionary, so a filter added to
/// a future PdfPig keeps working here instead of silently disappearing from a copied table.
/// </para>
/// </summary>
internal sealed class PdfFilterProvider : IFilterProvider
{
    public static PdfFilterProvider Instance { get; } = new();

    // One instance each serves every document: the DCT and JPX decoders are stateless, and the JBIG2 one keeps only
    // the globals streams lately decoded with, which any page may share.
    private static readonly IFilter Dct   = new PdfDctFilter();
    private static readonly IFilter Jbig2 = new PdfJbig2Filter();
    private static readonly IFilter Jpx   = new PdfJpxFilter();

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
