// Ported without nullable annotations; they are added as the file is reworked.
#nullable disable
#region License
/*
 * Copyright (c) 2002-2007, Communications and Remote Sensing Laboratory, Universite catholique de Louvain (UCL), Belgium
 * Copyright (c) 2002-2007, Professor Benoit Macq
 * Copyright (c) 2001-2003, David Janssens
 * Copyright (c) 2002-2003, Yannick Verschueren
 * Copyright (c) 2003-2007, Francois-Olivier Devaux and Antonin Descampe
 * Copyright (c) 2005, Herve Drolon, FreeImage Team
 * Copyright (c) 2006-2007, Parvatha Elangovan
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 * 1. Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 * 2. Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS `AS IS'
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 * POSSIBILITY OF SUCH DAMAGE.
 */
#endregion
#region Using
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OPJ_UINT32 = System.UInt32;
#endregion

namespace Nexaflow.Media.Jpeg2000.Internal
{
    /// <summary>
    /// Tile coder/decoder
    /// </summary>
    internal sealed class TileCoder
    {
        private delegate void ThreadWorker(object job);
        private delegate IEnumerable<object> CreateJobs(bool mt, Tier1Coding.SetRet ret);

        #region Variables and properties

        internal bool DisableMultiThreading => _cinfo.DisableMultiThreading;

        internal TcdImage TcdImage
        { get { return _tcd_image; } }

        internal JPXImage Image
        { get { return _image; } }

        /// <summary>
        /// Coordinates of the window of interest, in grid reference space
        /// </summary>
        private uint _win_x0, _win_y0, _win_x1, _win_y1;

        /// <summary>
        /// Whether the whole tile is decoded, or just the region in win_x0/win_y0/win_x1/win_y1
        /// </summary>
        /// <remarks>
        /// Only valid for decoding. 
        /// </remarks>
        internal bool WholeTileDecoding { get; set; }

        /// <summary>
        /// Codec context
        /// </summary>
        readonly CompressionInfo _cinfo;

        /// <summary>
        /// Info on each image tile
        /// </summary>
        TcdImage _tcd_image;

        /// <summary>
        /// Image
        /// </summary>
        JPXImage _image;

        /// <summary>
        /// Coding parameters
        /// </summary>
        CodingParameters _cp;

        /// <summary>
        /// Pointer to the current encoded/decoded tile
        /// </summary>
        //TcdTile _tcd_tile;

        /// <summary>
        /// Coding/decoding parameters common to all tiles
        /// </summary>
        TileCodingParams _tcp;

        /// <summary>
        /// Current encoded/decoded tile
        /// </summary>
        uint _tcd_tileno;

        #endregion

        #region Init

        /// <summary>
        /// Creates a tile-coder encoder
        /// </summary>
        /// <remarks>2.5 - opj_j2k_create_tcd</remarks>
        internal TileCoder(CompressionInfo cinfo, JPXImage image, CodingParameters cp)
        {
            _cinfo = cinfo;
            _tcd_image = new TcdImage();
            Init(image, cp);
        }

        /// <summary>
        /// Initialize the tile coder and may reuse some memory
        /// </summary>
        /// <remarks>2.5 - opj_tcd_init</remarks>
        void Init(JPXImage image, CodingParameters cp)
        {
            _cp = cp;
            _image = image;

            var tile = new TcdTile();

            _tcd_image.tiles = new TcdTile[1] { tile };
            tile.comps = new TcdTilecomp[image.numcomps];
            for(int i = 0; i < tile.comps.Length; i++)
                tile.comps[i] = new TcdTilecomp();

            tile.numcomps = image.numcomps;
            
        }

        //2.5
        internal bool InitDecodeTile(uint tile_no)
        {
            return InitTile(tile_no);
        }

