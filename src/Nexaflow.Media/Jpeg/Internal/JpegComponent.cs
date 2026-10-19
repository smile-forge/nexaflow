/*
 * Ported from libjpeg-turbo 3.1.1, src/jpeglib.h (jpeg_component_info):
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1991-1998, Thomas G. Lane.
 * Modified 2002-2009 by Guido Vollbeding.
 * Lossless JPEG Modifications:
 * Copyright (C) 1999, Ken Murchison.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2009-2011, 2013-2014, 2016-2017, 2020, 2022-2024, D. R. Commander.
 * Copyright (C) 2015, Google, Inc.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// One colour component of the frame — libjpeg's <c>jpeg_component_info</c> — with the whole image's coefficients
/// (lossy) or samples (lossless) for it.
/// </summary>
internal sealed class JpegComponent
{
    /// <summary>The component's position in the SOF marker.</summary>
    public int Index;

    /// <summary>Its identifier in the SOF marker, which SOS markers name it by.</summary>
    public int Id;

    public int HSamp, VSamp, QuantTableNo, DcTableNo, AcTableNo;

    /// <summary>Its size in data units — 8x8 blocks lossy, samples lossless — counting only the image's own.</summary>
    public int WidthInBlocks, HeightInBlocks;

    /// <summary>Its size in samples at its own sampling.</summary>
    public int DownsampledWidth, DownsampledHeight;

    // Set for each scan the component is in (per_scan_setup).
    public int McuWidth, McuHeight, McuBlocks, LastColWidth, LastRowHeight;

    /// <summary>The quantization table, latched from the first scan the component is in; null until then.</summary>
    public ushort[]? QuantTable;

    /// <summary>
    /// The coefficient (or lossless sample) array's size in data units: the component's own, padded to a whole number
    /// of its sampling factors, as libjpeg pads the full-image buffer.
    /// </summary>
    public int BlocksWide, BlocksHigh;

    /// <summary>
    /// Lossy: 64 coefficients a block, blocks row by row from <see cref="FirstBlockRow"/>, in natural order, not
    /// dequantized — the whole image, or the band being decoded.
    /// </summary>
    public short[] Coefficients = [];

    /// <summary>The block row <see cref="Coefficients"/> begins at.</summary>
    public int FirstBlockRow;

    /// <summary>The component's samples, <see cref="PlaneStride"/> a row: the IDCT's output, or lossless the samples.</summary>
    public byte[] Plane = [];

    public int PlaneStride;
}
