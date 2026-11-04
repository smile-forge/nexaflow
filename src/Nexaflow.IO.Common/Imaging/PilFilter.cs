namespace Nexaflow.IO.Common.Imaging;

/// <summary>The filters PIL resizes 8-bit pictures with, as <see cref="PictureResampling.ResizeAsPil"/> applies them.</summary>
public enum PilFilter
{
    /// <summary>Each output pixel the plain average of the source pixels it covers (<c>Image.BOX</c>).</summary>
    Box,

    /// <summary>A triangle over one source pixel each way (<c>Image.BILINEAR</c>).</summary>
    Bilinear,

    /// <summary>Keys' cubic, a = −0.5, over two (<c>Image.BICUBIC</c>).</summary>
    Bicubic,

    /// <summary>A Lanczos window of 3 (<c>Image.LANCZOS</c>).</summary>
    Lanczos,
}