        /// <param name="tile_no">Tile number</param>
        /// <remarks>
        /// 2.5 - opj_tcd_init_tile
        /// 
        /// C# note
        /// Org. impl used a macro to construct two similar functions, one
        /// for encode and one for decode. Later this was changed to a parameter.
        /// 
        /// For 2.5 we stick to the org. impl, using a parameter.
        /// </remarks>
        private bool InitTile(uint p_tile_no)
        {
            uint pdx, pdy;
            int x0b, y0b;
            // extent of precincts , top left, bottom right
            int tl_prc_x_start, tl_prc_y_start, br_prc_x_end, br_prc_y_end;
            // number of precinct for a resolution 
            uint n_precincts;
            // room needed to store l_nb_precinct precinct for a resolution 
            uint n_precinct_size;
            // number of code blocks for a precinct
            uint n_code_blocks;
            // room needed to store l_nb_code_blocks code blocks for a precinct
            uint n_code_blocks_size;
            // size of data for a tile 
            int data_size;

            CodingParameters cp = _cp;
            TileCodingParams tcp = cp.tcps[p_tile_no];
            TcdTile[] tiles = _tcd_image.tiles;
            TcdTile tile = tiles[0];
            TileCompParams[] tccps = tcp.tccps;
            TileCompParams tccp = tccps[0];
            TcdTilecomp[] tilecs = tile.comps;
            JPXImage image = _image;
            ImageComp[] image_comps = _image.comps;

            uint p = p_tile_no % cp.tw;       // tile coordinates 
            uint q = p_tile_no / cp.tw;

            // 4 borders of the tile rescale on the image if necessary
            {
                var tx0 = cp.tx0 + p * cp.tdx; // can't be greater than image->x1 so won't overflow
                tile.x0 = (int)Math.Max(tx0, image.x0);
                tile.x1 = (int)Math.Min(MyMath.uint_adds(tx0, cp.tdx), image.x1);
            }

            // All those OPJ_UINT32 are casted to OPJ_INT32, let's do a sanity check
            if (tile.x0 < 0 || tile.x1 <= tile.x0)
            {
                _cinfo.Error("Tile X coordinates are not supported");
                return false;
            }

            {
                var ty0 = cp.ty0 + q * cp.tdy; //can't be greater than image->y1 so won't overflow
                tile.y0 = (int)Math.Max(ty0, image.y0);
                tile.y1 = (int)Math.Min(MyMath.uint_adds(ty0, cp.tdy), image.y1);
            }

            // All those OPJ_UINT32 are casted to OPJ_INT32, let's do a sanity check
            if (tile.y0 < 0 || tile.y1 <= tile.y0)
            {
                _cinfo.Error("Tile Y coordinates are not supported");
                return false;
            }

            // testcase 1888.pdf.asan.35.988 
            if (tccp.numresolutions == 0)
            {
                _cinfo.Error("tiles require at least one resolution\n");
                return false;
            }

            /*tile.numcomps = image.numcomps; */
            for (uint compno = 0; compno < tile.comps.Length; compno++)
            {
                //C# impl, we don't have pointers, so these three are
                //         fetched every cycle.
                tccp = tccps[compno];
                TcdTilecomp tilec = tilecs[compno];
                ImageComp image_comp = image_comps[compno];

                image_comp.resno_decoded = 0;
                // Border of each tile component (global) 
                tilec.x0 = MyMath.int_ceildiv(tile.x0, (int)image_comp.dx);
                tilec.y0 = MyMath.int_ceildiv(tile.y0, (int)image_comp.dy);
                tilec.x1 = MyMath.int_ceildiv(tile.x1, (int)image_comp.dx);
                tilec.y1 = MyMath.int_ceildiv(tile.y1, (int)image_comp.dy);
                tilec.compno = compno;

                tilec.numresolutions = tccp.numresolutions;
                if (tccp.numresolutions < cp.specific_param.dec.reduce)
                {
                    tilec.minimum_num_resolutions = 1;
                }
                else
                {
                    tilec.minimum_num_resolutions = tccp.numresolutions
                        - cp.specific_param.dec.reduce;
                }



                //C# tilec.resolutions is an array of pointers instead of a
                //   struct[], so datasize is simply the number of resolutions
                data_size = (int) tilec.numresolutions;

                tilec.data_win = null;
                tilec.win_x0 = 0;
                tilec.win_y0 = 0;
                tilec.win_x1 = 0;
                tilec.win_y1 = 0;

                if (tilec.resolutions == null)
                {
                    tilec.resolutions = new TcdResolution[data_size];
                    //tilec.resolutions_size = data_size;

                    for (int c = 0; c < tilec.resolutions.Length; c++)
                        tilec.resolutions[c] = new TcdResolution();
                }
                else if (data_size > tilec.resolutions.Length)
                {
                    var old = tilec.resolutions.Length;
                    Array.Resize<TcdResolution>(ref tilec.resolutions, data_size);
                    for (int c = old; c < tilec.resolutions.Length; c++)
                        tilec.resolutions[c] = new TcdResolution();
                }

                uint level_no = tilec.numresolutions;
                //TcdResolution[] reso = tilec.resolutions;
                StepSize[] step_sizes = tccp.stepsizes;

                //C# C moves the step_size pointer, so we use this instead.
                int step_size_ptr = 0;

                for (int resno = 0; resno < tilec.numresolutions; resno++)
                {
                    int tlcbgxstart, tlcbgystart /*, brcbgxend, brcbgyend*/;
                    uint cbgwidthexpn, cbgheightexpn;
                    uint cblkwidthexpn, cblkheightexpn;

                    //C# impl, advances the res pointer.
                    TcdResolution res = tilec.resolutions[resno];

                    --level_no;

                    // Border for each resolution level (global)
                    res.x0 = MyMath.int_ceildivpow2(tilec.x0, (int)level_no);
                    res.y0 = MyMath.int_ceildivpow2(tilec.y0, (int)level_no);
                    res.x1 = MyMath.int_ceildivpow2(tilec.x1, (int)level_no);
                    res.y1 = MyMath.int_ceildivpow2(tilec.y1, (int)level_no);

                    // p. 35, table A-23, ISO/IEC FDIS154444-1 : 2000 (18 august 2000) 
                    pdx = tccp.prcw[resno];
                    pdy = tccp.prch[resno];

                    // p. 64, B.6, ISO/IEC FDIS15444-1 : 2000 (18 august 2000)  
                    tl_prc_x_start = MyMath.int_floordivpow2(res.x0, (int)pdx) << (int)pdx;
                    tl_prc_y_start = MyMath.int_floordivpow2(res.y0, (int)pdy) << (int)pdy;
                    {
                        uint tmp = (uint) MyMath.int_ceildivpow2(res.x1, (int)pdx) << (int)pdx;
                        if (tmp > int.MaxValue)
                        {
                            _cinfo.Error("Integer overflow");
                            return false;
                        }
                        br_prc_x_end = (int)tmp;
                    }
                    {
                        uint tmp = (uint) MyMath.int_ceildivpow2(res.y1, (int)pdy) << (int)pdy;
                        if (tmp > int.MaxValue)
                        {
                            _cinfo.Error("Integer overflow");
                            return false;
                        }
                        br_prc_y_end = (int)tmp;
                    }

                    res.pw = (res.x0 == res.x1) ? 0u : 
                        (uint)((br_prc_x_end - tl_prc_x_start) >> (int)pdx);
                    res.ph = (res.y0 == res.y1) ? 0u : 
                        (uint)((br_prc_y_end - tl_prc_y_start) >> (int)pdy);

                    if (res.pw != 0 && (int.MaxValue / res.pw) < res.ph)
                    {//C# impl. We use int.MaxValue instead of uint.MaxValue
                        _cinfo.Error("Size of tile data exceeds system limits");
                        return false;
                    }

                    n_precincts = res.pw * res.ph;
                    //C# impl note.
                    //Snip code that tests if the n_precincts * sizeof(opj_tcd_precinct_t)
                    //is bigger than uint.MaxValue (they get this value by casting -1 to uint)
                    //Anyway, in this implementation TcdPrecinct is not a struct. Meaning the
                    //check dosn't quite make sense. But there is probably a practial limit to
                    //how big we should accept n_precincts to be.
                    n_precinct_size = n_precincts;

                    if (resno == 0)
                    {
                        tlcbgxstart = tl_prc_x_start;
                        tlcbgystart = tl_prc_y_start;
                        /*brcbgxend = l_br_prc_x_end;*/
                        /* brcbgyend = l_br_prc_y_end;*/
                        cbgwidthexpn = pdx;
                        cbgheightexpn = pdy;
                        res.numbands = 1;
                    }
                    else
                    {
                        tlcbgxstart = MyMath.int_ceildivpow2(tl_prc_x_start, 1);
                        tlcbgystart = MyMath.int_ceildivpow2(tl_prc_y_start, 1);
                        /*brcbgxend = opj_int_ceildivpow2(l_br_prc_x_end, 1);*/
                        /*brcbgyend = opj_int_ceildivpow2(l_br_prc_y_end, 1);*/
                        cbgwidthexpn = pdx - 1;
                        cbgheightexpn = pdy - 1;
                        res.numbands = 3;
                    }

                    cblkwidthexpn = Math.Min(tccp.cblkw, cbgwidthexpn);
                    cblkheightexpn = Math.Min(tccp.cblkh, cbgheightexpn);
                    //C# impl: We set band inside the for loop.

                    for (uint bandno = 0; bandno < res.numbands; bandno++, step_size_ptr++)
                    {
                        TcdBand band = res.bands[bandno];

                        if (resno == 0)
                        {
                            band.bandno = 0;
                            band.x0 = MyMath.int_ceildivpow2(tilec.x0, (int)level_no);
                            band.y0 = MyMath.int_ceildivpow2(tilec.y0, (int)level_no);
                            band.x1 = MyMath.int_ceildivpow2(tilec.x1, (int)level_no);
                            band.y1 = MyMath.int_ceildivpow2(tilec.y1, (int)level_no);
                        }
                        else
                        {
                            band.bandno = bandno + 1;
                            // x0b = 1 if bandno = 1 or 3 
                            x0b = (int) (band.bandno & 1u);
                            // y0b = 1 if bandno = 2 or 3 
                            y0b = (int) (band.bandno >> 1);
                            // l_band border (global) 
                            band.x0 = MyMath.int64_ceildivpow2(tilec.x0 - (1L << (int)level_no) * x0b, (int)(level_no + 1));
                            band.y0 = MyMath.int64_ceildivpow2(tilec.y0 - (1L << (int)level_no) * y0b, (int)(level_no + 1));
                            band.x1 = MyMath.int64_ceildivpow2(tilec.x1 - (1L << (int)level_no) * x0b, (int)(level_no + 1));
                            band.y1 = MyMath.int64_ceildivpow2(tilec.y1 - (1L << (int)level_no) * y0b, (int)(level_no + 1));
                        }



                        //C# *_step_size is kept in synch with *l_band, see ++l_band, ++l_step_size in 2.5 source.
                        StepSize step_size = step_sizes[step_size_ptr];
                        {
                            // Table E-1 - Sub-band gains
                            // BUG_WEIRD_TWO_INVK (look for this identifier in dwt.c):
                            // the test (!isEncoder && l_tccp->qmfbid == 0) is strongly
                            // linked to the use of two_invK instead of invK
                            int log2_gain = tccp.qmfbid == 0 ? 0 : 
                                            (band.bandno == 0) ? 0 :
                                            (band.bandno == 3) ? 2 : 1;

                            // Nominal dynamic range. Equation E-4
                            int Rb = (int)image_comp.prec + log2_gain;

                            // Delta_b value of Equation E-3 in "E.1 Inverse quantization
                            // procedure" of the standard
                            band.stepsize = (1f + step_size.mant / 2048f) * (float)Math.Pow(2f, (int)(Rb - step_size.expn));
                        }

                        // Mb value of Equation E-2 in "E.1 Inverse quantization
                        // procedure" of the standard
                        band.numbps = step_size.expn + (int)tccp.numgbits - 1;

                        if (band.precincts == null)
                        {
                            if (n_precincts > 0)
                            {
                                band.precincts = new TcdPrecinct[n_precinct_size];

                                //Nulling
                                for (int c = 0; c < band.precincts.Length; c++)
                                    band.precincts[c] = new TcdPrecinct();

                                //same as l_band.precincts.Length
                                //l_band.precincts_data_size = l_nb_precinct_size;
                            }
                        }
                        else if (band.precincts.Length < n_precinct_size)
                        {
                            int old_size = band.precincts.Length;
                            Array.Resize<TcdPrecinct>(ref band.precincts, (int)n_precinct_size);
                            for (int c = old_size; c < band.precincts.Length; c++)
                                band.precincts[c] = new TcdPrecinct();
                        }

                        //C# impl: current_precinct is set within the loop
                        for (int precno = 0; precno < n_precincts; precno++)
                        {
                            int tlcblkxstart, tlcblkystart, brcblkxend, brcblkyend;
                            int cbgxstart = tlcbgxstart + (int)(precno % res.pw) * (1 << (int)cbgwidthexpn);
                            int cbgystart = tlcbgystart + (int)(precno / res.pw) * (1 << (int)cbgheightexpn);
                            int cbgxend = cbgxstart + (1 << (int)cbgwidthexpn);
                            int cbgyend = cbgystart + (1 << (int)cbgheightexpn);

                            // C# since we don't have pointers, we set instead of increment.
                            TcdPrecinct current_precinct = band.precincts[precno];

                            current_precinct.x0 = Math.Max(cbgxstart, band.x0);
                            current_precinct.y0 = Math.Max(cbgystart, band.y0);
                            current_precinct.x1 = Math.Min(cbgxend, band.x1);
                            current_precinct.y1 = Math.Min(cbgyend, band.y1);

                            tlcblkxstart = MyMath.int_floordivpow2(current_precinct.x0, (int)cblkwidthexpn) << (int)cblkwidthexpn;
                            tlcblkystart = MyMath.int_floordivpow2(current_precinct.y0, (int)cblkheightexpn) << (int)cblkheightexpn;
                            brcblkxend = MyMath.int_ceildivpow2(current_precinct.x1, (int)cblkwidthexpn) << (int)cblkwidthexpn;
                            brcblkyend = MyMath.int_ceildivpow2(current_precinct.y1, (int)cblkheightexpn) << (int)cblkheightexpn;
                            current_precinct.cw = (uint)((brcblkxend - tlcblkxstart) >> (int)cblkwidthexpn);
                            current_precinct.ch = (uint)((brcblkyend - tlcblkystart) >> (int)cblkheightexpn);

                            n_code_blocks = current_precinct.cw * current_precinct.ch;
                            //C# impl note.
                            //Snip code that tests if the n_code_blocks * sizeof(sizeof_block)
                            //is bigger than uint.MaxValue. Again we're not using a struct[]
                            n_code_blocks_size = n_code_blocks;

                            if (current_precinct.dec == null)
                            {
                                current_precinct.dec = new TcdCblkDec[n_code_blocks_size];

                                //Nulling
                                for (int c = 0; c < current_precinct.dec.Length; c++)
                                    current_precinct.dec[c] = new TcdCblkDec();

                                //Same as l_current_precinct.dec.Length
                                //l_current_precinct.block_size = l_nb_code_blocks_size;
                            }
                            else if (n_code_blocks_size > current_precinct.dec.Length)
                            {
                                int old_size = current_precinct.dec.Length;
                                Array.Resize<TcdCblkDec>(ref current_precinct.dec, (int)n_code_blocks_size);
                                for (int c = old_size; c < current_precinct.dec.Length; c++)
                                    current_precinct.dec[c] = new TcdCblkDec();
                            }

                            if (current_precinct.incltree == null)
                            {
                                current_precinct.incltree = TagTree.Create(current_precinct.cw, current_precinct.ch, _cinfo);
                            }
                            else
                            {
                                current_precinct.incltree.Init(current_precinct.cw, current_precinct.ch);
                            }

                            if (current_precinct.imsbtree == null)
                            {
                                current_precinct.imsbtree = TagTree.Create(current_precinct.cw, current_precinct.ch, _cinfo);
                            }
                            else
                            {
                                current_precinct.imsbtree.Init(current_precinct.cw, current_precinct.ch);
                            }

                            for (int cblkno = 0; cblkno < n_code_blocks; cblkno++)
                            {
                                int cblkxstart = tlcblkxstart + (int)(cblkno % current_precinct.cw) * (1 << (int)cblkwidthexpn);
                                int cblkystart = tlcblkystart + (int)(cblkno / current_precinct.cw) * (1 << (int)cblkheightexpn);
                                int cblkxend = cblkxstart + (1 << (int)cblkwidthexpn);
                                int cblkyend = cblkystart + (1 << (int)cblkheightexpn);

                                var code_block = current_precinct.dec[cblkno];

                                CodeBlockDecAllocate(code_block);

                                // code-block size (global)
                                code_block.x0 = Math.Max(cblkxstart, current_precinct.x0);
                                code_block.y0 = Math.Max(cblkystart, current_precinct.y0);
                                code_block.x1 = Math.Min(cblkxend, current_precinct.x1);
                                code_block.y1 = Math.Min(cblkyend, current_precinct.y1);
                            }
                            //C# impl, current_precinct set in the for loop
                        } /* precno */
                    } /* bandno */
                    //C# impl, res set in the for loop
                } /* resno */
                // C# impl, tccp, tilec and image_comp set in the for loop
            } /* compno */
            return true;
        }

