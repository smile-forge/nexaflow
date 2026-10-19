/*
 * Ported from libjpeg-turbo 3.1.1, src/jdcoefct.c (the output side), src/jdsample.c, src/jdmainct.c,
 * src/jdcolor.c and src/jdcolext.c:
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1991-1997, Thomas G. Lane.
 * Modified 2002-2011 by Guido Vollbeding.
 * Lossless JPEG Modifications:
 * Copyright (C) 1999, Ken Murchison.
 * libjpeg-turbo Modifications:
 * Copyright 2009 Pierre Ossman <ossman@cendio.se> for Cendio AB
 * Copyright (C) 2009, 2011-2012, 2014-2016, 2022, 2024, D. R. Commander.
 * Copyright (C) 2010, 2015-2016, 2019-2020, 2022-2024, D. R. Commander.
 * Copyright (C) 2013, Linaro Limited.
 * Copyright (C) 2015, 2020, Google, Inc.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

using System.Buffers;

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// The output side: each block's inverse DCT into its component's plane (smoothed first where block smoothing
/// applies), then each output row upsampled from the planes and colour-converted.
/// </summary>
/// <remarks>
/// <para>
/// A single-scan image is streamed. Its MCU rows are entropy-decoded a band at a time into one of two small coefficient
/// buffers, and while the next band decodes, the last one is transformed and its rows converted — on the thread pool,
/// shared out, when the decode is parallel. A multi-scan image needs every scan before any block is complete, so its
/// coefficients are held whole, as libjpeg holds them, and transformed and converted once all are read.
/// </para>
/// <para>
/// libjpeg feeds the upsampler a row group at a time through the main buffer controller, which manufactures the context
/// rows the vertical fancy upsamplers read: above the first row, the first row again; below the last real row, the last
/// real row again. With the planes whole, the same rows are read directly, clamped to the component's real rows; a row
/// is converted only once every plane row it reads is transformed.
/// </para>
/// </remarks>
internal sealed class ImageOutput
{
    private enum Upsampler { Fullsize, H2V1Fancy, H2V1Box, H1V2Fancy, H2V2Fancy, H2V2Box, Integral }

    private enum Converter { Grey, YccToRgb, Interleave, YcckToCmyk }

    /// <summary>Below this many pixels an image is decoded on the calling thread alone.</summary>
    private const long ParallelPixels = 256 * 256;

    /// <summary>The pixels a streamed band aims at: a few hundred thousand, so a band is worth a task.</summary>
    private const long BandPixels = 1 << 18;

    private readonly Decompressor _d;
    private readonly Upsampler[] _upsamplers;
    private readonly int[] _hExpand, _vExpand;
    private readonly Converter _converter;
    private readonly bool _parallel;

    /// <summary>The samples in each output pixel.</summary>
    public readonly int OutputComponents;

    /// <summary>
    /// 3.1.1 - jinit_color_deconverter and jinit_upsampler: the conversion and each component's upsampler chosen, and
    /// what neither can do refused, as libjpeg refuses it.
    /// </summary>
    public ImageOutput(Decompressor d)
    {
        _d = d;
        _parallel = d.Parallel && (long)d.ImageWidth * d.ImageHeight >= ParallelPixels;

        if (d.Lossless)
        {
            if (d.Precision is > 8 or < 2) throw JpegErrors.Fatal(JpegErrors.BadPrecision(d.Precision));
        }
        else if (d.Precision != 8)
        {
            throw JpegErrors.Fatal(JpegErrors.BadPrecision(d.Precision));
        }

        var n = d.NumComponents;
        var valid = d.JpegColorSpace switch
        {
            JpegColourSpace.Grey => n == 1,
            JpegColourSpace.Rgb or JpegColourSpace.YCbCr => n == 3,
            JpegColourSpace.Cmyk or JpegColourSpace.Ycck => n == 4,
            _ => n >= 1,
        };
        if (!valid) throw JpegErrors.Fatal(JpegErrors.BadJColorspace);

        // No lossy colour conversion is allowed in lossless mode.
        if (d.Lossless && d.JpegColorSpace != d.OutColorSpace) throw JpegErrors.Fatal(JpegErrors.ConversionNotImplemented);
        (_converter, OutputComponents) = (d.OutColorSpace, d.JpegColorSpace) switch
        {
            (JpegColourSpace.Grey, JpegColourSpace.Grey or JpegColourSpace.YCbCr) => (Converter.Grey, 1),
            (JpegColourSpace.Rgb, JpegColourSpace.YCbCr) => (Converter.YccToRgb, 3),
            (JpegColourSpace.Rgb, JpegColourSpace.Rgb) => (Converter.Interleave, 3),
            (JpegColourSpace.Cmyk, JpegColourSpace.Ycck) => (Converter.YcckToCmyk, 4),
            (JpegColourSpace.Cmyk, JpegColourSpace.Cmyk) => (Converter.Interleave, 4),
            (JpegColourSpace.Unknown, JpegColourSpace.Unknown) => (Converter.Interleave, n),
            _ => throw JpegErrors.Fatal(JpegErrors.ConversionNotImplemented),
        };

        // jdmainct.c cannot supply context rows when min_DCT_scaled_size is 1, as it is in lossless mode, so there
        // the upsampling is never fancy.
        var fancy = !d.Lossless;
        _upsamplers = new Upsampler[n];
        _hExpand = new int[n];
        _vExpand = new int[n];
        for (var ci = 0; ci < n; ci++)
        {
            var component = d.Components[ci];
            int hIn = component.HSamp, vIn = component.VSamp, hOut = d.MaxHSamp, vOut = d.MaxVSamp;
            if (hIn == hOut && vIn == vOut)
            {
                _upsamplers[ci] = Upsampler.Fullsize;
            }
            else if (hIn * 2 == hOut && vIn == vOut)
            {
                _upsamplers[ci] = fancy && component.DownsampledWidth > 2 ? Upsampler.H2V1Fancy : Upsampler.H2V1Box;
            }
            else if (hIn == hOut && vIn * 2 == vOut && fancy)
            {
                _upsamplers[ci] = Upsampler.H1V2Fancy;
            }
            else if (hIn * 2 == hOut && vIn * 2 == vOut)
            {
                _upsamplers[ci] = fancy && component.DownsampledWidth > 2 ? Upsampler.H2V2Fancy : Upsampler.H2V2Box;
            }
            else if (hOut % hIn == 0 && vOut % vIn == 0)
            {
                _upsamplers[ci] = Upsampler.Integral;
                _hExpand[ci] = hOut / hIn;
                _vExpand[ci] = vOut / vIn;
            }
            else
            {
                throw JpegErrors.Fatal(JpegErrors.FractionalSampling);
            }
        }
    }

    /// <summary>
    /// A single-scan lossy image, decoded band by band: <see cref="Decompressor.DecodeScanRow"/> fills a band's
    /// coefficients, then the band is transformed and its rows converted while the next one decodes.
    /// </summary>
    public byte[] RenderStreamed()
    {
        var d = _d;
        var components = d.Components;
        var output = new byte[checked(d.ImageWidth * d.ImageHeight * OutputComponents)];
        var multipliers = Multipliers();

        var iMcuRowHeight = d.MaxVSamp * 8;
        var bandIMcuRows = (int)Math.Clamp(BandPixels / ((long)d.ImageWidth * iMcuRowHeight), 1, d.TotalIMcuRows);
        var slots = new short[2][][];
        for (var s = 0; s < 2; s++)
        {
            slots[s] = new short[components.Length][];
            for (var ci = 0; ci < components.Length; ci++)
                slots[s][ci] = ArrayPool<short>.Shared.Rent(components[ci].BlocksWide * bandIMcuRows * components[ci].VSamp * 64);
        }

        RentPlanes();
        var progress = new RowProgress(this);
        var bands = (d.TotalIMcuRows + bandIMcuRows - 1) / bandIMcuRows;
        var tasks = new Task[bands];
        var previous = Task.CompletedTask;
        try
        {
            for (var band = 0; band < bands; band++)
            {
                var slot = slots[band & 1];
                if (band >= 2) tasks[band - 2].Wait();

                var firstIMcuRow = band * bandIMcuRows;
                var lastIMcuRow = Math.Min(d.TotalIMcuRows, firstIMcuRow + bandIMcuRows);
                for (var ci = 0; ci < components.Length; ci++)
                {
                    var component = components[ci];
                    Array.Clear(slot[ci], 0, component.BlocksWide * (lastIMcuRow - firstIMcuRow) * component.VSamp * 64);
                    component.Coefficients = slot[ci];
                    component.FirstBlockRow = firstIMcuRow * component.VSamp;
                }
                for (var row = firstIMcuRow; row < lastIMcuRow; row++) d.DecodeScanRow();
                if (band == bands - 1) d.FinishReading();

                var finalBand = band == bands - 1;
                if (_parallel)
                {
                    previous = previous.ContinueWith(prior =>
                    {
                        prior.GetAwaiter().GetResult();
                        TransformBand(slot, multipliers, firstIMcuRow, lastIMcuRow);
                        progress.Advance(lastIMcuRow, finalBand, output);
                    }, TaskScheduler.Default);
                    tasks[band] = previous;
                }
                else
                {
                    TransformBand(slot, multipliers, firstIMcuRow, lastIMcuRow);
                    progress.Advance(lastIMcuRow, finalBand, output);
                    tasks[band] = Task.CompletedTask;
                }
            }
            previous.GetAwaiter().GetResult();
        }
        finally
        {
            // A failed decode leaves its band tasks to finish before the buffers they read go back.
            try { Task.WaitAll(tasks.Where(task => task is not null).ToArray()); } catch (AggregateException) { }
            foreach (var slot in slots)
                foreach (var buffer in slot) ArrayPool<short>.Shared.Return(buffer);
            ReturnPlanes();
        }
        return output;
    }

    /// <summary>A multi-scan lossy image, its coefficients all read: every block transformed, then every row converted.</summary>
    public byte[] RenderBuffered()
    {
        var d = _d;
        var output = new byte[checked(d.ImageWidth * d.ImageHeight * OutputComponents)];
        RentPlanes();
        try
        {
            var smoothing = BlockSmoothing.For(d);
            var multipliers = Multipliers();
            var items = BlockRowItems(0, d.TotalIMcuRows, out var starts);
            ForEach(items, () => new short[64], (item, workspace) =>
            {
                var (ci, blockRow) = Locate(starts, item);
                var component = d.Components[ci];
                if (smoothing is not null)
                    smoothing.Row(component, multipliers[ci], blockRow, workspace);
                else
                    TransformRow(component, component.Coefficients, 0, multipliers[ci], blockRow);
            });
            ConvertRows(0, d.ImageHeight, output);
        }
        finally
        {
            ReturnPlanes();
        }
        return output;
    }

    /// <summary>A lossless image: its samples are already in the planes, so every row is converted.</summary>
    public byte[] RenderLossless()
    {
        var output = new byte[checked(_d.ImageWidth * _d.ImageHeight * OutputComponents)];
        ConvertRows(0, _d.ImageHeight, output);
        return output;
    }

    private short[][] Multipliers()
    {
        var multipliers = new short[_d.NumComponents][];
        for (var ci = 0; ci < multipliers.Length; ci++) multipliers[ci] = InverseDct.Multipliers(_d.Components[ci].QuantTable);
        return multipliers;
    }

    /// <summary>The planes, each the component's own blocks in samples, from the shared pool; the IDCT writes every sample.</summary>
    private void RentPlanes()
    {
        foreach (var component in _d.Components)
        {
            component.PlaneStride = component.WidthInBlocks * 8;
            component.Plane = ArrayPool<byte>.Shared.Rent(checked(component.PlaneStride * component.HeightInBlocks * 8));
        }
    }

    private void ReturnPlanes()
    {
        foreach (var component in _d.Components)
        {
            if (component.Plane.Length > 0) ArrayPool<byte>.Shared.Return(component.Plane);
            component.Plane = [];
        }
    }

    /// <summary>
    /// The work of transforming iMCU rows <paramref name="firstIMcuRow"/> to <paramref name="lastIMcuRow"/>: one item
    /// per real block row of each component, <paramref name="starts"/> holding where each component's items begin.
    /// </summary>
    private int BlockRowItems(int firstIMcuRow, int lastIMcuRow, out int[] starts)
    {
        var components = _d.Components;
        starts = new int[components.Length + 1];
        for (var ci = 0; ci < components.Length; ci++)
        {
            var component = components[ci];
            var first = firstIMcuRow * component.VSamp;
            var last = Math.Min(component.HeightInBlocks, lastIMcuRow * component.VSamp);
            starts[ci + 1] = starts[ci] + Math.Max(0, last - first);
        }
        return starts[^1];
    }

    private static (int Component, int Offset) Locate(int[] starts, int item)
    {
        var ci = 0;
        while (item >= starts[ci + 1]) ci++;
        return (ci, item - starts[ci]);
    }

    /// <summary>A band's blocks, transformed from its coefficient buffers into the planes.</summary>
    private void TransformBand(short[][] slot, short[][] multipliers, int firstIMcuRow, int lastIMcuRow)
    {
        var items = BlockRowItems(firstIMcuRow, lastIMcuRow, out var starts);
        ForEach(items, () => 0, (item, _) =>
        {
            var (ci, offset) = Locate(starts, item);
            var component = _d.Components[ci];
            var firstBlockRow = firstIMcuRow * component.VSamp;
            TransformRow(component, slot[ci], firstBlockRow, multipliers[ci], firstBlockRow + offset);
        });
    }

    /// <summary>One block row of a component: each of its real blocks through the IDCT into the plane.</summary>
    private static void TransformRow(JpegComponent component, short[] coefficients, int firstBlockRow, short[] multipliers, int blockRow)
    {
        var plane = component.Plane.AsSpan();
        var stride = component.PlaneStride;
        var source = coefficients.AsSpan((blockRow - firstBlockRow) * component.BlocksWide * 64);
        var target = blockRow * 8 * stride;
        for (var bx = 0; bx < component.WidthInBlocks; bx++)
            InverseDct.Transform(source.Slice(bx * 64, 64), multipliers, plane[(target + bx * 8)..], stride);
    }

    /// <summary>Runs <paramref name="body"/> over items 0 to <paramref name="count"/>, shared out when the decode is parallel.</summary>
    private void ForEach<T>(int count, Func<T> local, Action<int, T> body)
    {
        if (_parallel && count > 1)
        {
            Parallel.For(0, count, local, (item, _, state) =>
            {
                body(item, state);
                return state;
            }, _ => { });
            return;
        }

        var state = local();
        for (var item = 0; item < count; item++) body(item, state);
    }

    /// <summary>
    /// Which output rows a streamed image can convert so far: those whose every plane row — the context rows a vertical
    /// fancy upsampler reads included — is transformed.
    /// </summary>
    private sealed class RowProgress(ImageOutput output)
    {
        private int _converted;

        public void Advance(int iMcuRowsDone, bool final, byte[] target)
        {
            var d = output._d;
            var height = d.ImageHeight;
            var limit = final ? height : _converted;
            if (!final)
            {
                while (limit < height && Ready(limit, iMcuRowsDone)) limit++;
            }
            if (limit <= _converted) return;
            output.ConvertRows(_converted, limit, target);
            _converted = limit;
        }

        private bool Ready(int y, int iMcuRowsDone)
        {
            var d = output._d;
            for (var ci = 0; ci < d.NumComponents; ci++)
            {
                var component = d.Components[ci];
                var rowsDone = Math.Min(component.HeightInBlocks, iMcuRowsDone * component.VSamp) * 8;
                var needed = output._upsamplers[ci] switch
                {
                    Upsampler.H1V2Fancy or Upsampler.H2V2Fancy => Math.Min((y >> 1) + 1, component.DownsampledHeight - 1),
                    Upsampler.H2V2Box => y >> 1,
                    Upsampler.Integral => y / output._vExpand[ci],
                    _ => y,
                };
                if (needed >= rowsDone) return false;
            }
            return true;
        }
    }

    /// <summary>Output rows <paramref name="from"/> to <paramref name="to"/>, upsampled and converted, in bands shared out.</summary>
    private void ConvertRows(int from, int to, byte[] output)
    {
        const int rowsPerBand = 16;
        var bands = (to - from + rowsPerBand - 1) / rowsPerBand;
        ForEach(bands, () => new RowBuffers(this), (band, buffers) =>
        {
            var first = from + band * rowsPerBand;
            RenderRows(first, Math.Min(to, first + rowsPerBand), output, buffers);
        });
    }

    /// <summary>A worker's rows of upsampled samples, one per component that needs upsampling.</summary>
    private sealed class RowBuffers
    {
        public readonly byte[][] Rows;

        /// <summary>A fancy upsampler's row of 16-bit sums, its edges repeated.</summary>
        public readonly short[] Sums;

        public readonly byte[][] Sources;
        public readonly int[] Offsets;

        public RowBuffers(ImageOutput output)
        {
            var d = output._d;
            var width = Decompressor.RoundUp(d.ImageWidth, d.MaxHSamp) + 2;
            Rows = new byte[d.NumComponents][];
            for (var ci = 0; ci < Rows.Length; ci++)
                Rows[ci] = output._upsamplers[ci] == Upsampler.Fullsize ? [] : new byte[width];
            Sums = new short[width + 2];
            Sources = new byte[d.NumComponents][];
            Offsets = new int[d.NumComponents];
        }
    }

    private void RenderRows(int from, int to, byte[] output, RowBuffers buffers)
    {
        var d = _d;
        var width = d.ImageWidth;
        var n = d.NumComponents;
        var rowBytes = width * OutputComponents;
        var sources = buffers.Sources;
        var offsets = buffers.Offsets;

        for (var y = from; y < to; y++)
        {
            for (var ci = 0; ci < n; ci++)
            {
                if (_upsamplers[ci] == Upsampler.Fullsize)
                {
                    sources[ci] = d.Components[ci].Plane;
                    offsets[ci] = y * d.Components[ci].PlaneStride;
                }
                else
                {
                    Upsample(ci, y, buffers.Rows[ci], buffers.Sums);
                    sources[ci] = buffers.Rows[ci];
                    offsets[ci] = 0;
                }
            }

            Convert(sources, offsets, output.AsSpan(y * rowBytes, rowBytes));
        }
    }

    /// <summary>
    /// jdsample.c's per-component upsamplers, for output row <paramref name="y"/>: the fancy ones by triangle
    /// interpolation with alternating rounding, the others by replication.
    /// </summary>
    private void Upsample(int ci, int y, byte[] row, short[] sums)
    {
        var component = _d.Components[ci];
        var plane = component.Plane;
        var stride = component.PlaneStride;
        var dsw = component.DownsampledWidth;
        var lastRow = component.DownsampledHeight - 1;
        var width = _d.ImageWidth;

        switch (_upsamplers[ci])
        {
            case Upsampler.H2V1Fancy:
                RowKernels.H2V1Fancy(plane.AsSpan(y * stride, dsw), dsw, row, sums);
                break;

            case Upsampler.H2V1Box:
            {
                // 3.1.1 - h2v1_upsample
                var input = plane.AsSpan(y * stride);
                for (int o = 0, i = 0; o < width; o += 2, i++) row[o] = row[o + 1] = input[i];
                break;
            }

            case Upsampler.H1V2Fancy:
            {
                // 3.1.1 - h1v2_fancy_upsample: the nearer row 3/4, the other 1/4, rounding down above and up below.
                var r = y >> 1;
                var below = (y & 1) != 0;
                var near = plane.AsSpan(r * stride, dsw);
                var far = plane.AsSpan((below ? Math.Min(r + 1, lastRow) : Math.Max(r - 1, 0)) * stride, dsw);
                RowKernels.H1V2Fancy(near, far, dsw, below ? 2 : 1, row);
                break;
            }

            case Upsampler.H2V2Fancy:
            {
                // 3.1.1 - h2v2_fancy_upsample: the nearer row 3/4, the other 1/4, in each direction.
                var r = y >> 1;
                var below = (y & 1) != 0;
                var near = plane.AsSpan(r * stride, dsw);
                var far = plane.AsSpan((below ? Math.Min(r + 1, lastRow) : Math.Max(r - 1, 0)) * stride, dsw);
                RowKernels.H2V2Fancy(near, far, dsw, row, sums);
                break;
            }

            case Upsampler.H2V2Box:
            {
                // 3.1.1 - h2v2_upsample
                var input = plane.AsSpan((y >> 1) * stride);
                for (int o = 0, i = 0; o < width; o += 2, i++) row[o] = row[o + 1] = input[i];
                break;
            }

            default:
            {
                // 3.1.1 - int_upsample: any integral ratio, by replication.
                var hExpand = _hExpand[ci];
                var input = plane.AsSpan(y / _vExpand[ci] * stride);
                for (int o = 0, i = 0; o < width; i++)
                {
                    var invalue = input[i];
                    for (var h = 0; h < hExpand; h++) row[o++] = invalue;
                }
                break;
            }
        }
    }

    /// <summary>jdcolor.c's converters, a row: YCbCr to RGB and YCCK to CMYK, the rest interleaved as they are.</summary>
    private void Convert(byte[][] sources, int[] offsets, Span<byte> output)
    {
        var width = _d.ImageWidth;
        ReadOnlySpan<byte> Source(int ci) => sources[ci].AsSpan(offsets[ci], width);

        switch (_converter)
        {
            case Converter.Grey:
                Source(0).CopyTo(output);
                break;

            case Converter.YccToRgb:
                RowKernels.YccToRgb(Source(0), Source(1), Source(2), width, output);
                break;

            case Converter.YcckToCmyk:
                RowKernels.YcckToCmyk(Source(0), Source(1), Source(2), Source(3), width, output);
                break;

            default:
            {
                // 3.1.1 - null_convert
                var n = sources.Length;
                if (n == 3)
                {
                    RowKernels.InterleaveThree(Source(0), Source(1), Source(2), width, output);
                }
                else if (n == 4)
                {
                    RowKernels.InterleaveFour(Source(0), Source(1), Source(2), Source(3), width, output);
                }
                else
                {
                    for (var ci = 0; ci < n; ci++)
                    {
                        var input = Source(ci);
                        for (int col = 0, o = ci; col < width; col++, o += n) output[o] = input[col];
                    }
                }
                break;
            }
        }
    }
}
