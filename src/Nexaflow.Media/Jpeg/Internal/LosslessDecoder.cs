/*
 * Ported from libjpeg-turbo 3.1.1, src/jdlhuff.c, src/jdlossls.c and src/jddiffct.c:
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1991-1998, Thomas G. Lane.
 * Lossless JPEG Modifications:
 * Copyright (C) 1999, Ken Murchison.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2022, 2024, D. R. Commander.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// Lossless (SOF3) decoding: Huffman-coded differences (jdlhuff.c), undifferenced by the scan's predictor and scaled
/// back up by its point transform (jdlossls.c), a row of MCUs at a time (jddiffct.c), straight into each component's
/// samples.
/// </summary>
internal sealed class LosslessDecoder : HuffmanBitDecoder
{
    private readonly DerivedHuffmanTable?[] _derived = new DerivedHuffmanTable?[4];
    private readonly DerivedHuffmanTable[] _curTables = new DerivedHuffmanTable[Decompressor.MaxBlocksInMcu];

    // Per output pointer (one per sample row of a component in an MCU): its component, row and MCU width.
    private readonly int[] _ptrComponent = new int[Decompressor.MaxBlocksInMcu];
    private readonly int[] _ptrRow = new int[Decompressor.MaxBlocksInMcu];
    private readonly int[] _ptrMcuWidth = new int[Decompressor.MaxBlocksInMcu];
    private readonly int[] _ptrIndex = new int[Decompressor.MaxBlocksInMcu];
    private readonly int[] _ptrPosition = new int[Decompressor.MaxBlocksInMcu];
    private int _numPtrs;

    /// <summary>An iMCU row of differences and of undifferenced samples, per component (diff_buf, undiff_buf).</summary>
    private readonly int[][][] _diff;
    private readonly int[][][] _undiff;

    /// <summary>Whether each component's next row is the first of a scan or restart interval.</summary>
    private readonly bool[] _firstRow;

    private int _restartRowsToGo;
    private int _mcuRowsPerIMcuRow;

    /// <summary>3.1.1 - jinit_lhuff_decoder, jinit_lossless_decompressor and jinit_d_diff_controller</summary>
    public LosslessDecoder(Decompressor decompressor) : base(decompressor)
    {
        var count = D.NumComponents;
        _diff = new int[count][][];
        _undiff = new int[count][][];
        _firstRow = new bool[count];
        foreach (var component in D.Components)
        {
            var width = Decompressor.RoundUp(component.WidthInBlocks, component.HSamp);
            _diff[component.Index] = Rows(component.VSamp, width);
            _undiff[component.Index] = Rows(component.VSamp, width);

            // The whole image's samples, a byte each, in the plane the output reads.
            component.BlocksWide = width;
            component.BlocksHigh = Decompressor.RoundUp(component.HeightInBlocks, component.VSamp);
            component.PlaneStride = width;
            component.Plane = new byte[checked(width * component.BlocksHigh)];
        }

        static int[][] Rows(int count, int width)
        {
            var rows = new int[count][];
            for (var i = 0; i < count; i++) rows[i] = new int[width];
            return rows;
        }
    }

    /// <summary>
    /// 3.1.1 - start_input_pass (jddiffct.c), start_pass_lossless and start_pass_lhuff_decoder: a scan begins.
    /// </summary>
    public void StartInputPass()
    {
        StartPassLossless();

        if (D.RestartInterval % D.McusPerRow != 0)
            throw JpegErrors.Fatal(JpegErrors.BadRestart(D.RestartInterval, D.McusPerRow));
        _restartRowsToGo = D.RestartInterval / D.McusPerRow;

        StartPass();

        D.InputIMcuRow = 0;
        StartIMcuRow();
    }

    /// <summary>
    /// 3.1.1 - start_pass_lossless: Ss is the predictor (1 to 7), Al the point transform; the first row of each
    /// component is undifferenced from 2^(P-Pt-1).
    /// </summary>
    private void StartPassLossless()
    {
        if (D.Ss is < 1 or > 7 || D.Se != 0 || D.Ah != 0 || D.Al < 0 || D.Al >= D.Precision)
            throw JpegErrors.Fatal(JpegErrors.BadProgression(D.Ss, D.Se, D.Ah, D.Al));
        _firstRow.AsSpan().Fill(true);
    }

    /// <summary>3.1.1 - start_pass_lhuff_decoder</summary>
    public override void StartPass()
    {
        for (var ci = 0; ci < D.CompsInScan; ci++)
        {
            var dctbl = D.CurComp[ci]!.DcTableNo;
            if (dctbl is < 0 or >= 4 || D.DcHuffTables[dctbl] is null) throw JpegErrors.Fatal(JpegErrors.NoHuffTable(dctbl));
            DerivedHuffmanTable.Make(D.DcHuffTables, true, dctbl, true, _derived);
        }

        int sampn = 0, ptrn = 0;
        while (sampn < D.BlocksInMcu)
        {
            var component = D.CurComp[D.McuMembership[sampn]]!;
            for (var yoffset = 0; yoffset < component.McuHeight; yoffset++, ptrn++)
            {
                _ptrComponent[ptrn] = component.Index;
                _ptrRow[ptrn] = yoffset;
                _ptrMcuWidth[ptrn] = component.McuWidth;
                for (var xoffset = 0; xoffset < component.McuWidth; xoffset++, sampn++)
                {
                    _ptrIndex[sampn] = ptrn;
                    _curTables[sampn] = _derived[component.DcTableNo]!;
                }
            }
        }
        _numPtrs = ptrn;

        BitsLeft = 0;
        BitBuffer = 0;
        InsufficientData = false;
    }

    public override void DecodeMcu(McuBlocks mcu) => throw new InvalidOperationException("lossless scans decode a row at a time");

    /// <summary>3.1.1 - start_iMCU_row (jddiffct.c)</summary>
    private void StartIMcuRow()
    {
        if (D.CompsInScan > 1)
            _mcuRowsPerIMcuRow = 1;
        else if (D.InputIMcuRow < D.TotalIMcuRows - 1)
            _mcuRowsPerIMcuRow = D.CurComp[0]!.VSamp;
        else
            _mcuRowsPerIMcuRow = D.CurComp[0]!.LastRowHeight;
    }

    /// <summary>3.1.1 - process_restart (jdlhuff.c and jddiffct.c)</summary>
    private void ProcessRestart()
    {
        ReadRestart();
        if (D.UnreadMarker == 0) InsufficientData = false;
        StartPassLossless();
        _restartRowsToGo = D.RestartInterval / D.McusPerRow;
    }

    /// <summary>
    /// 3.1.1 - decompress_data (jddiffct.c), as consume_data drives it: one iMCU row decoded, undifferenced and
    /// scaled into the components' planes.
    /// </summary>
    public int ConsumeData()
    {
        for (var yoffset = 0; yoffset < _mcuRowsPerIMcuRow; yoffset++)
        {
            if (D.RestartInterval != 0 && _restartRowsToGo == 0) ProcessRestart();
            DecodeMcus(yoffset, D.McusPerRow);
            if (D.RestartInterval != 0) _restartRowsToGo--;
        }

        // Undifference and scale each row of each component, leaving out dummy rows at the bottom.
        for (var ci = 0; ci < D.CompsInScan; ci++)
        {
            var component = D.CurComp[ci]!;
            var compi = component.Index;
            var rows = D.InputIMcuRow == D.TotalIMcuRows - 1 ? component.LastRowHeight : component.VSamp;
            for (int row = 0, prevRow = component.VSamp - 1; row < rows; prevRow = row, row++)
            {
                Undifference(compi, _diff[compi][row], _undiff[compi][prevRow], _undiff[compi][row], component.WidthInBlocks);
                var planeRow = (D.InputIMcuRow * component.VSamp + row) * component.PlaneStride;
                Scale(_undiff[compi][row], component.Plane.AsSpan(planeRow, component.WidthInBlocks));
            }
        }

        if (++D.InputIMcuRow < D.TotalIMcuRows)
        {
            StartIMcuRow();
            return Decompressor.RowCompleted;
        }

        D.FinishInputPass();
        return Decompressor.ScanCompleted;
    }

    /// <summary>
    /// 3.1.1 - decode_mcus: <paramref name="count"/> MCUs of differences, each sample to its row of its component.
    /// Out of data, the rows are zeroed and every predictor reset, so the samples come out as 2^(P-1).
    /// </summary>
    private void DecodeMcus(int mcuRow, int count)
    {
        for (var ptrn = 0; ptrn < _numPtrs; ptrn++) _ptrPosition[ptrn] = 0;

        if (InsufficientData)
        {
            for (var ptrn = 0; ptrn < _numPtrs; ptrn++)
                Array.Clear(_diff[_ptrComponent[ptrn]][mcuRow + _ptrRow[ptrn]], 0, count * _ptrMcuWidth[ptrn]);
            StartPassLossless();
            return;
        }

        var getBuffer = BitBuffer;
        var bitsLeft = BitsLeft;
        for (var mcuNum = 0; mcuNum < count; mcuNum++)
        {
            for (var sampn = 0; sampn < D.BlocksInMcu; sampn++)
            {
                // Section H.2.2: the sample difference
                var s = HuffDecode(ref getBuffer, ref bitsLeft, _curTables[sampn]);
                if (s != 0)
                {
                    if (s == 16)
                    {
                        s = 32768;   // always 32768, with no bits following
                    }
                    else
                    {
                        var r = GetBits(ref getBuffer, ref bitsLeft, s);
                        s = HuffExtend(r, s);
                    }
                }

                var ptrn = _ptrIndex[sampn];
                _diff[_ptrComponent[ptrn]][mcuRow + _ptrRow[ptrn]][_ptrPosition[ptrn]++] = s;
            }
            BitBuffer = getBuffer;
            BitsLeft = bitsLeft;
        }
    }

    /// <summary>
    /// The undifferencers of jdlossls.c, a row: predicted from the samples to the left (Ra), above (Rb) and above
    /// left (Rc), modulo 2^16. The first row of a scan or restart interval starts from 2^(P-Pt-1) and runs on the
    /// horizontal predictor; after it, the first sample is predicted from above and the rest by the scan's predictor.
    /// </summary>
    private void Undifference(int compi, int[] diff, int[] prevRow, int[] undiff, int width)
    {
        var i = 0;
        if (_firstRow[compi])
        {
            // jpeg_undifference_first_row
            var ra = (diff[0] + (1 << (D.Precision - D.Al - 1))) & 0xFFFF;
            undiff[0] = ra;
            while (++i < width) undiff[i] = ra = (diff[i] + ra) & 0xFFFF;
            _firstRow[compi] = false;
            return;
        }

        var psv = D.Ss;
        if (psv == 1)
        {
            // jpeg_undifference1
            var ra = (diff[0] + prevRow[0]) & 0xFFFF;
            undiff[0] = ra;
            while (++i < width) undiff[i] = ra = (diff[i] + ra) & 0xFFFF;
            return;
        }

        // jpeg_undifference2..7: Rb and Rc are read before the sample is written, as the rows may be the same array.
        var rb = prevRow[0];
        var a = (diff[0] + rb) & 0xFFFF;
        undiff[0] = a;
        while (++i < width)
        {
            var rc = rb;
            rb = prevRow[i];
            var predicted = psv switch
            {
                2 => rb,
                3 => rc,
                4 => a + rb - rc,
                5 => a + ((rb - rc) >> 1),
                6 => rb + ((a - rc) >> 1),
                _ => (a + rb) >> 1,
            };
            undiff[i] = a = (diff[i] + predicted) & 0xFFFF;
        }
    }

    /// <summary>simple_upscale and noscale: the point transform undone, the sample kept to its low eight bits.</summary>
    private void Scale(int[] undiff, Span<byte> output)
    {
        var al = D.Al;
        for (var i = 0; i < output.Length; i++) output[i] = (byte)(undiff[i] << al);
    }
}