        /// <summary>
        /// Allocates memory for an decoding block
        /// </summary>
        /// <param name="code_block">Coding block to allocate memory to</param>
        /// <remarks>
        /// 2.5 - opj_tcd_code_block_dec_allocate
        /// </remarks>
        void CodeBlockDecAllocate(TcdCblkDec code_block)
        {
            if (code_block.segs == null)
            {
                code_block.segs = new TcdSeg[Constants.J2K_DEFAULT_NB_SEGS];
                //for (int c = 0; c < p_code_block.segs.Length; c++)
                //    p_code_block.segs[c] = new TcdSeg();

                code_block.current_max_segs = Constants.J2K_DEFAULT_NB_SEGS;
            }
            else
            {
                TcdSeg[] segs = code_block.segs;
                uint current_max_segs = code_block.current_max_segs;
                TcdSegDataChunk[] chunks = code_block.chunks;
                uint numchunksalloc = code_block.numchunksalloc;

                code_block.decoded_data = null;

                //C# impl, alternative to memset 0
                code_block.Reset();
                code_block.segs = segs;
                code_block.current_max_segs = current_max_segs;
                for(uint i = 0; i < current_max_segs; i++)
                {
                    //Reinits segment
                    segs[i] = new TcdSeg();
                }
                code_block.chunks = chunks;
                code_block.numchunksalloc = numchunksalloc;
            }
        }

