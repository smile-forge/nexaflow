/*
 * Ported from libjpeg-turbo 3.1.1, src/jdcoefct.c and src/jdcoefct.h (the input side):
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1994-1997, Thomas G. Lane.
 * libjpeg-turbo Modifications:
 * Copyright 2009 Pierre Ossman <ossman@cendio.se> for Cendio AB
 * Copyright (C) 2010, 2015-2016, 2019-2020, 2022-2024, D. R. Commander.
 * Copyright (C) 2015, 2020, Google, Inc.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>Where the entropy decoder stores each block of an MCU: its component's array and the block's offset.</summary>
internal sealed class McuBlocks
{
    public readonly short[][] Arrays = new short[Decompressor.MaxBlocksInMcu][];
    public readonly int[] Offsets = new int[Decompressor.MaxBlocksInMcu];
}

/// <summary>The coefficient controller's input side: jdcoefct.c's consume_data, into the full-image buffer.</summary>
internal sealed partial class Decompressor
{
    private readonly McuBlocks _mcu = new();
    private int _mcuRowsPerIMcuRow;

    /// <summary>3.1.1 - start_iMCU_row (jdcoefct.h)</summary>
    private void StartIMcuRow()
    {
        // An interleaved scan's MCU row is an iMCU row; a noninterleaved one has v_samp_factor of them, fewer at the
        // bottom.
        if (CompsInScan > 1)
            _mcuRowsPerIMcuRow = 1;
        else if (InputIMcuRow < TotalIMcuRows - 1)
            _mcuRowsPerIMcuRow = CurComp[0]!.VSamp;
        else
            _mcuRowsPerIMcuRow = CurComp[0]!.LastRowHeight;
    }

    /// <summary>3.1.1 - consume_data: one iMCU row of the scan, decoded into the coefficient buffer.</summary>
    private int ConsumeData()
    {
        if (Lossless) return _lossless!.ConsumeData();

        var entropy = _entropy!;
        var mcu = _mcu;
        for (var yoffset = 0; yoffset < _mcuRowsPerIMcuRow; yoffset++)
        {
            for (var mcuCol = 0; mcuCol < McusPerRow; mcuCol++)
            {
                // The blocks of this MCU, in the order the scan codes them.
                var blkn = 0;
                for (var ci = 0; ci < CompsInScan; ci++)
                {
                    var component = CurComp[ci]!;
                    var startCol = mcuCol * component.McuWidth;
                    for (var yindex = 0; yindex < component.McuHeight; yindex++)
                    {
                        var blockRow = InputIMcuRow * component.VSamp + yindex + yoffset;
                        var offset = ((blockRow - component.FirstBlockRow) * component.BlocksWide + startCol) * 64;
                        for (var xindex = 0; xindex < component.McuWidth; xindex++, offset += 64)
                        {
                            mcu.Arrays[blkn] = component.Coefficients;
                            mcu.Offsets[blkn++] = offset;
                        }
                    }
                }

                if (!entropy.InsufficientData) LastGoodIMcuRow = InputIMcuRow;
                entropy.DecodeMcu(mcu);
            }
        }

        if (++InputIMcuRow < TotalIMcuRows)
        {
            StartIMcuRow();
            return RowCompleted;
        }

        FinishInputPass();
        return ScanCompleted;
    }
}
