/*
 * Ported from libjpeg-turbo 3.1.1, src/jdmarker.c:
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1991-1998, Thomas G. Lane.
 * Lossless JPEG Modifications:
 * Copyright (C) 1999, Ken Murchison.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2012, 2015, 2022, 2024, D. R. Commander.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// The marker reader: libjpeg's jdmarker.c. APP0 and APP14 are examined for the JFIF and Adobe markers; every other
/// APPn, COM and DNL is skipped, as libjpeg skips them when no marker is asked to be saved.
/// </summary>
internal sealed partial class Decompressor
{
    private const int MarkerSof0 = 0xC0, MarkerSof1 = 0xC1, MarkerSof2 = 0xC2, MarkerSof3 = 0xC3;
    private const int MarkerSof9 = 0xC9, MarkerSof10 = 0xCA, MarkerSof11 = 0xCB;
    private const int MarkerDht = 0xC4, MarkerDac = 0xCC, MarkerRst0 = 0xD0, MarkerRst7 = 0xD7;
    private const int MarkerSoi = 0xD8, MarkerEoi = 0xD9, MarkerSos = 0xDA, MarkerDqt = 0xDB, MarkerDnl = 0xDC;
    private const int MarkerDri = 0xDD, MarkerApp0 = 0xE0, MarkerApp14 = 0xEE, MarkerApp15 = 0xEF, MarkerCom = 0xFE;
    private const int MarkerTem = 0x01;

    /// <summary>3.1.1 - read_markers: markers until SOS or EOI.</summary>
    private int ReadMarkers()
    {
        for (;;)
        {
            if (UnreadMarker == 0)
            {
                if (!SawSoi) FirstMarker();
                else NextMarker();
            }

            switch (UnreadMarker)
            {
                case MarkerSoi:
                    GetSoi();
                    break;

                case MarkerSof0:
                case MarkerSof1:
                    GetSof(false, false, false);
                    break;
                case MarkerSof2:
                    GetSof(true, false, false);
                    break;
                case MarkerSof3:
                    GetSof(false, true, false);
                    break;
                case MarkerSof9:
                    GetSof(false, false, true);
                    break;
                case MarkerSof10:
                    GetSof(true, false, true);
                    break;
                case MarkerSof11:
                    GetSof(false, true, true);
                    break;

                // Differential processes and JPG, unsupported.
                case 0xC5:
                case 0xC6:
                case 0xC7:
                case 0xC8:
                case 0xCD:
                case 0xCE:
                case 0xCF:
                    throw JpegErrors.Fatal(JpegErrors.SofUnsupported(UnreadMarker));

                case MarkerSos:
                    GetSos();
                    UnreadMarker = 0;
                    return ReachedSos;

                case MarkerEoi:
                    UnreadMarker = 0;
                    return ReachedEoi;

                case MarkerDac:
                    GetDac();
                    break;
                case MarkerDht:
                    GetDht();
                    break;
                case MarkerDqt:
                    GetDqt();
                    break;
                case MarkerDri:
                    GetDri();
                    break;

                case MarkerApp0:
                case MarkerApp14:
                    GetInterestingAppn();
                    break;

                case >= MarkerApp0 and <= MarkerApp15:
                case MarkerCom:
                case MarkerDnl:
                    SkipVariable();
                    break;

                case >= MarkerRst0 and <= MarkerRst7:
                case MarkerTem:
                    break;

                default:
                    // DHP, EXP, JPGn or RESn: likely to signal JPEG Part 3 extensions, so fatal.
                    throw JpegErrors.Fatal(JpegErrors.UnknownMarker(UnreadMarker));
            }

            UnreadMarker = 0;
        }
    }

    /// <summary>3.1.1 - get_soi</summary>
    private void GetSoi()
    {
        if (SawSoi) throw JpegErrors.Fatal(JpegErrors.SoiDuplicate);

        for (var i = 0; i < 16; i++)
        {
            ArithDcL[i] = 0;
            ArithDcU[i] = 1;
            ArithAcK[i] = 5;
        }
        RestartInterval = 0;
        JpegColorSpace = JpegColourSpace.Unknown;
        SawJfif = false;
        SawAdobe = false;
        AdobeTransform = 0;
        SawSoi = true;
    }

    /// <summary>3.1.1 - get_sof</summary>
    private void GetSof(bool isProgressive, bool isLossless, bool isArithmetic)
    {
        if (SawSof) throw JpegErrors.Fatal(JpegErrors.SofDuplicate);

        SofMarker = UnreadMarker;
        Progressive = isProgressive;
        Lossless = isLossless;
        ArithmeticCoding = isArithmetic;

        var length = Source.Read2Bytes();
        Precision = Source.ReadByte();
        ImageHeight = Source.Read2Bytes();
        ImageWidth = Source.Read2Bytes();
        NumComponents = Source.ReadByte();
        length -= 8;

        if (ImageHeight <= 0 || ImageWidth <= 0 || NumComponents <= 0) throw JpegErrors.Fatal(JpegErrors.EmptyImage);
        if (length != NumComponents * 3) throw JpegErrors.Fatal(JpegErrors.BadLength);

        Components = new JpegComponent[NumComponents];
        for (var ci = 0; ci < NumComponents; ci++)
        {
            var component = new JpegComponent { Index = ci, Id = Source.ReadByte() };
            var c = Source.ReadByte();
            component.HSamp = (c >> 4) & 15;
            component.VSamp = c & 15;
            component.QuantTableNo = Source.ReadByte();
            Components[ci] = component;
        }

        SawSof = true;
    }

    /// <summary>3.1.1 - get_sos</summary>
    private void GetSos()
    {
        if (!SawSof) throw JpegErrors.Fatal(JpegErrors.SosNoSof);

        var length = Source.Read2Bytes();
        var n = Source.ReadByte();
        if (length != n * 2 + 6 || n < 1 || n > MaxCompsInScan) throw JpegErrors.Fatal(JpegErrors.BadLength);

        CompsInScan = n;
        Array.Clear(CurComp);

        for (var i = 0; i < n; i++)
        {
            var cc = Source.ReadByte();
            var c = Source.ReadByte();

            // Only the first four components can be named; one named twice is not found the second time.
            JpegComponent? found = null;
            for (var ci = 0; ci < NumComponents && ci < MaxCompsInScan; ci++)
            {
                if (cc == Components[ci].Id && CurComp[ci] is null)
                {
                    found = Components[ci];
                    break;
                }
            }
            if (found is null) throw JpegErrors.Fatal(JpegErrors.BadComponentId(cc));

            CurComp[i] = found;
            found.DcTableNo = (c >> 4) & 15;
            found.AcTableNo = c & 15;

            for (var pi = 0; pi < i; pi++)
                if (ReferenceEquals(CurComp[pi], found)) throw JpegErrors.Fatal(JpegErrors.BadComponentId(cc));
        }

        Ss = Source.ReadByte();
        Se = Source.ReadByte();
        var a = Source.ReadByte();
        Ah = (a >> 4) & 15;
        Al = a & 15;

        NextRestartNum = 0;
        InputScanNumber++;
    }

    /// <summary>3.1.1 - get_dac</summary>
    private void GetDac()
    {
        var length = Source.Read2Bytes() - 2;
        while (length > 0)
        {
            var index = Source.ReadByte();
            var val = Source.ReadByte();
            length -= 2;

            if (index is < 0 or >= 32) throw JpegErrors.Fatal(JpegErrors.DacIndex(index));
            if (index >= 16)
            {
                ArithAcK[index - 16] = (byte)val;
            }
            else
            {
                ArithDcL[index] = (byte)(val & 0x0F);
                ArithDcU[index] = (byte)(val >> 4);
                if (ArithDcL[index] > ArithDcU[index]) throw JpegErrors.Fatal(JpegErrors.DacValue(val));
            }
        }
        if (length != 0) throw JpegErrors.Fatal(JpegErrors.BadLength);
    }

    /// <summary>3.1.1 - get_dht</summary>
    private void GetDht()
    {
        var length = Source.Read2Bytes() - 2;
        Span<byte> bits = stackalloc byte[17];
        Span<byte> huffval = stackalloc byte[256];

        while (length > 16)
        {
            var index = Source.ReadByte();
            bits[0] = 0;
            var count = 0;
            for (var i = 1; i <= 16; i++)
            {
                bits[i] = (byte)Source.ReadByte();
                count += bits[i];
            }
            length -= 1 + 16;

            if (count > 256 || count > length) throw JpegErrors.Fatal(JpegErrors.BadHuffTable);

            for (var i = 0; i < count; i++) huffval[i] = (byte)Source.ReadByte();
            huffval[count..].Clear();
            length -= count;

            HuffmanTable?[] tables;
            if ((index & 0x10) != 0)
            {
                index -= 0x10;
                if (index is < 0 or >= 4) throw JpegErrors.Fatal(JpegErrors.DhtIndex(index));
                tables = AcHuffTables;
            }
            else
            {
                if (index is < 0 or >= 4) throw JpegErrors.Fatal(JpegErrors.DhtIndex(index));
                tables = DcHuffTables;
            }

            var table = tables[index] ??= new HuffmanTable();
            bits.CopyTo(table.Bits);
            huffval.CopyTo(table.Values);
        }

        if (length != 0) throw JpegErrors.Fatal(JpegErrors.BadLength);
    }

    /// <summary>3.1.1 - get_dqt</summary>
    private void GetDqt()
    {
        var length = Source.Read2Bytes() - 2;
        var order = JpegTables.NaturalOrder;

        while (length > 0)
        {
            var n = Source.ReadByte();
            var prec = n >> 4;
            n &= 0x0F;
            if (n >= 4) throw JpegErrors.Fatal(JpegErrors.DqtIndex(n));

            var table = QuantTables[n] ??= new ushort[64];
            for (var i = 0; i < 64; i++)
            {
                var tmp = prec != 0 ? Source.Read2Bytes() : Source.ReadByte();
                table[order[i]] = (ushort)tmp;
            }

            length -= 64 + 1;
            if (prec != 0) length -= 64;
        }

        if (length != 0) throw JpegErrors.Fatal(JpegErrors.BadLength);
    }

    /// <summary>3.1.1 - get_dri</summary>
    private void GetDri()
    {
        var length = Source.Read2Bytes();
        if (length != 4) throw JpegErrors.Fatal(JpegErrors.BadLength);
        RestartInterval = Source.Read2Bytes();
    }

    /// <summary>3.1.1 - get_interesting_appn: APP0 or APP14 examined for JFIF or Adobe, the rest skipped.</summary>
    private void GetInterestingAppn()
    {
        const int dataLength = 14;   // APPN_DATA_LEN
        Span<byte> b = stackalloc byte[dataLength];

        long length = Source.Read2Bytes() - 2;
        var numtoread = length >= dataLength ? dataLength : length > 0 ? (int)length : 0;
        for (var i = 0; i < numtoread; i++) b[i] = (byte)Source.ReadByte();
        length -= numtoread;

        if (UnreadMarker == MarkerApp0) ExamineApp0(b[..numtoread]);
        else ExamineApp14(b[..numtoread]);

        if (length > 0) Source.Skip(length);
    }

    /// <summary>3.1.1 - examine_app0</summary>
    private void ExamineApp0(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 14 && data[0] == 0x4A && data[1] == 0x46 && data[2] == 0x49 && data[3] == 0x46 && data[4] == 0)
        {
            SawJfif = true;
            if (data[5] != 1) Warnings.Add(JpegErrors.JfifMajor(data[5], data[6]));
        }
    }

    /// <summary>3.1.1 - examine_app14</summary>
    private void ExamineApp14(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 12 && data[0] == 0x41 && data[1] == 0x64 && data[2] == 0x6F && data[3] == 0x62 && data[4] == 0x65)
        {
            SawAdobe = true;
            AdobeTransform = data[11];
        }
    }

    /// <summary>3.1.1 - skip_variable</summary>
    private void SkipVariable()
    {
        long length = Source.Read2Bytes() - 2;
        if (length > 0) Source.Skip(length);
    }

    /// <summary>
    /// 3.1.1 - next_marker: the next marker, skipping anything else, which is warned of. The result is never 0 or FF.
    /// </summary>
    private void NextMarker()
    {
        int c;
        for (;;)
        {
            c = Source.ReadByte();
            while (c != 0xFF)
            {
                DiscardedBytes++;
                c = Source.ReadByte();
            }
            do c = Source.ReadByte();
            while (c == 0xFF);
            if (c != 0) break;
            // A stuffed zero (FF 00): data, not a marker.
            DiscardedBytes += 2;
        }

        if (DiscardedBytes != 0)
        {
            Warnings.Add(JpegErrors.ExtraneousData(DiscardedBytes, c));
            DiscardedBytes = 0;
        }

        UnreadMarker = c;
    }

    /// <summary>3.1.1 - first_marker: the SOI marker must come first, with nothing before it.</summary>
    private void FirstMarker()
    {
        var c = Source.ReadByte();
        var c2 = Source.ReadByte();
        if (c != 0xFF || c2 != MarkerSoi) throw JpegErrors.Fatal(JpegErrors.NoSoi(c, c2));
        UnreadMarker = c2;
    }

    /// <summary>
    /// 3.1.1 - read_restart_marker: the restart marker expected next, or the recovery when another is found.
    /// </summary>
    public void ReadRestartMarker()
    {
        if (UnreadMarker == 0) NextMarker();

        if (UnreadMarker == MarkerRst0 + NextRestartNum) UnreadMarker = 0;
        else ResyncToRestart(NextRestartNum);

        NextRestartNum = (NextRestartNum + 1) & 7;
    }

    /// <summary>
    /// 3.1.1 - jpeg_resync_to_restart: a marker other than the restart expected. A restart one or two ahead, or any
    /// other valid marker, is left unread, so the coming segment decodes as empty; one one or two behind, or bogus
    /// data, is skipped to the next marker; anything further away is discarded and decoding resumes.
    /// </summary>
    private void ResyncToRestart(int desired)
    {
        var marker = UnreadMarker;
        Warnings.Add(JpegErrors.MustResync(marker, desired));

        for (;;)
        {
            int action;
            if (marker < MarkerSof0)
                action = 2;
            else if (marker is < MarkerRst0 or > MarkerRst7)
                action = 3;
            else if (marker == MarkerRst0 + ((desired + 1) & 7) || marker == MarkerRst0 + ((desired + 2) & 7))
                action = 3;
            else if (marker == MarkerRst0 + ((desired - 1) & 7) || marker == MarkerRst0 + ((desired - 2) & 7))
                action = 2;
            else
                action = 1;

            switch (action)
            {
                case 1:
                    UnreadMarker = 0;
                    return;
                case 2:
                    NextMarker();
                    marker = UnreadMarker;
                    break;
                default:
                    return;
            }
        }
    }
}