        #endregion

        //2.5
        bool T2Decode(byte[] src, int src_len, CodestreamIndex cstr_index)
        {
            Tier2Coding t2 = new Tier2Coding(_cinfo, _image, _cp);

            return t2.DecodePackets(this, _tcd_tileno, _tcd_image.tiles[0], src, src_len, cstr_index);
        }

        /// <remarks>
        /// //2.5 - opj_tcd_t1_decode
        /// 
        /// Implements multithreading with thread limiter. This function is
        /// basically all new, with the "real" T1Decode beeing made into a
        /// generator. See comment of that function for why.
        /// </remarks>
        bool T1Decode()
        {
            return RunThreads((mt, set_ret) =>
            {
                return T1Decode(mt, set_ret);
            }, (obj) =>
            {
                Tier1Coding.ThreadWorker((Tier1Coding.T1CBLKDecodeProcessingJob)obj);
            });
        }

        /// <summary>
        /// To share code between encoding and decoding, the MT code has been
        /// funneled into this function, with the differences handled by a couple
        /// of delegates
        /// </summary>
        /// <remarks>
        /// Org impl depends on its threadpool impl for limiting the number of threads,
        /// it also takes advantage of threadlocal store.
        /// 
        /// Since this impl. depends on .net's threadpool, we have to manualy limit the
        /// number of executable threads, and local storage isn't reccomended with these
        /// threads, as we don't own them, so we don't use it.
        /// </remarks>
        /// <param name="create_jobs">Used to generate the jobs that will be scheduled</param>
        /// <param name="run_thread">Command to actually run the job</param>
        /// <returns>True of true</returns>
        private bool RunThreads(CreateJobs create_jobs, ThreadWorker run_thread)
        {
            bool ret = true;

            //How many threads are currently working
            int running_jobs = 0;

            int max_threads;
            ThreadPool.GetAvailableThreads(out max_threads, out _);
            max_threads = _cinfo.DisableMultiThreading ? 1 : Math.Min(Environment.ProcessorCount, max_threads);

            //Generator for work
            var work = create_jobs(max_threads > 1, (val) =>
            {
                //Since we only allow setting this value false,
                //we don't need to worry about syncing the threads.
                if (val != null && val.Value == false)
                    ret = val.Value;
                return ret;
            });

            var reset = new ManualResetEvent(true);
            var done = new ManualResetEvent(true); //<-- true in case there's no jobs
            try
            {
                foreach (var job in work)
                {
                    //Before we enqueue the job, we must wait for a thread to become
                    //avalible.
                    int jobs = Interlocked.Increment(ref running_jobs);
                    if (jobs == max_threads)
                    {
                        //When max_threads == 1, we'll always enter this if,
                        //including on the first itteration.

                        reset.WaitOne();
                        reset.Reset();
                    }

                    //max_threads may be == 1, which is why this isn't an "else if"
                    if (jobs == 1)
                    {
                        //Can only be 1 after incrementing from 0, as this thread is
                        //the only thread incrementing running_jobs.
                        //
                        //This means we know that "done" is set, and thus needs
                        //to be reset.
                        //
                        //We lock before we reset so that we're sure that the thread
                        //which decremented to zero is actually done, as it may be
                        //sleeping right before "done.Set()"
                        lock (reset) { done.Reset(); }
                    }

                    ThreadPool.QueueUserWorkItem((x) =>
                    {
                        try { run_thread(x); }
                        finally
                        {
                            // Locking on reset is safe as it's a method local object.
                            //
                            //The reason we grab a lock is because we this thread
                            //may fall asleep right before done.Set().
                            lock (reset)
                            {
                                //Only worker threads decrement running jobs. 
                                int rj = Interlocked.Decrement(ref running_jobs);

                                //In theory we don't always have to set the reset flag,
                                //we only need to do so when (running_jobs == max_threads)
                                //as that means the main thread may be asleep.
                                //
                                //However, setting reset doesn't hurt, as we know a slot
                                //for new work has become avalible. (This thread is finished
                                //after all), and I'm not 100% sure I'm right in my thinking.
                                //
                                //if (running_jobs == max_threads)
                                {

                                    //Signal the main thread that it can schedule more
                                    //work.
                                    reset.Set();
                                    //The lock( ) isn't needed for reset.Set(), it can be
                                    //placed after the lock. But again, I'm not 100%
                                    //sure.
                                }

                                //rj being zero means this thread decremented running_jobs to
                                //zero. Since done will only be reset when running_jobs is
                                //zero, we know that at worst the main thread is waiting to
                                //call done.reset() right after this thread sets it. 
                                //
                                //
                                //running_jobs being zero means the main thread may be
                                //waiting on the done signal*.
                                //
                                //* It can also be sleeping before the increement, in which
                                //  case it will reset the done signal after waking up.
                                //* If it's sleeping after the increement, running_jobs can't
                                //  be zero.
                                if (rj == 0 && running_jobs == 0)
                                {
                                    done.Set();
                                }
                            }
                        }
                    }, job);

                    if (jobs == max_threads)
                    {
                        //We don't want jobs to become bigger than max_threads, as my logic
                        //don't like that.
                        reset.WaitOne();
                    }

                    //For when we break out of the loop early.
                    if (!ret)
                        break;
                }

                //We have to wait until all threads terminate.
                done.WaitOne();
            }
            finally
            {
                done.Dispose();
                reset.Dispose();
            }

            return ret;
        }

