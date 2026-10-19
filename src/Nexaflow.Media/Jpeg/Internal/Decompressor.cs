/*
 * Ported from libjpeg-turbo 3.1.1, src/jdapimin.c, src/jdapistd.c, src/jdinput.c and src/jdmaster.c:
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1991-1998, Thomas G. Lane.
 * Modified 2002-2009 by Guido Vollbeding.
 * Lossless JPEG Modifications:
 * Copyright (C) 1999, Ken Murchison.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2009-2011, 2016, 2018-2019, 2022-2024, D. R. Commander.
 * Copyright (C) 2013, Linaro Limited.
 * Copyright (C) 2015, Google, Inc.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

using System.Buffers;

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// One decode, start to finish — libjpeg's decompression object and the controllers that drive it. The input side
/// reads markers and entropy-decodes every scan into the whole image's coefficients (lossy) or samples (lossless);
/// the output side then turns them into pixels: <see cref="ImageOutput"/>.
/// </summary>
/// <remarks>
/// libjpeg decodes a single-scan file a row of MCUs at a time as the caller reads scanlines, and absorbs a
/// multi-scan file whole before the first one. Holding every file whole decodes the same coefficients either way —
/// each block is zeroed before its data is decoded, and blocks the data never reaches stay zero — and lets the
/// output be shared out over the processors.
/// </remarks>
internal sealed partial class Decompressor
{
    public const int MaxComponents = 10;
    public const int MaxCompsInScan = 4;
    public const int MaxBlocksInMcu = 10;
    public const int MaxSampFactor = 4;
    public const int MaxDimension = 65500;

    // What jpeg_consume_input and its helpers return.
    public const int ReachedSos = 1;
    public const int ReachedEoi = 2;
    public const int RowCompleted = 3;
    public const int ScanCompleted = 4;

    public readonly JpegWarnings Warnings = new();
    public readonly JpegSource Source;
    public readonly bool Parallel;

    // Marker reader state.
    public int UnreadMarker;
    public bool SawSoi, SawSof;
    public int DiscardedBytes;
    public int NextRestartNum;

    // The frame.
    public int Precision, ImageWidth, ImageHeight, NumComponents;
    public bool Progressive, Lossless, ArithmeticCoding;
    public int SofMarker;
    public JpegComponent[] Components = [];

    // Tables, as the markers so far define them.
    public readonly ushort[]?[] QuantTables = new ushort[]?[4];
    public readonly HuffmanTable?[] DcHuffTables = new HuffmanTable?[4];
    public readonly HuffmanTable?[] AcHuffTables = new HuffmanTable?[4];
    public readonly byte[] ArithDcL = new byte[16];
    public readonly byte[] ArithDcU = new byte[16];
    public readonly byte[] ArithAcK = new byte[16];
    public int RestartInterval;

    // APP0 and APP14.
    public bool SawJfif;
    public bool SawAdobe;
    public int AdobeTransform;

    // The current scan.
    public int CompsInScan;
    public readonly JpegComponent?[] CurComp = new JpegComponent?[MaxCompsInScan];
    public int Ss, Se, Ah, Al;
    public int InputScanNumber;
    public int MaxHSamp, MaxVSamp, TotalIMcuRows, McusPerRow, McuRowsInScan, BlocksInMcu;
    public readonly int[] McuMembership = new int[MaxBlocksInMcu];

    // Input controller state.
    public bool HasMultipleScans, EoiReached;
    private bool _inHeaders = true;
    private bool _inScan;
    public int InputIMcuRow;
    public int LastGoodIMcuRow;

    /// <summary>Progressive only: the Al of each coefficient's last scan, -1 before any; rows n..2n-1 the scan before.</summary>
    public int[][]? CoefBits;

    public JpegColourSpace JpegColorSpace, OutColorSpace;
    private EntropyDecoder? _entropy;
    private LosslessDecoder? _lossless;
    private ImageOutput? _output;

    /// <summary>Whether the components' coefficient buffers are the whole image's, rented, to go back after.</summary>
    private bool _rentedCoefficients;

    public Decompressor(ReadOnlyMemory<byte> data, bool parallel)
    {
        Source = new JpegSource(data, Warnings);
        Parallel = parallel;
    }

    /// <summary>
    /// 3.1.1 - jpeg_read_header with an image required: reads markers to the first SOS and settles the colour spaces
    /// from what they said.
    /// </summary>
    public void ReadHeader()
    {
        if (ConsumeMarkers() == ReachedEoi) throw JpegErrors.Fatal(JpegErrors.NoImage);
        DefaultDecompressParms();
    }

    public JpegHeader Header() => new(
        ImageWidth, ImageHeight, Precision, ProcessOf(SofMarker), ArithmeticCoding,
        [.. Components.Select(component => component.Id)], SawJfif, SawAdobe ? AdobeTransform : null, JpegColorSpace);

    private static JpegProcess ProcessOf(int sof) => sof switch
    {
        0xC0 => JpegProcess.Baseline,
        0xC2 or 0xCA => JpegProcess.Progressive,
        0xC3 or 0xCB => JpegProcess.Lossless,
        _ => JpegProcess.ExtendedSequential,
    };

    /// <summary>
    /// 3.1.1 - jpeg_start_decompress, the output pass and jpeg_finish_decompress. A multi-scan image is absorbed
    /// whole before any output, as jpeg_start_decompress absorbs it; a single-scan one is decoded as its rows are
    /// output, as libjpeg decodes it.
    /// </summary>
    public JpegImage Decompress()
    {
        byte[] samples;
        try
        {
            MasterSelection();
            if (HasMultipleScans)
            {
                while (ConsumeInput() != ReachedEoi) { }
                samples = Lossless ? _output!.RenderLossless() : _output!.RenderBuffered();
            }
            else if (Lossless)
            {
                while (ConsumeInput() != ScanCompleted) { }
                FinishReading();
                samples = _output!.RenderLossless();
            }
            else
            {
                samples = _output!.RenderStreamed();
            }
        }
        finally
        {
            if (_rentedCoefficients)
            {
                foreach (var component in Components)
                {
                    ArrayPool<short>.Shared.Return(component.Coefficients);
                    component.Coefficients = [];
                }
            }
        }

        return new JpegImage(ImageWidth, ImageHeight, OutColorSpace, JpegColorSpace, _output.OutputComponents,
            Lossless ? Precision : 8, samples, Warnings.Messages);
    }

    /// <summary>The next iMCU row of a single-scan image, entropy-decoded into its components' coefficient buffers.</summary>
    public int DecodeScanRow() => ConsumeData();

    /// <summary>
    /// 3.1.1 - jpeg_finish_decompress, after a single-scan image: the markers that follow its scan are read to EOI.
    /// libjpeg reports what it finds wrong there only once every scanline is out; it cannot change the image, so here
    /// it is a warning on the image rather than the image refused.
    /// </summary>
    public void FinishReading()
    {
        try
        {
            while (!EoiReached) ConsumeInput();
        }
        catch (JpegException failure)
        {
            Warnings.Add(failure.Message);
        }
    }

    private int ConsumeInput() => _inScan ? ConsumeData() : ConsumeMarkers();

    /// <summary>3.1.1 - consume_markers</summary>
    private int ConsumeMarkers()
    {
        if (EoiReached) return ReachedEoi;

        var val = ReadMarkers();
        if (val == ReachedSos)
        {
            if (_inHeaders)
            {
                InitialSetup();
                _inHeaders = false;
            }
            else
            {
                if (!HasMultipleScans) throw JpegErrors.Fatal(JpegErrors.EoiExpected);
                StartInputPass();
            }
        }
        else
        {
            EoiReached = true;
            if (_inHeaders && SawSof) throw JpegErrors.Fatal(JpegErrors.SofNoSos);
        }
        return val;
    }

    /// <summary>3.1.1 - initial_setup: sizes, at the first SOS.</summary>
    private void InitialSetup()
    {
        var dataUnit = Lossless ? 1 : 8;

        if (ImageHeight > MaxDimension || ImageWidth > MaxDimension)
            throw JpegErrors.Fatal(JpegErrors.ImageTooBig(MaxDimension));

        if (Lossless)
        {
            if (Precision is < 2 or > 16) throw JpegErrors.Fatal(JpegErrors.BadPrecision(Precision));
        }
        else if (Precision is not (8 or 12))
        {
            throw JpegErrors.Fatal(JpegErrors.BadPrecision(Precision));
        }

        if (NumComponents > MaxComponents) throw JpegErrors.Fatal(JpegErrors.ComponentCount(NumComponents, MaxComponents));

        MaxHSamp = 1;
        MaxVSamp = 1;
        foreach (var component in Components)
        {
            if (component.HSamp is <= 0 or > MaxSampFactor || component.VSamp is <= 0 or > MaxSampFactor)
                throw JpegErrors.Fatal(JpegErrors.BadSampling);
            MaxHSamp = Math.Max(MaxHSamp, component.HSamp);
            MaxVSamp = Math.Max(MaxVSamp, component.VSamp);
        }

        foreach (var component in Components)
        {
            component.WidthInBlocks = DivRoundUp((long)ImageWidth * component.HSamp, MaxHSamp * dataUnit);
            component.HeightInBlocks = DivRoundUp((long)ImageHeight * component.VSamp, MaxVSamp * dataUnit);
            component.DownsampledWidth = DivRoundUp((long)ImageWidth * component.HSamp, MaxHSamp);
            component.DownsampledHeight = DivRoundUp((long)ImageHeight * component.VSamp, MaxVSamp);
            component.QuantTable = null;
        }

        TotalIMcuRows = DivRoundUp(ImageHeight, MaxVSamp * dataUnit);
        HasMultipleScans = CompsInScan < NumComponents || Progressive;
    }

    /// <summary>3.1.1 - per_scan_setup: the MCU's make-up for the scan the last SOS began.</summary>
    private void PerScanSetup()
    {
        var dataUnit = Lossless ? 1 : 8;

        if (CompsInScan == 1)
        {
            var component = CurComp[0]!;
            McusPerRow = component.WidthInBlocks;
            McuRowsInScan = component.HeightInBlocks;
            component.McuWidth = 1;
            component.McuHeight = 1;
            component.McuBlocks = 1;
            component.LastColWidth = 1;
            var tmp = component.HeightInBlocks % component.VSamp;
            component.LastRowHeight = tmp == 0 ? component.VSamp : tmp;
            BlocksInMcu = 1;
            McuMembership[0] = 0;
            return;
        }

        if (CompsInScan is <= 0 or > MaxCompsInScan)
            throw JpegErrors.Fatal(JpegErrors.ComponentCount(CompsInScan, MaxCompsInScan));

        McusPerRow = DivRoundUp(ImageWidth, MaxHSamp * dataUnit);
        McuRowsInScan = DivRoundUp(ImageHeight, MaxVSamp * dataUnit);
        BlocksInMcu = 0;
        for (var ci = 0; ci < CompsInScan; ci++)
        {
            var component = CurComp[ci]!;
            component.McuWidth = component.HSamp;
            component.McuHeight = component.VSamp;
            component.McuBlocks = component.McuWidth * component.McuHeight;
            var tmp = component.WidthInBlocks % component.McuWidth;
            component.LastColWidth = tmp == 0 ? component.McuWidth : tmp;
            tmp = component.HeightInBlocks % component.McuHeight;
            component.LastRowHeight = tmp == 0 ? component.McuHeight : tmp;
            var mcublks = component.McuBlocks;
            if (BlocksInMcu + mcublks > MaxBlocksInMcu) throw JpegErrors.Fatal(JpegErrors.BadMcuSize);
            while (mcublks-- > 0) McuMembership[BlocksInMcu++] = ci;
        }
    }

    /// <summary>3.1.1 - latch_quant_tables: each component keeps the table its first scan used.</summary>
    private void LatchQuantTables()
    {
        for (var ci = 0; ci < CompsInScan; ci++)
        {
            var component = CurComp[ci]!;
            if (component.QuantTable is not null) continue;
            var qtblno = component.QuantTableNo;
            if (qtblno is < 0 or >= 4 || QuantTables[qtblno] is null) throw JpegErrors.Fatal(JpegErrors.NoQuantTable(qtblno));
            component.QuantTable = (ushort[])QuantTables[qtblno]!.Clone();
        }
    }

    /// <summary>3.1.1 - start_input_pass</summary>
    private void StartInputPass()
    {
        PerScanSetup();
        if (Lossless)
        {
            _lossless!.StartInputPass();
        }
        else
        {
            LatchQuantTables();
            _entropy!.StartPass();
            InputIMcuRow = 0;
            StartIMcuRow();
        }
        _inScan = true;
    }

    /// <summary>3.1.1 - finish_input_pass</summary>
    public void FinishInputPass() => _inScan = false;

    /// <summary>3.1.1 - default_decompress_parms: the colour spaces the markers imply.</summary>
    private void DefaultDecompressParms()
    {
        switch (NumComponents)
        {
            case 1:
                JpegColorSpace = JpegColourSpace.Grey;
                OutColorSpace = JpegColourSpace.Grey;
                break;

            case 3:
                if (SawJfif)
                {
                    JpegColorSpace = JpegColourSpace.YCbCr;
                }
                else if (SawAdobe)
                {
                    switch (AdobeTransform)
                    {
                        case 0: JpegColorSpace = JpegColourSpace.Rgb; break;
                        case 1: JpegColorSpace = JpegColourSpace.YCbCr; break;
                        default:
                            Warnings.Add(JpegErrors.AdobeTransform(AdobeTransform));
                            JpegColorSpace = JpegColourSpace.YCbCr;
                            break;
                    }
                }
                else
                {
                    // No special markers: guess from the component ids.
                    int cid0 = Components[0].Id, cid1 = Components[1].Id, cid2 = Components[2].Id;
                    if (cid0 == 1 && cid1 == 2 && cid2 == 3)
                        JpegColorSpace = Lossless ? JpegColourSpace.Rgb : JpegColourSpace.YCbCr;
                    else if (cid0 == 82 && cid1 == 71 && cid2 == 66)
                        JpegColorSpace = JpegColourSpace.Rgb;
                    else
                        JpegColorSpace = Lossless ? JpegColourSpace.Rgb : JpegColourSpace.YCbCr;
                }
                OutColorSpace = JpegColourSpace.Rgb;
                break;

            case 4:
                if (SawAdobe)
                {
                    switch (AdobeTransform)
                    {
                        case 0: JpegColorSpace = JpegColourSpace.Cmyk; break;
                        case 2: JpegColorSpace = JpegColourSpace.Ycck; break;
                        default:
                            Warnings.Add(JpegErrors.AdobeTransform(AdobeTransform));
                            JpegColorSpace = JpegColourSpace.Ycck;
                            break;
                    }
                }
                else
                {
                    JpegColorSpace = JpegColourSpace.Cmyk;
                }
                OutColorSpace = JpegColourSpace.Cmyk;
                break;

            default:
                JpegColorSpace = JpegColourSpace.Unknown;
                OutColorSpace = JpegColourSpace.Unknown;
                break;
        }
    }

    /// <summary>
    /// 3.1.1 - master_selection: the modules this image needs, chosen and checked in libjpeg's order, so a file it
    /// cannot decode fails here with the same complaint; then the first scan begins.
    /// </summary>
    private void MasterSelection()
    {
        // Every component's DCT_scaled_size stays 8 (1 lossless): nothing is scaled, so the output is the image's size.
        _output = new ImageOutput(this);   // jinit_color_deconverter and jinit_upsampler

        if (Lossless)
        {
            if (Precision is > 8 or < 2) throw JpegErrors.Fatal(JpegErrors.BadPrecision(Precision));
            if (ArithmeticCoding) throw JpegErrors.Fatal(JpegErrors.ArithNotImplemented);
            _lossless = new LosslessDecoder(this);
        }
        else
        {
            if (Precision != 8) throw JpegErrors.Fatal(JpegErrors.BadPrecision(Precision));
            if (ArithmeticCoding)
            {
                _entropy = new ArithmeticDecoder(this);
            }
            else if (Progressive)
            {
                _entropy = new ProgressiveHuffmanDecoder(this);
            }
            else
            {
                StandardHuffmanTables();
                _entropy = new HuffmanDecoder(this);
            }

            // The coefficient buffer, padded to a multiple of the sampling factors: a multi-scan image's whole and
            // pre-zeroed, as libjpeg's, from the shared pool; a single-scan image's a band at a time, as the output
            // asks for it.
            foreach (var component in Components)
            {
                component.BlocksWide = RoundUp(component.WidthInBlocks, component.HSamp);
                component.BlocksHigh = RoundUp(component.HeightInBlocks, component.VSamp);
                if (!HasMultipleScans) continue;
                var length = checked(component.BlocksWide * component.BlocksHigh * 64);
                component.Coefficients = ArrayPool<short>.Shared.Rent(length);
                Array.Clear(component.Coefficients, 0, length);
                _rentedCoefficients = true;
            }
        }

        StartInputPass();
        LastGoodIMcuRow = 0;
    }

    /// <summary>
    /// 3.1.1 - std_huff_tables, as jinit_huff_decoder applies it: Motion JPEG frames leave out the standard tables, so
    /// a sequential Huffman image that defines no table 0 or 1 by its first scan is given the standard ones.
    /// </summary>
    private void StandardHuffmanTables()
    {
        DcHuffTables[0] ??= HuffmanTable.Of(JpegTables.DcLuminanceBits, JpegTables.DcLuminanceValues);
        AcHuffTables[0] ??= HuffmanTable.Of(JpegTables.AcLuminanceBits, JpegTables.AcLuminanceValues);
        DcHuffTables[1] ??= HuffmanTable.Of(JpegTables.DcChrominanceBits, JpegTables.DcChrominanceValues);
        AcHuffTables[1] ??= HuffmanTable.Of(JpegTables.AcChrominanceBits, JpegTables.AcChrominanceValues);
    }

    public static int DivRoundUp(long a, long b) => (int)((a + b - 1L) / b);

    public static int RoundUp(int a, int b) => (a + b - 1) / b * b;
}