        /// <summary>
        /// Creates decoding jobs
        /// </summary>
        /// <param name="must_copy">If multithreading, this must be set true</param>
        /// <remarks>
        /// 2.5 - opj_tcd_t1_decode
        /// 
        /// To simplify the MT impl, this function and the function it calls have been changed to iterators,
        /// which lets me put all the MT stuff in a single parent function.
        /// 
        /// The basic problem is that the org impl assumes you can control the amount of threads in the
        /// threadpool. That's of course possible, but have side effects on C#, unless, of course, you
        /// make your own threadpool implementation.
        /// 
        /// So since I have to make changes anyway, I've decided to make it easier for myself.
        /// </remarks>
        private IEnumerable<Tier1Coding.T1CBLKDecodeProcessingJob> T1Decode(bool must_copy, Tier1Coding.SetRet set_ret)
        {
            TcdTile tile = _tcd_image.tiles[0];
            var tccps = _tcp.tccps;
            bool check_pterm = false;


            if (_tcp.num_layers_to_decode == _tcp.numlayers &&
                (tccps[0].cblksty & CCP_CBLKSTY.PTERM) != 0)
            {
                check_pterm = true;
            }

            for (int compno = 0; compno < tile.numcomps; ++compno)
            {
                var tilec = tile.comps[compno];
                foreach (var job in Tier1Coding.DecodeCblks(this, tilec, tccps[compno], check_pterm,
                    _cinfo, set_ret, must_copy))
                {
                    yield return job;

                    if (!set_ret(null))
                        break;
                }

                if (!set_ret(null))
                    yield break;
            }
        }

        //2.5 - opj_tcd_dwt_decode
        bool DWTDecode()
        {
            TcdTile tile = _tcd_image.tiles[0];
            var tccps = _tcp.tccps;
            var comps = _image.comps;

            for (int compno = 0; compno < tile.numcomps; compno++)
            {
                TcdTilecomp tilec = tile.comps[compno];

                if (tccps[compno].qmfbid == 1)
                {
                    if (!DWT.Decode(this, tilec, comps[compno].resno_decoded + 1))
                        return false;
                }
                else
                {
                    if (!DWT.DecodeReal(this, tilec, comps[compno].resno_decoded + 1))
                        return false;
                }
            }

            return true;
        }

        //2.5 - opj_tcd_mct_decode
        bool MCTDecode()
        {
            TcdTile tile = _tcd_image.tiles[0];
            TcdTilecomp[] tile_comps = tile.comps;
            TcdTilecomp tile_comp = tile_comps[0];
            int samples;

            if (_tcp.mct == 0)
                return true;

            if (WholeTileDecoding)
            {
                TcdResolution res_comp0 = tile.comps[0].resolutions[tile_comp.minimum_num_resolutions - 1];

                // A bit inefficient: we process more data than needed if
                // resno_decoded < l_tile_comp->minimum_num_resolutions-1,
                // but we would need to take into account a stride then
                samples = (int)((res_comp0.x1 - res_comp0.x0) * (res_comp0.y1 - res_comp0.y0));
                if (tile.numcomps >= 3)
                {
                    if (tile_comp.minimum_num_resolutions != tile.comps[1].minimum_num_resolutions ||
                        tile_comp.minimum_num_resolutions != tile.comps[2].minimum_num_resolutions)
                    {
                        _cinfo.Error("Tiles don't all have the same dimension. Skip the MCT step.");
                        return false;
                    }

                    TcdResolution res_comp1 = tile.comps[1].resolutions[tile_comp.minimum_num_resolutions - 1];
                    TcdResolution res_comp2 = tile.comps[2].resolutions[tile_comp.minimum_num_resolutions - 1];
                    // testcase 1336.pdf.asan.47.376
                    if (_image.comps[0].resno_decoded != _image.comps[1].resno_decoded ||
                        _image.comps[0].resno_decoded != _image.comps[2].resno_decoded ||
                        (res_comp1.x1 - res_comp1.x0) * (res_comp1.y1 - res_comp1.y0) != samples ||
                        (res_comp2.x1 - res_comp2.x0) * (res_comp2.y1 - res_comp2.y0) != samples)
                    {
                        _cinfo.Error("Tiles don't all have the same dimension. Skip the MCT step.");
                        return false;
                    }
                }
            }
            else
            {
                TcdResolution res_comp0 = tile.comps[0].resolutions[tile_comp.minimum_num_resolutions - 1];
                samples = (int)((res_comp0.win_x1 - res_comp0.win_x0) * (res_comp0.win_y1 - res_comp0.win_y0));

                if (tile.numcomps >= 3)
                {
                    TcdResolution res_comp1 = tile.comps[1].resolutions[tile_comp.minimum_num_resolutions - 1];
                    TcdResolution res_comp2 = tile.comps[2].resolutions[tile_comp.minimum_num_resolutions - 1];
                    // testcase 1336.pdf.asan.47.376
                    if (_image.comps[0].resno_decoded != _image.comps[1].resno_decoded ||
                        _image.comps[0].resno_decoded != _image.comps[2].resno_decoded ||
                        (res_comp1.win_x1 - res_comp1.win_x0) * (res_comp1.win_y1 - res_comp1.win_y0) != samples ||
                        (res_comp2.win_x1 - res_comp2.win_x0) * (res_comp2.win_y1 - res_comp2.win_y0) != samples)
                    {
                        _cinfo.Error("Tiles don't all have the same dimension. Skip the MCT step.");
                        return false;
                    }
                }
            }

            if (tile.numcomps >= 3)
            {
                if (_tcp.mct == 2)
                {
                    if (_tcp.mct_decoding_matrix == null)
                        return true;

                    var data = new int[tile.numcomps][];
                    for (int i = 0; i < data.Length; i++)
                        data[i] = tile_comps[i].data;

                    MCT.DecodeCustom(_tcp.mct_decoding_matrix, samples, data, tile.numcomps, _image.comps[0].sgnd);
                }
                else
                {
                    if (_tcp.tccps[0].qmfbid == 1)
                    {
                        if (WholeTileDecoding)
                        {
                            MCT.Decode(tile.comps[0].data,
                                       tile.comps[1].data,
                                       tile.comps[2].data,
                                       samples);
                        }
                        else
                        {
                            MCT.Decode(tile.comps[0].data_win,
                                       tile.comps[1].data_win,
                                       tile.comps[2].data_win,
                                       samples);
                        }
                    }
                    else
                    {
                        if (WholeTileDecoding)
                        {
                            MCT.DecodeReal(tile.comps[0].data,
                                           tile.comps[1].data,
                                           tile.comps[2].data,
                                           samples);
                        }
                        else
                        {
                            MCT.DecodeReal(tile.comps[0].data_win,
                                           tile.comps[1].data_win,
                                           tile.comps[2].data_win,
                                           samples);
                        }
                    }
                }
            }
            else
                _cinfo.Warn("Number of components ({0}) is inconsistent with a MCT. Skip the MCT step.", tile.numcomps);

            return true;
        }

        //2.5.1 - opj_tcd_dc_level_shift_decode
        void DcLevelShiftDecode()
        {
            TcdTile tile = _tcd_image.tiles[0];
            var tccps = _tcp.tccps;
            int min, max;

            for (int compno = 0; compno < tile.numcomps; ++compno)
            {
                TcdTilecomp tile_comp = tile.comps[compno];
                ImageComp imagec = _image.comps[compno];
                TcdResolution res = tile_comp.resolutions[imagec.resno_decoded];
                TileCompParams tccp = tccps[compno];
                int width, height, stride, data_ptr;
                int[] data;

                if (!WholeTileDecoding)
                {
                    width = (int)(res.win_x1 - res.win_x0);
                    height = (int)(res.win_y1 - res.win_y0);
                    stride = 0;
                    data = tile_comp.data_win;
                    data_ptr = 0;
                }
                else
                {
                    width = res.x1 - res.x0;
                    height = res.y1 - res.y0;
                    stride = tile_comp.resolutions[tile_comp.minimum_num_resolutions - 1].x1 -
                             tile_comp.resolutions[tile_comp.minimum_num_resolutions - 1].x0 -
                             width;
                    data = tile_comp.data;
                    data_ptr = 0;
                }

                if (imagec.sgnd)
                {
                    min = -(1 << ((int)imagec.prec - 1));
                    max = (1 << ((int)imagec.prec - 1)) - 1;
                }
                else
                {
                    min = 0;
                    max = (1 << (int)imagec.prec) - 1;
                }

                if (width == 0 || height == 0)
                    continue;

                if (_tcp.tccps[compno].qmfbid == 1)
                {
                    for (int j = 0; j < height; j++)
                    {
                        for (int i = 0; i < width; i++)
                        {
                            data[data_ptr] = MyMath.int_clamp(data[data_ptr] + tccp.dc_level_shift, min, max);
                            data_ptr++;
                        }
                        data_ptr += stride;
                    }
                }
                else
                {
                    IntOrFloat fi = new IntOrFloat();
                    for (int j = 0; j < height; j++)
                    {
                        for (int i = 0; i < width; i++)
                        {
                            fi.I = data[data_ptr];
                            if (fi.F > int.MaxValue)
                            {
                                data[data_ptr] = max;
                            }
                            else if (fi.F < int.MinValue)
                            {
                                data[data_ptr] = min;
                            }
                            else 
                            {
                                // Do addition on int64 to avoid overflows
                                //long value_int = lrintf(fi.F);
                                long value_int = (long)Math.Round(fi.F); //<-- Impl. banker's rounding (round towards even).
                                data[data_ptr] = (int)MyMath.int64_clamp(value_int + tccp.dc_level_shift, min, max);
                            }
                            data_ptr++;
                        }
                        data_ptr += stride;
                    }
                }
            }
        }

        /// <remarks>
        /// There's a SSE2 version of this, and it uses banker's rounding. I'm just using
        /// Math.Round instead.
        /// </remarks>
        //long lrintf(float f) 
        //{ 
        //    //Give different results over what the C impl does, as it uses bankers rounding.
        //    //Fortunatly, Math.Round also uses bankers rounding.
        //    return (long)(f > 0f ? (f + .5f) : (f - .5f)); 
        //}

        /// <remarks>
        /// 2.5 - opj_tcd_decode_tile
        /// 
        /// Note that variables named "tileno" is used inconsistently  
        /// in the lib.
        ///  - As the actual tileno
        ///  - As a lookup into a tilno array
        ///  - As a dual lookup in the tilno array
        /// 
        /// In this case I changed tilno to be a the same
        /// value for both prep and this (actual).
        /// </remarks>
        internal bool DecodeTile(
            uint win_x0,
            uint win_y0,
            uint win_x1,
            uint win_y1,
            byte[] src,
            int src_len,
            uint tileno,
            CodestreamIndex cstr_index)
        {
            _tcd_tileno = tileno;
            _tcp = _cp.tcps[tileno];
            _win_x0 = win_x0;
            _win_y0 = win_y0;
            _win_x1 = win_x1;
            _win_y1 = win_y1;
            WholeTileDecoding = true;



            for (uint compno = 0; compno < _image.numcomps; compno++)
            {
                if (!IsWholeTilecompDecoding(compno))
                {
                    WholeTileDecoding = false;
                    break;
                }
            }

            if (WholeTileDecoding)
            {
                for (var compno = 0; compno < _image.numcomps; compno++)
                {
                    var tilec = _tcd_image.tiles[0].comps[compno];
                    var l_res = tilec.resolutions[tilec.minimum_num_resolutions - 1];
                    long l_data_size;

                    // compute data_size with overflow check
                    long res_w = (long)(l_res.x1 - l_res.x0);
                    long res_h = (long)(l_res.y1 - l_res.y0);

                    // issue 733, l_data_size == 0U, probably something wrong should be checked before getting here
                    if (res_h > 0 && res_w > Constants.SIZE_MAX / res_h)
                    {
                        _cinfo.Error("Size of tile data exceeds system limits");
                        return false;
                    }
                    l_data_size = res_w * res_h;

                    if (Constants.SIZE_MAX / sizeof(OPJ_UINT32) < l_data_size)
                    {
                        _cinfo.Error("Size of tile data exceeds system limits");
                        return false;
                    }
                    //l_data_size *= sizeof(OPJ_UINT32);

                    tilec.data_size_needed = l_data_size;

                    if (!tilec.AllocTileComponentData())
                    {
                        _cinfo.Error("Size of tile data exceeds system limits");
                        return false;
                    }
                }
            }
            else
            {
                // Compute restricted tile-component and tile-resolution coordinates
                // of the window of interest, but defer the memory allocation until
                // we know the resno_decoded
                for (var compno = 0; compno < _image.numcomps; compno++)
                {
                    var tilec = _tcd_image.tiles[0].comps[compno];
                    var image_comp = _image.comps[compno];

                    // Compute the intersection of the area of interest, expressed in tile coordinates
                    // with the tile coordinates
                    tilec.win_x0 = Math.Max(
                                        (OPJ_UINT32)tilec.x0,
                                        MyMath.uint_ceildiv(_win_x0, image_comp.dx));
                    tilec.win_y0 = Math.Max(
                                        (OPJ_UINT32)tilec.y0,
                                        MyMath.uint_ceildiv(_win_y0, image_comp.dy));
                    tilec.win_x1 = Math.Min(
                                        (OPJ_UINT32)tilec.x1,
                                        MyMath.uint_ceildiv(_win_x1, image_comp.dx));
                    tilec.win_y1 = Math.Min(
                                        (OPJ_UINT32)tilec.y1,
                                        MyMath.uint_ceildiv(_win_y1, image_comp.dy));
                    if (tilec.win_x1 < tilec.win_x0 ||
                            tilec.win_y1 < tilec.win_y0)
                    {
                        // We should not normally go there. The circumstance is when
                        // the tile coordinates do not intersect the area of interest
                        // Upper level logic should not even try to decode that tile
                        _cinfo.Error("Invalid tilec.win_xxx values");
                        return false;
                    }

                    for (int resno = 0; resno < tilec.numresolutions; ++resno)
                    {
                        var res = tilec.resolutions[resno];
                        res.win_x0 = MyMath.uint_ceildivpow2(tilec.win_x0,
                                                           (int)tilec.numresolutions - 1 - resno);
                        res.win_y0 = MyMath.uint_ceildivpow2(tilec.win_y0,
                                                           (int)tilec.numresolutions - 1 - resno);
                        res.win_x1 = MyMath.uint_ceildivpow2(tilec.win_x1,
                                                           (int)tilec.numresolutions - 1 - resno);
                        res.win_y1 = MyMath.uint_ceildivpow2(tilec.win_y1,
                                                           (int)tilec.numresolutions - 1 - resno);
                    }
                }
            }

            //// --------------- Tier 2 -------------
            if (!T2Decode(src, src_len, cstr_index))
                return false;

            //DumpCblks("");

            //// --------------- Tier 1 -------------
            if (!T1Decode())
                return false;

            // For subtile decoding, now we know the resno_decoded, we can allocate
            // the tile data buffer
            if (!WholeTileDecoding)
            {
                for (int compno = 0; compno < _image.numcomps; compno++)
                {
                    TcdTilecomp tilec = _tcd_image.tiles[0].comps[compno];
                    ImageComp image_comp = _image.comps[compno];
                    var res = tilec.resolutions;
                    uint res_pt = image_comp.resno_decoded;
                    ulong w = res[res_pt].win_x1 - res[res_pt].win_x0;
                    ulong h = res[res_pt].win_y1 - res[res_pt].win_y0;
                    ulong l_data_size;

                    tilec.data_win = null;

                    if (w > 0 && h > 0)
                    {
                        if (w > Constants.SIZE_MAX / h)
                        {
                            _cinfo.Error("Size of tile data exceeds system limits");
                            return false;
                        }
                        l_data_size = w * h;
                        if (l_data_size > Constants.SIZE_MAX / sizeof(int))
                        {
                            _cinfo.Error("Size of tile data exceeds system limits");
                            return false;
                        }
                        //l_data_size *= sizeof(int);

                        tilec.data_win = new int[l_data_size];
                    }
                }
            }

            //DumpTilcomp("Before DWT", false);
            //DumpAreaCblks(""); //<-- Use when !WholeTileDecoding

            //// --------------- DWT ----------------
            if (!DWTDecode())
                return false;

            //if (tileno == 17)
            //DumpTilcomp("Before MCT", !WholeTileDecoding);

            //// --------------- MCT ----------------
            if (!MCTDecode())
                return false;

            //DumpTilcomp("After MCT");

            DcLevelShiftDecode();

            //DumpTilcomp("After DC");

            return true;
        }


        //2.5
        internal uint GetDecodedTileSize(bool take_into_account_partial_decoding)
        {
            uint i, data_size = 0;
            ImageComp[] img_comps;
            TcdTilecomp[] tile_comps;
            uint size_comp, remaining;

            tile_comps = _tcd_image.tiles[0].comps;
            img_comps = _image.comps;
            for (i = 0; i < _image.numcomps; i++)
            {
                OPJ_UINT32 w, h;
                var img_comp = img_comps[i];
                var tile_comp = tile_comps[i];
                size_comp = (uint)img_comp.prec >> 3; /*(/ 8)*/
                remaining = (uint)img_comp.prec & 7u;  /* (%8) */

                if (remaining != 0)
                {
                    size_comp++;
                }

                if (size_comp == 3)
                {
                    size_comp = 4;
                }


                var res = tile_comp.resolutions[tile_comp.minimum_num_resolutions - 1];
                if (take_into_account_partial_decoding && !WholeTileDecoding)
                {
                    w = res.win_x1 - res.win_x0;
                    h = res.win_y1 - res.win_y0;
                }
                else
                {
                    w = (OPJ_UINT32)(res.x1 - res.x0);
                    h = (OPJ_UINT32)(res.y1 - res.y0);
                }
                if (h > 0 && uint.MaxValue / w < h)
                {
                    return uint.MaxValue;
                }
                var temp = w * h;
                if (size_comp != 0 && uint.MaxValue / size_comp < temp)
                {
                    return uint.MaxValue;
                }
                temp *= size_comp;

                if (temp > uint.MaxValue - data_size)
                {
                    return uint.MaxValue;
                }
                data_size += temp;
            }

            return data_size;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// 2.1 - opj_tcd_update_tile_data
        /// 
        /// C# Yes, this code haven't been update to 2.5.
        /// 
        /// Org. impl copies to data from an int[] to a byte/short/int[].
        /// 
        /// Nice, but no publically accsible method (in this impl) allows
        /// you to call DecodeTile(xxx, data != null), so this code will
        /// never execute.
        /// </remarks>
        internal bool UpdateTileData(/*byte[] dest*/ int[][] dest)
        {
            //uint data_size = GetDecodedTileSize();

            //if (data_size > dest.Length)
            //    return false;

            TcdTilecomp[] tilecs = _tcd_image.tiles[0].comps;
            var img_comps = _image.comps;
            //int dest_ptr = 0;

            //for (int i = 0; i < img_comps.Length; i++)
            //{
            //    var tilec = tilecs[i];
            //    int nb_elem = ((tilec.x1 - tilec.x0) * (tilec.y1 - tilec.y0));

            //    //C# impl. note. No need for conversion.
            //    Buffer.BlockCopy(tilec.data, 0, dest, dest_ptr * 4, nb_elem * 4);
            //    dest_ptr += nb_elem;
            //}

            for (int i = 0; i < img_comps.Length; i++)
            {
                dest[i] = tilecs[i].data;
            }

            return true;
        }

        //2.5 - opj_tcd_is_subband_area_of_interest
        internal bool IsSubbandAreaOfInterest(
            uint compno,
            uint resno,
            uint bandno,
            uint band_x0,
            uint band_y0,
            uint band_x1,
            uint band_y1)
        {
            /* Note: those values for filter_margin are in part the result of */
            /* experimentation. The value 2 for QMFBID=1 (5x3 filter) can be linked */
            /* to the maximum left/right extension given in tables F.2 and F.3 of the */
            /* standard. The value 3 for QMFBID=0 (9x7 filter) is more suspicious, */
            /* since F.2 and F.3 would lead to 4 instead, so the current 3 might be */
            /* needed to be bumped to 4, in case inconsistencies are found while */
            /* decoding parts of irreversible coded images. */
            /* See opj_dwt_decode_partial_53 and opj_dwt_decode_partial_97 as well */
            uint filter_margin = (_tcp.tccps[compno].qmfbid == 1) ? 2u : 3u;
            var tilec = _tcd_image.tiles[0].comps[compno];
            var image_comp = _image.comps[compno];
            /* Compute the intersection of the area of interest, expressed in tile coordinates */
            /* with the tile coordinates */
            OPJ_UINT32 tcx0 = Math.Max(
                                  (OPJ_UINT32)tilec.x0,
                                  MyMath.uint_ceildiv(_win_x0, image_comp.dx));
            OPJ_UINT32 tcy0 = Math.Max(
                                  (OPJ_UINT32)tilec.y0,
                                  MyMath.uint_ceildiv(_win_y0, image_comp.dy));
            OPJ_UINT32 tcx1 = Math.Min(
                                  (OPJ_UINT32)tilec.x1,
                                  MyMath.uint_ceildiv(_win_x1, image_comp.dx));
            OPJ_UINT32 tcy1 = Math.Min(
                                  (OPJ_UINT32)tilec.y1,
                                  MyMath.uint_ceildiv(_win_y1, image_comp.dy));
            /* Compute number of decomposition for this band. See table F-1 */
            OPJ_UINT32 nb = (resno == 0) ?
                            tilec.numresolutions - 1 :
                            tilec.numresolutions - resno;
            /* Map above tile-based coordinates to sub-band-based coordinates per */
            /* equation B-15 of the standard */
            OPJ_UINT32 x0b = bandno & 1;
            OPJ_UINT32 y0b = bandno >> 1;
            OPJ_UINT32 tbx0 = (nb == 0) ? tcx0 :
                              (tcx0 <= (1U << (int)(nb - 1)) * x0b) ? 0 :
                              MyMath.uint_ceildivpow2(tcx0 - (1U << (int)(nb - 1)) * x0b, (int)nb);
            OPJ_UINT32 tby0 = (nb == 0) ? tcy0 :
                              (tcy0 <= (1U << (int)(nb - 1)) * y0b) ? 0 :
                              MyMath.uint_ceildivpow2(tcy0 - (1U << (int)(nb - 1)) * y0b, (int)nb);
            OPJ_UINT32 tbx1 = (nb == 0) ? tcx1 :
                              (tcx1 <= (1U << (int)(nb - 1)) * x0b) ? 0 :
                              MyMath.uint_ceildivpow2(tcx1 - (1U << (int)(nb - 1)) * x0b, (int)nb);
            OPJ_UINT32 tby1 = (nb == 0) ? tcy1 :
                              (tcy1 <= (1U << (int)(nb - 1)) * y0b) ? 0 :
                              MyMath.uint_ceildivpow2(tcy1 - (1U << (int)(nb - 1)) * y0b, (int)nb);
            bool intersects;

            if (tbx0 < filter_margin)
            {
                tbx0 = 0;
            }
            else
            {
                tbx0 -= filter_margin;
            }
            if (tby0 < filter_margin)
            {
                tby0 = 0;
            }
            else
            {
                tby0 -= filter_margin;
            }
            tbx1 = MyMath.uint_adds(tbx1, filter_margin);
            tby1 = MyMath.uint_adds(tby1, filter_margin);

            intersects = band_x0 < tbx1 && band_y0 < tby1 && band_x1 > tbx0 &&
                         band_y1 > tby0;

            return intersects;
        }

        /// <summary>
        /// Returns whether a tile componenent is fully decoded, taking into account
        /// _win_* members.
        /// </summary>
        /// <param name="compno">Component number</param>
        /// <returns>Whether the tile componenent is fully decoded</returns>
        /// <remarks>
        /// 2.5
        /// </remarks>
        private bool IsWholeTilecompDecoding(OPJ_UINT32 compno)
        {
            var tilec = _tcd_image.tiles[0].comps[compno];
            var image_comp = _image.comps[compno];
            // Compute the intersection of the area of interest, expressed in tile coordinates
            // with the tile coordinates
            OPJ_UINT32 tcx0 = Math.Max(
                                  (OPJ_UINT32)tilec.x0,
                                  MyMath.uint_ceildiv(_win_x0, image_comp.dx));
            OPJ_UINT32 tcy0 = Math.Max(
                                  (OPJ_UINT32)tilec.y0,
                                  MyMath.uint_ceildiv(_win_y0, image_comp.dy));
            OPJ_UINT32 tcx1 = Math.Min(
                                  (OPJ_UINT32)tilec.x1,
                                  MyMath.uint_ceildiv(_win_x1, image_comp.dx));
            OPJ_UINT32 tcy1 = Math.Min(
                                  (OPJ_UINT32)tilec.y1,
                                  MyMath.uint_ceildiv(_win_y1, image_comp.dy));

            int shift = (int) (tilec.numresolutions - tilec.minimum_num_resolutions);
            // Tolerate small margin within the reduced resolution factor to consider if
            // the whole tile path must be taken
            return (tcx0 >= (OPJ_UINT32)tilec.x0 &&
                    tcy0 >= (OPJ_UINT32)tilec.y0 &&
                    tcx1 <= (OPJ_UINT32)tilec.x1 &&
                    tcy1 <= (OPJ_UINT32)tilec.y1 &&
                    (shift >= 32 ||
                     (((tcx0 - (OPJ_UINT32)tilec.x0) >> shift) == 0 &&
                      ((tcy0 - (OPJ_UINT32)tilec.y0) >> shift) == 0 &&
                      (((OPJ_UINT32)tilec.x1 - tcx1) >> shift) == 0 &&
                      (((OPJ_UINT32)tilec.y1 - tcy1) >> shift) == 0)));
        }
    }
}
