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
using System.Diagnostics;
using System.IO;
using Nexaflow.Media.Jpeg2000.Internal;

using OPJ_UINT32 = System.UInt32;
#endregion

namespace Nexaflow.Media.Jpeg2000
{
    /// <summary>
    /// This class keeps track of the code block
    /// encoding/decoding process. 
    /// </summary>
    internal sealed class J2K
    {
        #region Variables and properties

        /// <summary>
        /// Coding parameters
        /// </summary>
        internal CodingParameters CP { get { return _cp; } }

        /// <summary>
        /// I've decided to keep these in a instance
        /// table to ease the situation with callbacks
        /// a little.
        /// </summary>
        readonly DecMstabent _dec_tab;

        /// <summary>
        /// The parent compression info obj.
        /// </summary>
        readonly CompressionInfo _cinfo;

        /// <summary>
        /// Parameters relevant to encoding or decoding
        /// </summary>
        J2KEncOrDec _specific_param;
        /// <summary>
        /// The coding parameters
        /// </summary>
        CodingParameters _cp;

        /// <summary>
        /// Index structure of the codestream
        /// </summary>
        CodestreamIndex _cstr_index;

        /// <summary>
        /// Number of the tile curently concern by coding/decoding
        /// </summary>
        uint _current_tile_number;

        /// <summary>
        /// locate the start position of the SOT marker of the current coded tile:
        /// after encoding the tile, a jump (in j2k_write_sod) is done to the SOT marker to store the value of its length.
        /// </summary>
        //int _sot_start, _sod_start;

        //Decompression only:

        /// <summary>
        /// Image being decoded/encoded (m_private_image)
        /// </summary>
        JPXImage _private_image, _output_image;

        /// <summary>
        /// Stream being decoded/encoded
        /// </summary>
        CIO _cio;
        /// <summary>
        /// Current tile coder/decoder
        /// </summary>
        TileCoder _tcd;

        /// <summary>
        /// Image width coming from JP2 IHDR box. 0 from a pure codestream
        /// </summary>
        public uint _ihdr_w;

        /// <summary>
        /// Image height coming from JP2 IHDR box. 0 from a pure codestream
        /// </summary>
        public uint _ihdr_h;

#if SUPPORT_DUMP_FLAG
        /// <summary>
        /// Used to select what information to dump about the code stream to a file or the console
        /// </summary>
        bool _dump_state;
#endif

        #endregion

        #region Init

        private J2K(CompressionInfo cinfo)
        {
            _cinfo = cinfo;
            _dec_tab = new DecMstabent(this);
            _cp = new CodingParameters();
            
            _specific_param = new J2KEncOrDec();
        }

        internal static J2K Create(CompressionInfo cinfo)
        {
            var j2k = new J2K(cinfo);

            // Default to using strict mode. Set directly, as opj_j2k_create_decompress does: DecoderSetStrictMode
            // also marks the TPsot==TNsot correction as checked, which would turn it off for every codestream.
            j2k._cp.strict = true;

            
            // in the absence of JP2 boxes, consider different bit depth / sign
            // per component is allowed
            j2k._cp.AllowDifferentBitDepthSign = true;

            j2k._specific_param.decoder.header_data = new byte[Constants.J2K_DEFAULT_HEADER_SIZE];
            j2k._specific_param.decoder.header_data_size = Constants.J2K_DEFAULT_HEADER_SIZE;
            j2k._specific_param.decoder.tile_ind_to_dec = -1;
            j2k._specific_param.decoder.last_sot_read_pos = 0;

            j2k._cstr_index = new CodestreamIndex();

            return j2k;
        }

        #endregion

        #region Setup

        //2.5.3 - opj_j2k_setup_decoder
        internal void SetupDecode(CIO cio, DecompressionParameters parameters)
        {
            _cio = cio;
            if (parameters != null)
            {
                _cp.specific_param.dec.layer = (uint)parameters.layer;
                _cp.specific_param.dec.reduce = (uint)parameters.reduce;

#if SUPPORT_DUMP_FLAG
                _dump_state = (parameters.flags & DecompressionParameters.DPARAMETERS.DUMP_FLAG) != 0;
#endif
            }
        }



        #endregion

        #region Validation

        #endregion
        /// <summary>
        /// Merges all PPT markers read (Packed packet headers, tile-part header)
        /// </summary>
        /// <param name="tcp">The tile</param>
        /// <remarks>
        /// 2.5 - opj_j2k_merge_ppt
        /// </remarks>
        private bool MergePPT(TileCodingParams tcp)
        {
            OPJ_UINT32 i, ppt_data_size;

            // preconditions
            Debug.Assert(tcp != null);

            if (tcp.ppt_data != null)
            {
                _cinfo.Error("MergePPT() has already been called");
                return false;
            }

            if (!tcp.ppt)
            {
                return true;
            }

            ppt_data_size = 0;
            for (i = 0U; i < tcp.ppt_markers_count; ++i)
            {
                ppt_data_size +=
                    tcp.ppt_markers[i].DataSize; // can't overflow, max 256 markers of max 65536 bytes
            }

            tcp.ppt_data = new byte[ppt_data_size];
            tcp.ppt_buffer = 0;
            tcp.ppt_len = (int) ppt_data_size;
            ppt_data_size = 0U;
            for (i = 0U; i < tcp.ppt_markers_count; ++i)
            {
                if (tcp.ppt_markers[i].Data != null)
                { // standard doesn't seem to require contiguous Zppt
                    Buffer.BlockCopy(tcp.ppt_markers[i].Data, 0,
                        tcp.ppt_data, tcp.ppt_buffer + (int) ppt_data_size, 
                        (int) tcp.ppt_markers[i].DataSize);
                    ppt_data_size +=
                        tcp.ppt_markers[i].DataSize; // can't overflow, max 256 markers of max 65536 bytes

                    tcp.ppt_markers[i].Data = null;
                }
            }

            tcp.ppt_markers_count = 0;
            tcp.ppt_markers = null;

            tcp.ppt_data_start = tcp.ppt_buffer;
            tcp.ppt_data_size = tcp.ppt_len;
            return true;
        }

        #region Decode image

        //2.5
        void AddTlmarker(OPJ_UINT32 tileno, J2K_Marker type, long pos, OPJ_UINT32 len)
        {
            Debug.Assert(_cstr_index != null);
            Debug.Assert(_cstr_index.tile_index != null);

            // expand the list?
            if ((_cstr_index.tile_index[tileno].marknum + 1) >
                    _cstr_index.tile_index[tileno].maxmarknum)
            {
                _cstr_index.tile_index[tileno].maxmarknum += 100;

                Array.Resize(ref _cstr_index.tile_index[tileno].marker,
                    (int)_cstr_index.tile_index[tileno].maxmarknum);
            }

            /* add the marker */
            _cstr_index.tile_index[tileno].marker[_cstr_index.tile_index[tileno].marknum].type
                = type;
            _cstr_index.tile_index[tileno].marker[_cstr_index.tile_index[tileno].marknum].pos
                = pos;
            _cstr_index.tile_index[tileno].marker[_cstr_index.tile_index[tileno].marknum].len
                = (int)len;
            _cstr_index.tile_index[tileno].marknum++;

            if (type == J2K_Marker.SOT)
            {
                OPJ_UINT32 current_tile_part = _cstr_index.tile_index[tileno].current_tpsno;

                if (_cstr_index.tile_index[tileno].tp_index != null &&
                    current_tile_part < _cstr_index.tile_index[tileno].n_tps)
                {
                    _cstr_index.tile_index[tileno].tp_index[current_tile_part].start_pos = pos;
                }

            }
        }

        //2.5 - opj_j2k_end_decompress
        internal bool EndDecompress()
        {
            return true;
        }

        /// <summary>
        /// Decodes image
        /// </summary>
        /// <param name="image">Dest image</param>
        /// <returns>False on error</returns>
        /// <remarks>2.5 - opj_j2k_decode</remarks>
        internal bool Decode(JPXImage image)
        {
            if (image == null)
                return false;

            // Heuristics to detect sequence opj_read_header(), opj_set_decoded_resolution_factor()
            // and finally opj_decode_image() without manual setting of comps[].factor
            // We could potentially always execute it, if we don't allow people to do
            // opj_read_header(), modify x0,y0,x1,y1 of returned image an call opj_decode_image()
            if (_cp.specific_param.dec.reduce > 0 &&
                _private_image != null &&
                _private_image.numcomps > 0 &&
                _private_image.comps[0].factor == _cp.specific_param.dec.reduce &&
                image.numcomps > 0 &&
                image.comps[0].factor == 0 &&
                /* Don't mess with image dimension if the user has allocated it */
                image.comps[0].data == null)
            {
                // Update the comps[].factor member of the output image with the one
                // of m_reduce
                for (var it_comp = 0; it_comp < image.numcomps; ++it_comp)
                {
                    image.comps[it_comp].factor = _cp.specific_param.dec.reduce;
                }
                if (!UpdateImageDimensions(image))
                {
                    return false;
                }
            }

            if (_output_image == null)
                _output_image = new JPXImage();
            _output_image.CopyImageHeader(image);

            // Decodes the codestream
            if (!DecodeTiles())
            {
                _private_image = null;
                return false;
            }

            //// Move data and copy one information from codec to output image
            //for (int compno = 0; compno < image.numcomps; compno++)
            //{
            //    image.comps[compno].resno_decoded = _output_image.comps[compno].resno_decoded;
            //    image.comps[compno].data = _output_image.comps[compno].data;

            //    _output_image.comps[compno].data = null;
            //}

            return move_data_from_codec_to_output_image(image);
        }

        /// <summary>
        /// Decodes image
        /// </summary>
        /// <param name="image">Dest image</param>
        /// <param name="tile_index">Tile to decode</param>
        /// <returns>False on error</returns>
        /// <remarks>2.5.1 - opj_j2k_get_tile</remarks>
        internal bool Decode(JPXImage image, uint tile_index)
        {
            if (image == null)
            {
                _cinfo.Error("We need an image previously created.");
                return false;
            }

            if (image.numcomps < _private_image.numcomps)
            {
                _cinfo.Error("Image has less components than codestream.");
                return false;
            }

            if ((tile_index >= _cp.tw * _cp.th))
            {
                _cinfo.Error("Tile index provided by the user is incorrect {0} (max = {1}) \n", tile_index,
                              (_cp.tw * _cp.th) - 1);
                return false;
            }

            /* Compute the dimension of the desired tile*/
            uint tile_x = tile_index % _cp.tw;
            uint tile_y = tile_index / _cp.tw;

            image.x0 = tile_x * _cp.tdx + _cp.tx0;
            if (image.x0 < _private_image.x0)
            {
                image.x0 = _private_image.x0;
            }
            image.x1 = (tile_x + 1) * _cp.tdx + _cp.tx0;
            if (image.x1 > _private_image.x1)
            {
                image.x1 = _private_image.x1;
            }

            image.y0 = tile_y * _cp.tdy + _cp.ty0;
            if (image.y0 < _private_image.y0)
            {
                image.y0 = _private_image.y0;
            }
            image.y1 = (tile_y + 1) * _cp.tdy + _cp.ty0;
            if (image.y1 > _private_image.y1)
            {
                image.y1 = _private_image.y1;
            }
            
            for (uint compno = 0; compno < _private_image.numcomps; ++compno)
            {
                int comp_x1, comp_y1;
                var img_comp = image.comps[compno];

                img_comp.factor = _private_image.comps[compno].factor;

                img_comp.x0 = MyMath.uint_ceildiv(image.x0, img_comp.dx);
                img_comp.y0 = MyMath.uint_ceildiv(image.y0, img_comp.dy);
                comp_x1 = MyMath.int_ceildiv((int)image.x1, (int)img_comp.dx);
                comp_y1 = MyMath.int_ceildiv((int)image.y1, (int)img_comp.dy);

                img_comp.w = (OPJ_UINT32)(MyMath.int_ceildivpow2(comp_x1,
                                             (int)img_comp.factor) - MyMath.int_ceildivpow2((int)img_comp.x0,
                                                     (int)img_comp.factor));
                img_comp.h = (OPJ_UINT32)(MyMath.int_ceildivpow2(comp_y1,
                                             (int)img_comp.factor) - MyMath.int_ceildivpow2((int)img_comp.y0,
                                                     (int)img_comp.factor));
            }

            if (image.numcomps > _private_image.numcomps)
            {
                /* Can happen when calling repeatdly opj_get_decoded_tile() on an
                 * image with a color palette, where color palette expansion is done
                 * later in jp2.c */
                for (uint compno = _private_image.numcomps; compno < image.numcomps;
                        ++compno)
                {
                    image.comps[compno].data = null;
                }
                image.numcomps = _private_image.numcomps;
            }

            //Replace the previous output image, create new from information previously computed
            _output_image = new JPXImage();
            _output_image.CopyImageHeader(image);

            _specific_param.decoder.tile_ind_to_dec = (int)tile_index;

            // Decodes the codestream
            if (!DecodeOneTile())
            {
                _private_image = null;
                return false;
            }

            return move_data_from_codec_to_output_image(image);
        }

        //2.5
        private bool move_data_from_codec_to_output_image(JPXImage p_image)
        {
            // Move data and copy one information from codec to output image
            for (OPJ_UINT32 compno = 0; compno < p_image.numcomps; compno++)
            {
                p_image.comps[compno].resno_decoded = _output_image.comps[compno].resno_decoded;
                p_image.comps[compno].data = _output_image.comps[compno].data;

                _output_image.comps[compno].data = null;
            }
            return true;
        }

        //2.5 - opj_j2k_allocate_tile_element_cstr_index
        private void AllocateTileElementCstrIndex()
        {
            _cstr_index.n_of_tiles = _cp.tw * _cp.th;
            _cstr_index.tile_index = new TileIndex[_cstr_index.n_of_tiles];

            for (var it_tile = 0; it_tile < _cstr_index.n_of_tiles; it_tile++)
            {
                _cstr_index.tile_index[it_tile].maxmarknum = 100;
                _cstr_index.tile_index[it_tile].marknum = 0;
                _cstr_index.tile_index[it_tile].marker =
                    new MarkerInfo[_cstr_index.tile_index[it_tile].maxmarknum];
            }
        }

        //2.5 - opj_j2k_are_all_used_components_decoded
        private bool AreAllUsedComponentsDecoded()
        {
            bool decoded_all_used_components = true;

            for (OPJ_UINT32 compno = 0; compno < _output_image.numcomps; compno++)
            {
                if (_output_image.comps[compno].data == null)
                {
                    _cinfo.Warn("Failed to decode component {0}",
                                  compno);
                    decoded_all_used_components = false;
                }
            }

            if (decoded_all_used_components == false)
            {
                _cinfo.Error("Failed to decode all used components");
                return false;
            }

            return true;
        }

        /// <summary>
        /// C#. Org impl uses quicksort. 
        /// </summary>
        private void BubbleSort(long[] arr)
        {
            int n = arr.Length;
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = 0; j < n - i - 1; j++)
                {
                    if (arr[j] > arr[j + 1])
                    {
                        // Swap arr[j] and arr[j + 1]
                        long temp = arr[j];
                        arr[j] = arr[j + 1];
                        arr[j + 1] = temp;
                    }
                }
            }
        }

        /// <summary>
        /// Read and decode one tile.
        /// </summary>
        /// <remarks>2.5.3 - opj_j2k_decode_one_tile</remarks>
        bool DecodeOneTile()
        {
            bool go_on = true;
            uint current_tile_no;
            uint? data_size = null; //Datasize has been removed from 2.5
            int tile_x0, tile_y0, tile_x1, tile_y1;
            uint n_comps;

            // Move into the codestream to the first SOT used to decode the desired tile
            var tile_no_to_dec = (uint)_specific_param.decoder.tile_ind_to_dec;
            if (_cstr_index.tile_index != null)
            {
                if (_cstr_index.tile_index[0].tp_index != null)
                {
                    if (_cstr_index.tile_index[tile_no_to_dec].n_tps == 0)
                    {
                        // the index for this tile has not been built,
                        //  so move to the last SOT read
                        _cio.Pos = _specific_param.decoder.last_sot_read_pos + 2;
                    }
                    else
                    {
                        _cio.Pos = _cstr_index.tile_index[tile_no_to_dec].tp_index[0].start_pos;

                        if ((J2K_Marker)_cio.ReadUShort() != J2K_Marker.SOT)
                        {
                            _cinfo.Error("Did not get expected SOT marker");
                            return false;
                        }
                    }
                    // Special case if we have previously read the EOC marker (if the previous tile getted is the last )
                    if (_specific_param.decoder.state == J2K_STATUS.EOC)
                    {
                        _specific_param.decoder.state = J2K_STATUS.TPHSOT;
                    }
                }
            }

            // Reset current tile part number for all tiles, and not only the one
            // of interest.
            // Not completely sure this is always correct but required for
            // ./build/bin/j2k_random_tile_access ./build/tests/tte1.j2k
            var nb_tiles = _cp.tw * _cp.th;
            for (int i = 0; i < nb_tiles; ++i)
            {
                _cp.tcps[i].current_tile_part_number = -1;
            }

            while(true)
            {
                if (!ReadTileHeader(out current_tile_no,
                                    ref data_size,
                                    out tile_x0, out tile_y0,
                                    out tile_x1, out tile_y1,
                                    out n_comps,
                                    out go_on))
                {
                    return false;
                }

                if (!go_on)
                    break;

                if (!DecodeTile(current_tile_no, null))
                    return false;
                _cinfo.Info("Tile {0}/{1} has been decoded.", current_tile_no + 1, _cp.th * _cp.tw);

                if (!UpdateImageData())
                    return false;
                _cp.tcps[current_tile_no].data = null;
                _cp.tcps[current_tile_no].data_size = 0;

                _cinfo.Info("Image data has been updated with tile {0}.\n", current_tile_no + 1);

                if (current_tile_no == tile_no_to_dec)
                {
                    /* move into the codestream to the first SOT (FIXME or not move?)*/
                    _cio.Pos = _cstr_index.main_head_end + 2;
                    break;
                }
                else
                {
                    _cinfo.Warn("Tile read, decoded and updated is not the desired one ({0} vs {1}).",
                                  current_tile_no + 1, tile_no_to_dec + 1);
                }
            }

            return AreAllUsedComponentsDecoded();
        }

        //2.5.3 - opj_j2k_decode_tiles
        bool DecodeTiles()
        {
            bool go_on = true;
            uint current_tile_no;
            uint? data_size = null; //Datasize has been removed from 2.5 (but C# needs something for the "ref")
            int tile_x0, tile_y0, tile_x1, tile_y1;
            uint n_comps;
            uint nr_tiles = 0;
            long end_pos = 0;

            // Particular case for whole single tile decoding
            // We can avoid allocating intermediate tile buffers
            if (_cp.tw == 1 && _cp.th == 1 &&
                _cp.tx0 == 0 && _cp.ty0 == 0 &&
                _output_image.x0 == 0 &&
                _output_image.y0 == 0 &&
                _output_image.x1 == _cp.tdx &&
                _output_image.y1 == _cp.tdy)
            {
                OPJ_UINT32 i;
                if (!ReadTileHeader(out current_tile_no,
                                    ref data_size, 
                                    out tile_x0, out tile_y0,
                                    out tile_x1, out tile_y1,
                                    out n_comps,
                                    out go_on))
                {
                    return false;
                }
                
                if (!go_on || !DecodeTile(current_tile_no, null))
                {
                    _cinfo.Error("Failed to decode tile 1/1");
                    return false;
                }

                /* Transfer TCD data to output image data */
                for (i = 0; i < _output_image.numcomps; i++)
                {
                    _output_image.comps[i].data =
                        _tcd.TcdImage.tiles[0].comps[i].data;
                    _output_image.comps[i].resno_decoded =
                        _tcd.Image.comps[i].resno_decoded;
                    _tcd.TcdImage.tiles[0].comps[i].data = null;
                }

                return true;
            }

            _specific_param.decoder.num_intersecting_tile_parts = 0;
            _specific_param.decoder.idx_intersecting_tile_parts = 0;
            _specific_param.decoder.intersecting_tile_parts_offset = null;

            // If the area to decode only intersects a subset of tiles, and we have
            // valid TLM information, then use it to plan the tilepart offsets to
            // seek to.
            if (!(_specific_param.decoder.start_tile_x == 0 &&
                  _specific_param.decoder.start_tile_y == 0 &&
                  _specific_param.decoder.end_tile_x == _cp.tw &&
                  _specific_param.decoder.end_tile_y == _cp.th) &&
                 !_specific_param.decoder.tlm.is_invalid &&
                 _cio.CanSeek)
            {
                OPJ_UINT32 m_num_intersecting_tile_parts = 0;

                OPJ_UINT32 j;
                for (j = 0; j < _cp.tw * _cp.th; ++j)
                {
                    if (_cstr_index.tile_index[j].n_tps > 0 &&
                        _cstr_index.tile_index[j].tp_index[_cstr_index.tile_index[j].n_tps - 1].end_pos > end_pos)
                    {
                        end_pos = _cstr_index.tile_index[j].tp_index[_cstr_index.tile_index[j].n_tps - 1].end_pos;
                    }
                }

                for (j = _specific_param.decoder.start_tile_y; j < _specific_param.decoder.end_tile_y; ++j)
                {
                    OPJ_UINT32 i;
                    for (i = _specific_param.decoder.start_tile_x; i < _specific_param.decoder.end_tile_x; ++i)
                    {
                        OPJ_UINT32 tile_number = j * _cp.tw + i;
                        m_num_intersecting_tile_parts += _cstr_index.tile_index[tile_number].n_tps;
                    }
                }

                _specific_param.decoder.intersecting_tile_parts_offset = new long[m_num_intersecting_tile_parts];
                if (m_num_intersecting_tile_parts > 0)
                {
                    OPJ_UINT32 idx = 0;
                    for (j = _specific_param.decoder.start_tile_y; j < _specific_param.decoder.end_tile_y; ++j)
                    {
                        OPJ_UINT32 i;
                        for (i = _specific_param.decoder.start_tile_x; i < _specific_param.decoder.end_tile_x; ++i)
                        {
                            OPJ_UINT32 tile_number = j * _cp.tw + i;
                            OPJ_UINT32 k;
                            for (k = 0; k < _cstr_index.tile_index[tile_number].n_tps; ++k)
                            {
                                var next_tp_sot_pos = _cstr_index.tile_index[tile_number].tp_index[k].start_pos;
                                _specific_param.decoder.intersecting_tile_parts_offset[idx] = next_tp_sot_pos;
                                ++idx;
                            }
                        }
                    }

                    _specific_param.decoder.num_intersecting_tile_parts = idx;

                    // Sort by increasing offset
                    BubbleSort(_specific_param.decoder.intersecting_tile_parts_offset);
                }
            }

            while (true)
            {
                if (_cp.tw == 1 && _cp.th == 1 &&
                    _cp.tcps[0].data != null)
                {
                    current_tile_no = 0;
                    _current_tile_number = 0;
                    _specific_param.decoder.state |= J2K_STATUS.DATA;
                } else
                {
                    if (!ReadTileHeader(out current_tile_no, 
                                        ref data_size, 
                                        out tile_x0, out tile_y0,
                                        out tile_x1, out tile_y1, 
                                        out n_comps, 
                                        out go_on))
                        return false;

                    if (!go_on)
                        break;
                }

                if (!DecodeTile(current_tile_no, null))
                {
                    _cinfo.Error("Failed to decode tile {0}/{1}", 
                        current_tile_no + 1, _cp.th * _cp.tw);
                    return false;
                }

                _cinfo.Info("Tile {0}/{1} has been decoded.", 
                    current_tile_no + 1, _cp.th * _cp.tw);

                if (!UpdateImageData())
                    return false;

                if (_cp.tw == 1 && _cp.th == 1 &&
                        !(_output_image.x0 == _private_image.x0 &&
                          _output_image.y0 == _private_image.y0 &&
                          _output_image.x1 == _private_image.x1 &&
                          _output_image.y1 == _private_image.y1))
                {
                    /* Keep current tcp data */
                }
                else
                {
                    _cp.tcps[current_tile_no].data = null;
                }

                _cinfo.Info("Image data has been updated with tile {0}.", current_tile_no + 1);

                if (_cio.BytesLeft == 0 && _specific_param.decoder.state == J2K_STATUS.NEOC)
                    break;

                if (++nr_tiles == _cp.th * _cp.tw)
                    break;

                if (_specific_param.decoder.num_intersecting_tile_parts > 0 &&
                    _specific_param.decoder.idx_intersecting_tile_parts == _specific_param.decoder.num_intersecting_tile_parts)
                {
                    _cio.Pos = end_pos + 2;
                    break;
                }
            }

            return AreAllUsedComponentsDecoded();
        }

        //2.5
        bool UpdateImageData()
        {
            uint width_src, height_src;
            uint width_dest, height_dest;
            int offset_x0_src, offset_y0_src, offset_x1_src, offset_y1_src;
            long start_offset_src;
            uint start_x_dest, start_y_dest;
            uint x0_dest, y0_dest, x1_dest, y1_dest;
            long start_offset_dest;

            //C# impl note.
            //tilec is set insde the for loop, because in the org. impl it's a
            //incrementing pointer.
            //Same story with img_comp_src and img_comp_dest
            for (int i = 0; i < _tcd.Image.numcomps; i++)
            {
                int res_x0, res_x1, res_y0, res_y1;
                uint src_data_stride;
                int[] src_data;

                var img_comp_src = _tcd.Image.comps[i];
                var img_comp_dest = _output_image.comps[i];
                var tilec = _tcd.TcdImage.tiles[0].comps[i];

                //Copy info from decoded comp image to output image
                img_comp_dest.resno_decoded = img_comp_src.resno_decoded;

                if (_tcd.WholeTileDecoding)
                {
                    var res = tilec.resolutions[img_comp_src.resno_decoded];
                    res_x0 = res.x0;
                    res_y0 = res.y0;
                    res_x1 = res.x1;
                    res_y1 = res.y1;
                    src_data_stride = (uint)(
                        tilec.resolutions[tilec.minimum_num_resolutions - 1].x1 -
                        tilec.resolutions[tilec.minimum_num_resolutions - 1].x0);
                    src_data = tilec.data;
                }
                else
                {
                    var res = tilec.resolutions[img_comp_src.resno_decoded];
                    res_x0 = (int)res.win_x0;
                    res_y0 = (int)res.win_y0;
                    res_x1 = (int)res.win_x1;
                    res_y1 = (int)res.win_y1;
                    src_data_stride = res.win_x1 - res.win_x0;
                    src_data = tilec.data_win;
                }

                if (src_data == null)
                {
                    // Happens for partial component decoding
                    continue;
                }


                //Current tile component size
                width_src = (uint)(res_x1 - res_x0);
                height_src = (uint)(res_y1 - res_y0);

                //Border of the current output component
                x0_dest = MyMath.uint_ceildivpow2(img_comp_dest.x0, (int)img_comp_dest.factor);
                y0_dest = MyMath.uint_ceildivpow2(img_comp_dest.y0, (int)img_comp_dest.factor);
                x1_dest = x0_dest + img_comp_dest.w; // can't overflow given that image.x1 is uint32
                y1_dest = y0_dest + img_comp_dest.h;

                /* Compute the area (offset_x0_src, offset_y0_src, offset_x1_src, offset_y1_src)
                 * of the input buffer (decoded tile component) which will be move
                 * in the output buffer. Compute the area of the output buffer (start_x_dest,
                 * start_y_dest, width_dest, height_dest)  which will be modified
                 * by this input area.
                 * */
                if (x0_dest < res_x0)
                {
                    start_x_dest = (uint) (res_x0 - x0_dest);
                    offset_x0_src = 0;

                    if (x1_dest >= res_x1)
                    {
                        width_dest = width_src;
                        offset_x1_src = 0;
                    }
                    else
                    {
                        width_dest = (uint)(x1_dest - res_x0);
                        offset_x1_src = (int)(width_src - width_dest);
                    }
                }
                else
                {
                    start_x_dest = 0;
                    offset_x0_src = (int)(x0_dest - res_x0);

                    if (x1_dest >= res_x1)
                    {
                        width_dest = width_src - (uint)offset_x0_src;
                        offset_x1_src = 0;
                    }
                    else
                    {
                        width_dest = img_comp_dest.w;
                        offset_x1_src = res_x1 - (int)x1_dest;
                    }
                }

                if (y0_dest < res_y0)
                {
                    start_y_dest = (uint)(res_y0 - y0_dest);
                    offset_y0_src = 0;

                    if (y1_dest >= res_y1)
                    {
                        height_dest = height_src;
                        offset_y1_src = 0;
                    }
                    else
                    {
                        height_dest = y1_dest - (uint)res_y0;
                        offset_y1_src = (int)(height_src - height_dest);
                    }
                }
                else
                {
                    start_y_dest = 0;
                    offset_y0_src = (int)(y0_dest - res_y0);

                    if (y1_dest >= res_y1)
                    {
                        height_dest = height_src - (uint)offset_y0_src;
                        offset_y1_src = 0;
                    }
                    else
                    {
                        height_dest = img_comp_dest.h;
                        offset_y1_src = res_y1 - (int)y1_dest;
                    }
                }

                if ((offset_x0_src < 0) || (offset_y0_src < 0) || (offset_x1_src < 0) || (offset_y1_src < 0))
                {
                    return false;
                }

                if ((int)width_dest < 0 || (int)height_dest < 0)
                {
                    return false;
                }

                //Compute the input buffer offset
                start_offset_src = offset_x0_src + offset_y0_src * src_data_stride;

                //Compute the output buffer offset
                start_offset_dest = start_x_dest + start_y_dest * img_comp_dest.w;

                //Allocate output component buffer if necessary
                if (img_comp_dest.data == null &&
                    start_offset_src == 0 && start_offset_dest == 0 &&
                    src_data_stride == img_comp_dest.w &&
                    width_dest == img_comp_dest.w &&
                    height_dest == img_comp_dest.h)
                {
                    // If the final image matches the tile buffer, then borrow it
                    // directly to save a copy
                    if (_tcd.WholeTileDecoding)
                    {
                        img_comp_dest.data = tilec.data;
                        tilec.data = null;
                    }
                    else
                    {
                        img_comp_dest.data = tilec.data_win;
                        tilec.data_win = null;
                    }
                    continue;
                }
                else if (img_comp_dest.data == null)
                {
                    var width = img_comp_dest.w;
                    var height = img_comp_dest.h;

                    if ((height == 0) || (width > (Constants.SIZE_MAX / height)) ||
                            width * height > (Constants.SIZE_MAX / 4))
                    {
                        // will overflow
                        return false;
                    }
                    img_comp_dest.data = new int[width * height];

                    //C# Commented out. Arrays are always zeroed on creation, so this
                    //                  is already done.
                    //if (img_comp_dest.w != width_dest ||
                    //    img_comp_dest.h != height_dest)
                    //{
                    //    var data = img_comp_dest.data;
                    //    Array.Clear(data, 0, (int)(img_comp_dest.w * img_comp_dest.h));
                    //}
                }

                //Move the output buffer to the first place where we will write
                var dest_ptr = start_offset_dest;
                var dest_data = img_comp_dest.data;

                {
                    int src_ptr = (int)start_offset_src;

                    for (int j = 0; j < height_dest; j++)
                    {
                        Buffer.BlockCopy(src_data, src_ptr * 4, dest_data, (int)(dest_ptr * 4), (int)(width_dest * 4));
                        dest_ptr += img_comp_dest.w;
                        src_ptr += (int)src_data_stride;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Decode one tile
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_j2k_decode_tile
        /// 
        /// Note, in the org_impl the methods:
        ///    opj_jp2_decode_tile
        ///    opj_j2k_decode_tile
        /// calls this method with data != null, those two methods do not
        /// exist in this impl.
        /// 
        /// If someone wants to decode a particular tile, they can still do
        /// that through other means, and if someone later wish to use this
        /// functionality, the way it's implemented now saves you from 
        /// calculating the size of the buffer. 
        /// </remarks>
        bool DecodeTile(OPJ_UINT32 tile_index, /*byte[] data*/ int[][] data)
        {
            if ((_specific_param.decoder.state & J2K_STATUS.DATA) == 0 ||
                tile_index != _current_tile_number)
                return false;

            var tcp = _cp.tcps[tile_index];
            if (tcp.data == null)
                return false;

            var image_for_bounds = _output_image != null ? _output_image : _private_image;
            if (!_tcd.DecodeTile(
                image_for_bounds.x0,
                image_for_bounds.y0,
                image_for_bounds.x1,
                image_for_bounds.y1,
                tcp.data,
                tcp.data_size,
                tile_index,
                _cstr_index))
            {
                _cinfo.Error("Failed to decode.");
                return false;
            }

            // p_data can be set to NULL when the call will take care of using
            // itself the TCD data. This is typically the case for whole single
            // tile decoding optimization.
            if (data != null)
            {
                _tcd.UpdateTileData(data);
            }

            _specific_param.decoder.can_decode = false;
            _specific_param.decoder.state &= (J2K_STATUS) (~(0x0080));

            if (_cio.BytesLeft == 0 && _specific_param.decoder.state == J2K_STATUS.NEOC)
                return true;

            if (_specific_param.decoder.state != J2K_STATUS.EOC)
            {
                var current_marker = (J2K_Marker)_cio.ReadUShort();
                if (current_marker == J2K_Marker.EOC)
                {
                    _current_tile_number = 0;
                    _specific_param.decoder.state = J2K_STATUS.EOC;
                }
                else if (current_marker != J2K_Marker.SOT)
                {
                    _cinfo.Error("Stream too short, expected SOT");

                    if (_cio.BytesLeft == 0)
                    {
                        _specific_param.decoder.state = J2K_STATUS.NEOC;
                        return true;
                    }
                    return false;
                }
            }

            return true;
        }

        //2.5
        private bool NeedNTilePartsCorrection(OPJ_UINT32 tile_no, out bool p_correction_needed)
        {
            long l_stream_pos_backup;
            J2K_Marker l_current_marker;
            OPJ_UINT32 l_marker_size;
            OPJ_UINT32 l_tile_no, l_tot_len, l_current_part, l_num_parts;

            // initialize to no correction needed
            p_correction_needed = false;

            if (!_cio.CanSeek)
            {
                // We can't do much in this case, seek is needed
                return true;
            }

            l_stream_pos_backup = _cio.Pos;
            
            for (; ; )
            {
                // Try to read 2 bytes (the next marker ID) from stream and copy them into the buffer
                if (_cio.BytesLeft < 2)
                {
                    // Assume all is OK
                    return true;
                }

                l_current_marker = (J2K_Marker) _cio.ReadUShort();

                if (l_current_marker != J2K_Marker.SOT)
                {
                    // Assume all is OK
                    _cio.Pos = l_stream_pos_backup;
                    return true;
                }

                // Try to read 2 bytes (the marker size) from stream
                if (_cio.BytesLeft < 2)
                {
                    _cinfo.Error("Stream too short");
                    return false;
                }
                l_marker_size = _cio.ReadUShort();

                /* Check marker size for SOT Marker */
                if (l_marker_size != 10)
                {
                    _cinfo.Error("Inconsistent marker size");
                    return false;
                }
                l_marker_size -= 2;

                if (_cio.BytesLeft < l_marker_size)
                {
                    _cinfo.Error("Stream too short");
                    return false;
                }

                if (!GetSOTValues(l_marker_size, 
                                  out l_tile_no,
                                  out l_tot_len, 
                                  out l_current_part, 
                                  out l_num_parts))
                {
                    return false;
                }

                if (l_tile_no == tile_no)
                {
                    /* we found what we were looking for */
                    break;
                }

                if (l_tot_len < 14U)
                {
                    // last SOT until EOC or invalid Psot value
                    // assume all is OK
                    _cio.Pos = l_stream_pos_backup;
                    return true;
                }
                l_tot_len -= 12U;
                /* look for next SOT marker */
                _cio.Skip(l_tot_len);
            }

            /* check for correction */
            if (l_current_part == l_num_parts)
            {
                p_correction_needed = true;
            }

            _cio.Pos = l_stream_pos_backup;
            return true;
        }

        //2.5.3 - opj_j2k_read_tile_header
        bool ReadTileHeader(out uint tile_index, ref uint? data_size, out int tile_x0, out int tile_y0,
            out int tile_x1, out int tile_y1, out uint n_comps, out bool go_on)
        {
            J2K_Marker current_marker = J2K_Marker.SOT;
            tile_index = n_comps = 0;
            tile_x0 = tile_x1 = tile_y0 = tile_y1 = 0;
            go_on = false;

            // Reach the End Of Codestream ?
            if (_specific_param.decoder.state == J2K_STATUS.EOC)
                current_marker = J2K_Marker.EOC;
            // We need to encounter a SOT marker (a new tile-part header)
            else if (_specific_param.decoder.state != J2K_STATUS.TPHSOT)
            {
                return false;
            }

            //Read into the codestream until reach the EOC
            while (!_specific_param.decoder.can_decode && current_marker != J2K_Marker.EOC)
            {
                if (_specific_param.decoder.num_intersecting_tile_parts > 0 &&
                    _specific_param.decoder.idx_intersecting_tile_parts <
                    _specific_param.decoder.num_intersecting_tile_parts)
                {
                    var next_tp_sot_pos = _specific_param.decoder.intersecting_tile_parts_offset[_specific_param.decoder.idx_intersecting_tile_parts];
                    ++_specific_param.decoder.idx_intersecting_tile_parts;
                    _cio.Pos = next_tp_sot_pos;

                    current_marker = (J2K_Marker)_cio.ReadUShort();
                    
                    if (current_marker != J2K_Marker.SOT)
                    {
                        _cinfo.Error("Did not get expected SOT marker");
                        return false;
                    }
                }

                //Try to read until the Start Of Data is detected
                while (current_marker != J2K_Marker.SOD)
                {
                    if (_cio.BytesLeft == 0)
                    {
                        _specific_param.decoder.state = J2K_STATUS.NEOC;
                        break;
                    }

                    //Size of this marker
                    uint marker_size = _cio.ReadUShort();

                    //Check marker size (does not include marker ID but includes marker size)
                    if (marker_size < 2)
                    {
                        _cinfo.Error("Inconsistent marker size");
                        return false;
                    }

                    //cf. https://code.google.com/p/openjpeg/issues/detail?id=226
                    if (current_marker == (J2K_Marker) 0x8080 && _cio.BytesLeft == 0)
                    { //testcase_2325.jp2
                        _specific_param.decoder.state = J2K_STATUS.NEOC;
                        break;
                    }

                    if (((_specific_param.decoder.state & J2K_STATUS.TPH) != 0) &&
                          _specific_param.decoder.sot_length != 0)
                    {
                        if (_specific_param.decoder.sot_length < marker_size + 2)
                        {
                            _cinfo.Error("Sot length is less than marker size + marker ID");
                            return false;
                        }
                        _specific_param.decoder.sot_length -= marker_size + 2;
                    }
                    marker_size -= 2; // Subtract the size of the marker ID already read

                    var handler = _dec_tab[current_marker];

                    if ((_specific_param.decoder.state & handler.States) == 0 || handler.Handler == null)
                    {
                        if (handler.Handler == null)
                            _cinfo.Error("Not sure how that happened");
                        else
                            _cinfo.Error("Marker is not compliant with its position");
                        return false;
                    }

                    //Check if the marker size is compatible with the header data size
                    if (marker_size > _specific_param.decoder.header_data_size)
                    { 
                        // If we are here, this means we consider this marker as known & we will read it
                        // Check enough bytes left in stream before allocation 
                        if (marker_size > _cio.BytesLeft)
                        {
                            _cinfo.Error("Marker size inconsistent with stream length");
                            return false;
                        }
                        Array.Resize(ref _specific_param.decoder.header_data, (int)marker_size);
                        _specific_param.decoder.header_data_size = (int) marker_size;
                    }

                    if (!handler.Handler(marker_size))
                    {
                        _cinfo.Error("Fail to read the current marker segment ({0:X})", current_marker);
                        return false;
                    }

                    //Add the marker to the codestream index
                    AddTlmarker(_current_tile_number, handler.Mark, _cio.Pos - marker_size - 4, marker_size + 4);

                    //Keep the position of the last SOT marker read
                    if (handler.Mark == J2K_Marker.SOT)
                    {
                        var sot_pos = _cio.Pos - marker_size - 4;
                        if (sot_pos > _specific_param.decoder.last_sot_read_pos)
                            _specific_param.decoder.last_sot_read_pos = sot_pos;
                    }

                    if (_specific_param.decoder.skip_data)
                    {
                        //Skip the rest of the tile part header
                        _cio.Skip(_specific_param.decoder.sot_length);
                        current_marker = J2K_Marker.SOD;
                    }
                    else
                    {
                        current_marker = (J2K_Marker)_cio.ReadUShort();
                    }
                }
                if (_cio.BytesLeft == 0 && _specific_param.decoder.state == J2K_STATUS.NEOC)
                    break;

                //If we didn't skip data before, we need to read the SOD marker
                if (!_specific_param.decoder.skip_data)
                {
                    if (!ReadSOD())
                    {
                        return false;
                    }

                    // Check if we can use the TLM index to access the next tile-part
                    if (!_specific_param.decoder.can_decode &&
                         _specific_param.decoder.tile_ind_to_dec >= 0 &&
                         _current_tile_number == (OPJ_UINT32) _specific_param.decoder.tile_ind_to_dec &&
                        !_specific_param.decoder.tlm.is_invalid &&
                        _cio.CanSeek)
                    {
                        var tcp = _cp.tcps[_current_tile_number];
                        if (tcp.n_tile_parts ==
                                _cstr_index.tile_index[_current_tile_number].n_tps &&
                                (OPJ_UINT32)tcp.current_tile_part_number + 1 < tcp.n_tile_parts)
                        {
                            long next_tp_sot_pos = _cstr_index.tile_index[_current_tile_number]
                                  .tp_index[tcp.current_tile_part_number + 1].start_pos;

                            if (next_tp_sot_pos != _cio.Pos)
                            {
                                _cio.Pos = next_tp_sot_pos;
                            }

                            current_marker = (J2K_Marker) _cio.ReadUShort();

                            if (current_marker != J2K_Marker.SOT)
                            {
                                _cinfo.Error("Did not get expected SOT marker");
                                return false;
                            }

                            continue;
                        }
                    }

                    if (_specific_param.decoder.can_decode &&
                        !_specific_param.decoder.n_tile_parts_correction_checked)
                    {
                        // Issue 254
                        bool correction_needed = false;

                        _specific_param.decoder.n_tile_parts_correction_checked = true;
                        if (_cp.tcps[_current_tile_number].n_tile_parts == 1)
                        {
                            /* Skip opj_j2k_need_nb_tile_parts_correction() if there is
                             * only a single tile part declared. The
                             * opj_j2k_need_nb_tile_parts_correction() hack was needed
                             * for files with 5 declared tileparts (where they were
                             * actually 6).
                             * Doing it systematically hurts performance when reading
                             * Sentinel2 L1C JPEG2000 files as explained in
                             * https://lists.osgeo.org/pipermail/gdal-dev/2024-November/059805.html
                             */
                        }
                        else if (!NeedNTilePartsCorrection(_current_tile_number, out correction_needed))
                        {
                            _cinfo.Error("opj_j2k_apply_nb_tile_parts_correction error");
                            return false;
                        }

                        if (correction_needed)
                        {
                            OPJ_UINT32 tile_no;
                            OPJ_UINT32 n_tiles = _cp.tw * _cp.th;

                            _specific_param.decoder.can_decode = false;
                            _specific_param.decoder.n_tile_parts_correction = true;
                            // correct tiles
                            for (tile_no = 0; tile_no < n_tiles; ++tile_no)
                            {
                                if (_cp.tcps[tile_no].n_tile_parts != 0)
                                {
                                    _cp.tcps[tile_no].n_tile_parts += 1;
                                }
                            }
                            _cinfo.Warn("Non conformant codestream TPsot==TNsot.");
                        }
                    }
                }
                else
                {
                    //Indicate we will try to read a new tile-part header
                    _specific_param.decoder.skip_data = false;
                    _specific_param.decoder.can_decode = false;
                    _specific_param.decoder.state = J2K_STATUS.TPHSOT;
                }

                if (!_specific_param.decoder.can_decode)
                {

                    if (_cio.BytesLeft < 2)
                    {
                        // Deal with likely non conformant SPOT6 files, where the last
                        // row of tiles have TPsot == 0 and TNsot == 0, and missing EOC,
                        // but no other tile-parts were found.
                        OPJ_UINT32 n_tiles = _cp.tw * _cp.th;
                        if (_current_tile_number + 1 == n_tiles)
                        {
                            OPJ_UINT32 l_tile_no;
                            for (l_tile_no = 0U; l_tile_no < n_tiles; ++l_tile_no)
                            {
                                if (_cp.tcps[l_tile_no].current_tile_part_number == 0 &&
                                        _cp.tcps[l_tile_no].n_tile_parts == 0)
                                {
                                    break;
                                }
                            }
                            if (l_tile_no < n_tiles)
                            {
                                _cinfo.Info("Tile {0} has TPsot == 0 and TNsot == 0, "+
                                            "but no other tile-parts were found. "+
                                            "EOC is also missing.",
                                              l_tile_no);
                                _current_tile_number = l_tile_no;
                                current_marker = J2K_Marker.EOC;
                                _specific_param.decoder.state = J2K_STATUS.EOC;
                                break;
                            }
                        }

                        _cinfo.Error("Stream too short");
                        return false;
                    }

                    current_marker = (J2K_Marker)_cio.ReadUShort();
                }
            }

            if (current_marker == J2K_Marker.EOC)
            {
                if (_specific_param.decoder.state != J2K_STATUS.EOC)
                {
                    _current_tile_number = 0;
                    _specific_param.decoder.state = J2K_STATUS.EOC;
                }
            }

            if (!_specific_param.decoder.can_decode)
            {
                var tcps = _cp.tcps;
                int n_tiles = (int)(_cp.th * _cp.tw);

                while (_current_tile_number < n_tiles && tcps[_current_tile_number].data == null)
                    _current_tile_number++;

                if (_current_tile_number == n_tiles)
                {
                    return true;
                }
            }

            if (!MergePPT(_cp.tcps[_current_tile_number]))
            {
                _cinfo.Error("Failed to merge PPT data");
                return false;
            }

            if (!_tcd.InitDecodeTile(_current_tile_number))
            {
                _cinfo.Error("Cannot decode tile, memory error");
                return false;
            }

            _cinfo.Info("Header of tile {0} / {1} has been read.",
                _current_tile_number + 1, (_cp.th * _cp.tw));

            tile_index = _current_tile_number;
            go_on = true;
            if (data_size != null)
            {
                data_size = _tcd.GetDecodedTileSize(false);
                if (data_size.Value == uint.MaxValue)
                {
                    return false;
                }
            }
            
            tile_x0 = _tcd.TcdImage.tiles[0].x0;
            tile_y0 = _tcd.TcdImage.tiles[0].y0;
            tile_x1 = _tcd.TcdImage.tiles[0].x1;
            tile_y1 = _tcd.TcdImage.tiles[0].y1;
            n_comps = _tcd.TcdImage.tiles[0].numcomps;

            _specific_param.decoder.state |= (J2K_STATUS) 0x0080; //J2K_DEC_STATE_DATA

            return true;
        }

        //2.5 - opj_j2k_decoding_validation
        bool DecodingValidation()
        {
            return _specific_param.decoder.state == J2K_STATUS.NONE; 
        }

        void BuildTPIndexFromTLM()
        {
            var tlm = _specific_param.decoder.tlm;
            if (tlm == null)
            {
                tlm = new TlmInfo();
                _specific_param.decoder.tlm = tlm;
            }

            if (tlm.entries_count == 0 || tlm.is_invalid)
            {
                tlm.is_invalid = true;
                return;
            }

            // Initial pass to count the number of tile-parts per tile
            for (int i = 0; i < tlm.entries_count; ++i)
            {
                OPJ_UINT32 l_tile_index_no = tlm.tile_part_infos[i].tile_index;
                Debug.Assert(l_tile_index_no < _cstr_index.n_of_tiles);
                _cstr_index.tile_index[l_tile_index_no].tileno = l_tile_index_no;
                ++_cstr_index.tile_index[l_tile_index_no].current_n_tps;
            }

            // Now check that all tiles have at least one tile-part
            for (int i = 0; i < _cstr_index.n_of_tiles; ++i)
            {
                if (_cstr_index.tile_index[i].current_n_tps == 0)
                {
                    _cinfo.Error("opj_j2k_build_tp_index_from_tlm(): tile {0} has no "+
                                  "registered tile-part in TLM marker segments.", i);
                    goto error;
                }
            }

            // Final pass to fill p_j2k->cstr_index
            var cur_offset = _cstr_index.main_head_end;
            for (int i = 0; i < tlm.entries_count; ++i)
            {
                OPJ_UINT32 tile_index_no = tlm.tile_part_infos[i].tile_index;
                var tile_index = _cstr_index.tile_index;
                if (tile_index[tile_index_no].tp_index == null)
                {
                    tile_index[tile_index_no].tp_index = new TPIndex[tile_index[tile_index_no].current_n_tps];
                }

                Debug.Assert(tile_index[tile_index_no].n_tps < tile_index[tile_index_no].current_n_tps);
                tile_index[tile_index_no].tp_index[tile_index[tile_index_no].n_tps].start_pos = cur_offset;
                // We don't know how to set the tp_index[].end_header field, but this is not really needed
                // If there would be no markers between SOT and SOD, that would be :
                // l_tile_index->tp_index[l_tile_index->nb_tps].end_header = l_cur_offset + 12;
                tile_index[tile_index_no].tp_index[tile_index[tile_index_no].n_tps].end_pos = cur_offset +
                        tlm.tile_part_infos[i].length;
                ++tile_index[tile_index_no].n_tps;

                cur_offset += tlm.tile_part_infos[i].length;
            }

            return;

        error:
            tlm.is_invalid = true;
            for (int i = 0; i < tlm.entries_count; ++i)
            {
                OPJ_UINT32 tile_index = tlm.tile_part_infos[i].tile_index;
                _cstr_index.tile_index[tile_index].current_n_tps = 0;
                _cstr_index.tile_index[tile_index].tp_index = null;
            }
        }

        //2.5.3 - opj_j2k_read_header
        internal bool ReadHeader(out JPXImage image)
        {
            // create an empty image header
            _private_image = new JPXImage();

            if (!DecodingValidation())
            {
                image = null;
                return false;
            }

            if (!ReadHeaderProcedure())
            {
                image = null;
                return false;
            }

            if (!CopyDefaultTCPandCreateTcd())
            {
                image = null;
                return false;
            }

            image = new JPXImage();
            image.CopyImageHeader(_private_image);

            return true;
        }

        //2.5
        bool CopyDefaultTCPandCreateTcd()
        {
            int n_tiles = (int) (_cp.th * _cp.tw);
            TileCodingParams[] tcps = _cp.tcps;
            var default_tcp = _specific_param.decoder.default_tcp;

            // For each tile
            for (int i = 0; i < n_tiles; i++)
            {
                var tcp = tcps[i];

                //Keeps the the tile-compo coding parameters
                TileCompParams[] tccps = tcp.tccps;

                //Copies default coding parameters into the current tile coding parameters
                tcp = (TileCodingParams) default_tcp.Clone();
                tcps[i] = tcp;

                //Initialize some values of the current tile coding parameters
                tcp.cod = false;
                tcp.ppt = false;
                tcp.ppt_data = null;
                tcp.current_tile_part_number = -1;
                //Remove memory not owned by this tile in case of early error return.
                tcp.mct_decoding_matrix = null;
                tcp.n_max_mct_records = 0;
                tcp.mct_records = null;
                tcp.n_max_mcc_records = 0;
                tcp.mcc_records = null;

                //Reconnect the tile-compo coding parameters pointer to the current tile coding parameters
                tcp.tccps = tccps;

                //Get the mct_decoding_matrix of the dflt_tile_cp and copy them into the current tile cp
                if (default_tcp.mct_decoding_matrix != null)
                {
                    tcp.mct_decoding_matrix = (float[])default_tcp.mct_decoding_matrix.Clone();
                }

                //Get the mct_record of the dflt_tile_cp and copy them into the current tile cp
                tcp.mct_records = new MctData[default_tcp.mct_records.Length];
                //Snip memcopy. What it does is essentially zero everything,
                //then it copies over data in the following loop. We do the
                //zeroing in the following loop. 

                //Copy the mct record data from dflt_tile_cp to the current tile
                // Notice, c < tcp.mct_records.Length, not c < n_mct_records
                for (int c = 0; c < tcp.mct_records.Length; c++)
                {
                    var mct = default_tcp.mct_records[c];
                    mct = mct == null ? new MctData() : (MctData)mct.Clone();
                    tcp.mct_records[c] = mct;
                }
                tcp.n_max_mct_records = default_tcp.n_mct_records;

                //Get the mcc_record of the dflt_tile_cp and copy them into the current tile cp
                tcp.mcc_records = new SimpleMccDecorrelationData[default_tcp.n_max_mcc_records];
                tcp.n_max_mcc_records = default_tcp.n_max_mcc_records;
                //Snip memcopy. Again, we do the work in the following loop instead.

                //Copy the mcc record data from dflt_tile_cp to the current tile
                for (int c = 0; c < tcp.mcc_records.Length; c++)
                {
                    var mcc = tcp.mcc_records[c];
                    mcc = mcc == null ? new SimpleMccDecorrelationData() : (SimpleMccDecorrelationData)mcc.Clone();
                    tcp.mcc_records[c] = mcc;
                }

                //Copy all the dflt_tile_compo_cp to the current tile cp
                for (int c = 0; c < _private_image.numcomps; c++)
                    tccps[c] = (TileCompParams) default_tcp.tccps[c].Clone();
            }

            //Create the current tile decoder
            _tcd = new TileCoder(_cinfo, _private_image, _cp);

            return true;
        }

        //2.5.3 - opj_j2k_read_header_procedure
        bool ReadHeaderProcedure()
        {
            _specific_param.decoder.state = J2K_STATUS.MHSOC;

            if (!ReadSOC())
            {
                _cinfo.Error("Expected a SOC marker ");
                return false;
            }

            J2K_Marker current_marker = (J2K_Marker) _cio.ReadUShort();
            bool has_siz = false;
            bool has_cod = false;
            bool has_qcd = false;

            while (current_marker != J2K_Marker.SOT)
            {
                // Check if the current marker ID is valid
                if (current_marker < J2K_Marker.FIRST)
                {
                    _cinfo.Error("We expected to read a marker ID (0xff--) instead of 0x{0:X}", current_marker);
                    return false;
                }

                J2kMarker e = _dec_tab[current_marker];

                // Manage unknown marker
                if (e.Mark == J2K_Marker.NONE)
                {
                    if (!ReadUNK(out current_marker))
                    {
                        _cinfo.Error("Unknow marker has been detected and generated error.");
                        return false;
                    }

                    if (current_marker == J2K_Marker.SOT)
                        break; //marker is detected main header is completely read
                    else
                        e = _dec_tab[current_marker];
                }

                switch(e.Mark)
                {
                    case J2K_Marker.SIZ:
                        has_siz = true;
                        break;
                    case J2K_Marker.COD:
                        has_cod = true;
                        break;
                    case J2K_Marker.QCD:
                        has_qcd = true;
                        break;
                }

                // Check if the marker is known and if it is the right place to find it
                if ((_specific_param.decoder.state & e.States) == J2K_STATUS.NONE)
                {
                    _cinfo.Error("Marker is not compliant with its position");
                    return false;
                }

                uint marker_size = _cio.ReadUShort();
                if (marker_size < 2)
                {
                    _cinfo.Error("Invalid marker size");
                    return false;
                }
                marker_size -= 2; // Subtract the size of the marker ID already read

                //Check if the marker size is compatible with the header data size
                if (marker_size > _specific_param.decoder.header_data_size)
                {
                    _specific_param.decoder.header_data = new byte[marker_size];
                    _specific_param.decoder.header_data_size = (int)marker_size;
                }

                if (!e.Handler(marker_size))
                {
                    _cinfo.Error("Marker handler function failed to read the marker segment");
                    return false;
                }

                current_marker = (J2K_Marker)_cio.ReadUShort();
            }

            if (!has_siz)
            {
                _cinfo.Error("required SIZ marker not found in main header");
                return false;
            }
            if (!has_cod)
            {
                _cinfo.Error("required COD marker not found in main header");
                return false;
            }
            if (!has_qcd)
            {
                _cinfo.Error("required QCD marker not found in main header");
                return false;
            }

            if (!MergePPM())
            {
                _cinfo.Error("Failed to merge PPM data");
                return false;
            }

            _cinfo.Info("Main header has been correctly decoded");

            // Position of the last element if the main header
            _cstr_index.main_head_end = _cio.Pos - 2;

            BuildTPIndexFromTLM();

            // Next step: read a tile-part header
            _specific_param.decoder.state = J2K_STATUS.TPHSOT;

            return true;
        }

        /// <summary>
        /// Reads a SOC marker (Start of Codestream)
        /// </summary>
        /// <returns>True on success</returns>
        /// <remarks>
        /// 2.5
        /// </remarks>
        internal bool ReadSOC()
        {
            if (_cio.ReadUShort() != (ushort)J2K_Marker.SOC)
            {
                return false;
            }

            _specific_param.decoder.state = J2K_STATUS.MHSIZ;

            // FIXME move it in a index structure included in p_j2k
            _cstr_index.main_head_start = _cio.Pos - 2;

            _cinfo.Info("Start to read j2k main header ({0})", 
                _cstr_index.main_head_start);

            return true;
        }

        //2.5 - opj_j2k_get_sot_values
        private bool GetSOTValues(OPJ_UINT32 header_size,
                                  out OPJ_UINT32 tile_no,
                                  out OPJ_UINT32 tot_len,
                                  out OPJ_UINT32 current_part,
                                  out OPJ_UINT32 num_parts)
        {
            // Size of this marker is fixed = 12 (we have already read marker and its size)
            if (header_size != 8 || _cio.BytesLeft < 8)
            {
                _cinfo.Error("Error reading SOT marker");
                tile_no = tot_len = current_part = num_parts = 0;
                return false;
            }

            tile_no = _cio.ReadUShort();    // Isot
            tot_len = _cio.ReadUInt();      // Psot
            current_part = _cio.ReadByte(); // TPsot
            num_parts = _cio.ReadByte();    // TNsot
            return true;
        }

        //2.5.3 - opj_j2k_read_sot
        internal bool ReadSOT(OPJ_UINT32 header_size)
        {
            uint tot_len, current_part, num_parts;

            if (!GetSOTValues(header_size, out _current_tile_number, out tot_len, out current_part, out num_parts))
                return false;

            // testcase 2.pdf.SIGFPE.706.1112
            if (_current_tile_number >= _cp.tw * _cp.th)
            {
                _cinfo.Error("Invalid tile number {0}", _current_tile_number);
                return false;
            }

            var tcp = _cp.tcps[_current_tile_number];
            uint tile_x = _current_tile_number % _cp.tw;
            uint tile_y = _current_tile_number / _cp.tw;


            if (_specific_param.decoder.tile_ind_to_dec < 0 ||
                _current_tile_number == (OPJ_UINT32)
                _specific_param.decoder.tile_ind_to_dec)
            {
                // Do only this check if we decode all tile part headers, or if
                // we decode one precise tile. Otherwise the m_current_tile_part_number
                // might not be valid
                // Fixes issue with id_000020,sig_06,src_001958,op_flip4,pos_149
                // of https://github.com/uclouvain/openjpeg/issues/939
                // We must avoid reading twice the same tile part number for a given tile
                // so as to avoid various issues, like opj_j2k_merge_ppt being called
                // several times.
                // ISO 15444-1 A.4.2 Start of tile-part (SOT) mandates that tile parts
                // should appear in increasing order.
                if (tcp.current_tile_part_number + 1 != (int)current_part)
                {
                    _cinfo.Error("Invalid tile part index for tile number {0}. "+
                                  "Got {1}, expected {2}",
                                  _current_tile_number,
                                  current_part,
                                  tcp.current_tile_part_number + 1);
                    return false;
                }
            }

            tcp.current_tile_part_number = (int) current_part;

            // look for the tile in the list of already processed tile (in parts).
            // Optimization possible here with a more complex data structure and with the removing of tiles
            // since the time taken by this function can only grow at the time

            if (tot_len != 0 && tot_len < 14)
            {
                if (tot_len == 12) //Special case for the PHR data which are read by kakadu
                    _cinfo.Warn("Empty SOT marker detected: Psot={0}.", tot_len);
                else
                {
                    _cinfo.Error("Psot value is not correct regards to the JPEG2000 norm: {0}.", tot_len);
                    return false;
                }
            }

            //Ref A.4.2: Psot could be equal zero if it is the last tile-part of the codestream.
            if (tot_len == 0)
            {
                _cinfo.Info("Psot value of the current tile-part is equal to zero, "+
                     "we assuming it is the last tile-part of the codestream.");
                _specific_param.decoder.last_tile_part = true;
            }

            if (tcp.n_tile_parts != 0 && current_part >= tcp.n_tile_parts)
            {
                // Fixes https://bugs.chromium.org/p/oss-fuzz/issues/detail?id=2851
                _cinfo.Error("In SOT marker, TPSot ({0}) is not valid regards to the current " +
                             "number of tile-part ({1}), giving up", current_part, tcp.n_tile_parts);
                _specific_param.decoder.last_tile_part = true;
                return false;
            }


            if (num_parts != 0)
            {
                //Number of tile-part header is provided by this tile-part header
                num_parts += _specific_param.decoder.n_tile_parts_correction ? 1u : 0u;
                // Useful to manage the case of textGBR.jp2 file because two values of TNSot are allowed: the correct numbers of
                // tile-parts for that tile and zero (A.4.2 of 15444-1 : 2002).

                if (tcp.n_tile_parts != 0)
                {
                    if (current_part >= tcp.n_tile_parts)
                    {
                        _cinfo.Error("In SOT marker, TPSot ({0}) is not valid regards to the current " +
                                     "number of tile-part ({1}), giving up", current_part, tcp.n_tile_parts);
                        _specific_param.decoder.last_tile_part = true;
                        return false;
                    }
                }

                if (current_part >= num_parts)
                {
                    // testcase 451.pdf.SIGSEGV.ce9.3723
                    _cinfo.Error("In SOT marker, TPSot ({0}) is not valid regards to the current " +
                        "number of tile-part (header) ({0}), giving up", current_part, num_parts);
                    _specific_param.decoder.last_tile_part = true;
                    return false;
                }
                tcp.n_tile_parts = num_parts;
            }

            //If know the number of tile part header we will check if we didn't read the last
            if (tcp.n_tile_parts != 0)
            {
                if (tcp.n_tile_parts == current_part + 1)
                {
                    //Process the last tile-part header
                    _specific_param.decoder.can_decode = true;
                }
            }

            if (!_specific_param.decoder.last_tile_part)
            {
                //Keep the size of data to skip after this marker
                _specific_param.decoder.sot_length = (OPJ_UINT32)(tot_len - 12);
            }
            else
            {
                /* FIXME: need to be computed from the number of bytes remaining in the codestream */
                _specific_param.decoder.sot_length = 0;
            }
            _specific_param.decoder.state = J2K_STATUS.TPH;

            //Check if the current tile is outside the area we want decode or not corresponding to the tile index
            if (_specific_param.decoder.tile_ind_to_dec == -1)
            {
                _specific_param.decoder.skip_data =
                    tile_x < _specific_param.decoder.start_tile_x ||
                    tile_x >= _specific_param.decoder.end_tile_x ||
                    tile_y < _specific_param.decoder.start_tile_y ||
                    tile_y >= _specific_param.decoder.end_tile_y;
            }
            else
            {
                _specific_param.decoder.skip_data = _current_tile_number != _specific_param.decoder.tile_ind_to_dec;
            }

            Debug.Assert(_cstr_index != null && _cstr_index.tile_index != null);
            {
                _cstr_index.tile_index[_current_tile_number].tileno = _current_tile_number;
                _cstr_index.tile_index[_current_tile_number].current_tpsno = current_part;

                if (!_specific_param.decoder.tlm.is_invalid &&
                    num_parts > _cstr_index.tile_index[_current_tile_number].n_tps)
                {
                    _cinfo.Warn("SOT marker for tile {0} declares more tile-parts than found in TLM marker.",
                        _current_tile_number);
                    _specific_param.decoder.tlm.is_invalid = true;
                }
                
                if (!_specific_param.decoder.tlm.is_invalid)
                {
                    // Do nothing
                }
                else if (num_parts != 0)
                {
                    _cstr_index.tile_index[_current_tile_number].n_tps = num_parts;
                    _cstr_index.tile_index[_current_tile_number].current_n_tps = num_parts;

                    if (_cstr_index.tile_index[_current_tile_number].tp_index == null)
                        _cstr_index.tile_index[_current_tile_number].tp_index = new TPIndex[num_parts];
                    else
                        Array.Resize(ref _cstr_index.tile_index[_current_tile_number].tp_index, (int) num_parts);
                }
                else
                {
                    {
                        if (_cstr_index.tile_index[_current_tile_number].tp_index == null)
                        {
                            const int SIZE = 10;
                            _cstr_index.tile_index[_current_tile_number].current_n_tps = SIZE;
                            _cstr_index.tile_index[_current_tile_number].tp_index = new TPIndex[SIZE];
                        }

                        if (current_part >=
                                _cstr_index.tile_index[_current_tile_number].current_n_tps)
                        {
                            _cstr_index.tile_index[_current_tile_number].current_n_tps = current_part + 1;
                            Array.Resize(ref _cstr_index.tile_index[_current_tile_number].tp_index,
                                (int) _cstr_index.tile_index[_current_tile_number].current_n_tps);
                        }
                    }

                }
            }

            return true;
        }

        //2.5.3 - opj_j2k_read_sod
        internal bool ReadSOD()
        {
            TileCodingParams tcp = _cp.tcps[_current_tile_number];
            bool sot_length_pb_detected = false;

            if (_specific_param.decoder.last_tile_part)
            {
                /* opj_stream_get_number_byte_left returns OPJ_OFF_T
                // but we are in the last tile part,
                // so its result will fit on OPJ_UINT32 unless we find
                // a file with a single tile part of more than 2 GB...*/
                _specific_param.decoder.sot_length = (uint)(_cio.BytesLeft - 2);
            }
            else
            {
                if (_specific_param.decoder.sot_length >= 2)
                    _specific_param.decoder.sot_length -= 2;
            }

            int current_read_size;
            byte[] current_data = tcp.data;
            int tile_len = tcp.data_size;
            
            if (_specific_param.decoder.sot_length != 0)
            {
                if (_specific_param.decoder.sot_length > _cio.BytesLeft)
                {
                    var str = "Tile part length size inconsistent with stream length";
                    if (_cp.strict)
                    {
                        _cinfo.Error(str);
                        return false;
                    }
                    else
                    {
                        _cinfo.Warn(str);
                    }
                }

                if (_specific_param.decoder.sot_length > 
                    int.MaxValue - Constants.COMMON_CBLK_DATA_EXTRA)
                {
                    _cinfo.Error("j2k._specific_param.decoder.sot_length > " +
                                 "INT_MAX - COMMON_CBLK_DATA_EXTRA");
                    return false;
                }

                if (current_data == null)
                {
                    current_data = new byte[_specific_param.decoder.sot_length + Constants.COMMON_CBLK_DATA_EXTRA];
                    tcp.data = current_data;
                }
                else
                {
                    if (tile_len > int.MaxValue - Constants.COMMON_CBLK_DATA_EXTRA -
                        _specific_param.decoder.sot_length)
                    {
                        _cinfo.Error("tile_len > INT_MAX - COMMON_CBLK_DATA_EXTRA -" +
                            "j2k._specific_param.decoder.sot_length");
                        return false;
                    }

                    Array.Resize<byte>(ref current_data, (int)(tile_len + _specific_param.decoder.sot_length + 
                                                         Constants.COMMON_CBLK_DATA_EXTRA));
                    tcp.data = current_data;
                }
            }
            else
            {
                sot_length_pb_detected = true;
            }

            // Index
            Debug.Assert(_cstr_index != null);
            {
                long current_pos = _cio.Pos - 2;

                OPJ_UINT32 current_tile_part =
                    _cstr_index.tile_index[_current_tile_number].current_tpsno;
                _cstr_index.tile_index[_current_tile_number].tp_index[current_tile_part].end_header
                    =
                        current_pos;
                _cstr_index.tile_index[_current_tile_number].tp_index[current_tile_part].end_pos
                    =
                        current_pos + _specific_param.decoder.sot_length + 2;

                AddTlmarker(_current_tile_number, J2K_Marker.SOD, current_pos,
                    _specific_param.decoder.sot_length + 2);
            }

            // Patch to support new PHR data
            if (!sot_length_pb_detected)
                current_read_size = _cio.Read(current_data, tile_len, (int)_specific_param.decoder.sot_length);
            else
                current_read_size = 0;

            if (current_read_size != _specific_param.decoder.sot_length)
                _specific_param.decoder.state = J2K_STATUS.NEOC;
            else
                _specific_param.decoder.state = J2K_STATUS.TPHSOT;

            //Org impl. sets l_tile_len, but that is a pointer at tcp.data_size
            tcp.data_size += current_read_size;

            return true;
        }

        /// <summary>
        /// Reads a TLM marker (Tile Length Marker)
        /// </summary>
        /// <param name="header_size">
        /// the size of the data contained in the TLM marker.
        /// </param>
        /// <remarks>
        /// 2.5.3 - opj_j2k_read_tlm
        /// 
        /// In the original impl. all the data is read before this
        /// function is called. Here, it's read piecewise from the
        /// cio stream.
        /// </remarks>
        internal bool ReadTLM(uint header_size)
        {
            TlmInfo tlm = _specific_param.decoder.tlm;
            if (tlm == null)
            {
                tlm = new TlmInfo();
                _specific_param.decoder.tlm = tlm;
            }

            if (header_size < 2)
            {
                _cinfo.Error("Error reading TLM marker.");
                return false;
            }

            if (tlm.is_invalid)
            {
                _cio.Skip(header_size);
                return true;
            }

            uint Ztlm = _cio.ReadByte();
            uint Stlm = _cio.ReadByte();
            header_size -= 2;

            uint ST = ((Stlm >> 4) & 0x03);
            if (ST == 3)
            {
                tlm.is_invalid = true;
                _cinfo.Warn("opj_j2k_read_tlm(): ST = 3 is invalid.");
                _cio.Skip(header_size);
                return true;
            }
            uint SP = (Stlm >> 6) & 0x01;

            uint Ptlm_size = (SP + 1) * 2;
            uint entry_size = Ptlm_size + ST;

            if (header_size % entry_size != 0)
            {
                tlm.is_invalid = true;
                _cinfo.Warn("opj_j2k_read_tlm(): TLM marker not of expected size.");
                _cio.Skip(header_size);
                return true;
            }

            var num_tileparts = header_size / entry_size;
            if (num_tileparts == 0)
            {
                // not totally sure if this is valid...
                _cio.Skip(header_size);
                return true;
            }

            // Highly unlikely, unless there are gazillions of TLM markers
            if (tlm.entries_count > uint.MaxValue - num_tileparts ||
                tlm.entries_count + num_tileparts > uint.MaxValue / 8)
            {
                tlm.is_invalid = true;
                _cinfo.Warn("opj_j2k_read_tlm(): too many TLM markers.");
                _cio.Skip(header_size);
                return true;
            }

            // C#: In the org impl, if there's not enough memory, it will skip reading TLMs
            //     Here we will crash.
            //     Also note, this is a struct.
            tlm.tile_part_infos = new TlmTilePartInfo[tlm.entries_count + num_tileparts];


            for (int i=0; i < num_tileparts; i++)
            {
                uint tile_index;

                // Read Ttlm_i
                if (ST == 0)
                    tile_index = tlm.entries_count;
                else
                {
                    tile_index = _cio.Read(ST);
                    header_size -= ST;
                }
                
                if (tile_index >= _cp.tw * _cp.th)
                {
                    tlm.is_invalid = true;
                    _cinfo.Warn("opj_j2k_read_tlm(): invalid tile number {0}", tile_index);
                    _cio.Skip(header_size);
                    return true;
                }

                uint length = _cio.Read(Ptlm_size);
                header_size -= Ptlm_size;

                tlm.tile_part_infos[tlm.entries_count].tile_index =
                    (ushort)tile_index;
                tlm.tile_part_infos[tlm.entries_count].length = length;
                tlm.entries_count++;
            }

            _cio.Skip(header_size);
            return true;
        }

        /// <summary>
        /// Reads a PLM marker (Packet length, main header marker)
        /// </summary>
        /// <remarks>2.5 - opj_j2k_read_plm</remarks>
        internal bool ReadPLM(uint header_size)
        {
            if (header_size < 1)
            {
                _cinfo.Error("Error reading PLM marker");
                return false;
            }
            //int Zplm = _cio.ReadByte();
            _cio.Skip(header_size);
            return true;
        }

        /// <summary>
        /// Reads a PLT marker (Packet length, tile-part header)
        /// </summary>
        /// <remarks>2.5 - opj_j2k_read_plt</remarks>
        internal bool ReadPLT(uint header_size)
        {
            if (header_size < 1)
            {
                _cinfo.Error("Error reading PLT marker");
                return false;
            }

            int Zplt = _cio.ReadByte();
            int packet_len = 0;
            header_size--;
            for (int i = 0; i < header_size; i++)
            {
                int add = _cio.ReadByte();
                packet_len |= add & 0x7f;
                if ((add & 0x80) != 0)
                    packet_len <<= 7;
                else
                    packet_len = 0;
            }

            if (packet_len != 0)
            {
                _cinfo.Error("Error reading PLT marker");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads a PPM marker (Packed packet headers, main header)
        /// </summary>
        /// <remarks>
        /// 2.1 - opj_j2k_read_ppm
        /// 
        /// Uppgrading to 2.5 isn't trivial, so we added an error check 
        /// (_cp.ppm_data == null) and called it a day.
        /// </remarks>
        internal bool ReadPPM(uint header_size)
        {
            // We need to have the Z_ppm element + 1 byte of Nppm/Ippm at minimum
            if (header_size < 2)
            {
                _cinfo.Error("Error reading PPM marker");
                return false;
            }

            _cp.ppm = true;

            int Z_ppm = _cio.ReadByte();
            header_size--;

            // Check allocation needed
            // The following code is for 2.5
            if (_cp.ppm_markers == null)
            {//This is the first PPM;
                int new_count = Z_ppm + 1; // can't overflow, l_Z_ppm is byte

                _cp.ppm_markers = new PPX[new_count];
                _cp.ppm_markers_count = (uint)new_count;
            }
            else if (_cp.ppm_markers_count <= Z_ppm)
            {
                int new_count = Z_ppm + 1; // can't overflow, l_Z_ppm is byte
                Array.Resize(ref _cp.ppm_markers, new_count);
                _cp.ppm_markers_count = (uint)new_count;
            }

            if (_cp.ppm_markers[Z_ppm].Data != null)
            {
                _cinfo.Error("Zppm {0} already read", Z_ppm);
                return false;
            }

            _cp.ppm_markers[Z_ppm].Data = new byte[header_size];
            //_cp.ppm_markers[Z_ppm].DataSize = header_size;
            _cio.Read(_cp.ppm_markers[Z_ppm].Data, 0, (int)header_size);

            return true;
        }

        /// <summary>
        /// Merges all PPM markers read (Packed headers, main header)
        /// </summary>
        /// <remarks>
        /// 2.5.1 - opj_j2k_merge_ppm
        /// </remarks>
        internal bool MergePPM()
        {
            uint i, ppm_data_size, N_ppm_remaining;

            if (!_cp.ppm)
                return true;

            ppm_data_size = 0;
            N_ppm_remaining = 0;
            for (i = 0U; i < _cp.ppm_markers_count; ++i)
            {
                if (_cp.ppm_markers[i].Data != null)
                { // standard doesn't seem to require contiguous Zppm
                    OPJ_UINT32 N_ppm;
                    CIO data = new CIO(_cinfo, new MemoryStream(_cp.ppm_markers[i].Data), OpenMode.Read);

                    if (N_ppm_remaining >= data.BytesLeft)
                    {
                        N_ppm_remaining -= (uint) data.BytesLeft;
                        data.Skip((uint)data.BytesLeft);
                    }
                    else
                    {
                        data.Skip(N_ppm_remaining);
                        N_ppm_remaining = 0U;
                    }

                    if (data.BytesLeft > 0U)
                    {
                        do
                        {
                            /* read Nppm */
                            if (data.BytesLeft < 4)
                            {
                                /* clean up to be done on l_cp destruction */
                                _cinfo.Error("Not enough bytes to read Nppm");
                                return false;
                            }
                            N_ppm = data.ReadUInt();

                            if (ppm_data_size > uint.MaxValue - N_ppm)
                            {
                                _cinfo.Error("Too large value for Nppm\n");
                                return false;
                            }
                            ppm_data_size += N_ppm;
                            if (data.BytesLeft >= N_ppm)
                            {
                                data.Skip(N_ppm);
                            }
                            else
                            {
                                N_ppm_remaining = N_ppm - (uint) data.BytesLeft;
                                data.Skip((uint) data.BytesLeft);
                            }
                        } while (data.BytesLeft > 0);
                    }
                }
            }

            if (N_ppm_remaining != 0U)
            {
                /* clean up to be done on l_cp destruction */
                _cinfo.Error("Corrupted PPM markers");
                return false;
            }

            var ppm_buffer =  new byte[ppm_data_size];
            _cp.ppm_buffer_pt = 0;
            _cp.ppm_len = ppm_data_size;
            ppm_data_size = 0U;
            N_ppm_remaining = 0U;
            for (i = 0U; i < _cp.ppm_markers_count; ++i)
            {
                if (_cp.ppm_markers[i].Data != null)
                { /* standard doesn't seem to require contiguous Zppm */
                    OPJ_UINT32 N_ppm;
                    int data_size = (int) _cp.ppm_markers[i].DataSize;
                    byte[] data = _cp.ppm_markers[i].Data;
                    int data_pt = 0;

                    if (N_ppm_remaining >= data_size)
                    {
                        Buffer.BlockCopy(data, data_pt, ppm_buffer, _cp.ppm_buffer_pt + (int)ppm_data_size, (int) data_size);
                        ppm_data_size += (uint) data_size;
                        N_ppm_remaining -= (uint) data_size;
                        data_size = 0;
                    }
                    else
                    {
                        Buffer.BlockCopy(data, data_pt, ppm_buffer, _cp.ppm_buffer_pt + (int)ppm_data_size, (int)N_ppm_remaining);
                        ppm_data_size += N_ppm_remaining;
                        data_pt += (int) N_ppm_remaining;
                        data_size -= (int) N_ppm_remaining;
                        N_ppm_remaining = 0;
                    }

                    if (data_size > 0)
                    {
                        do
                        {
                            /* read Nppm */
                            if (data_size < 4)
                            {
                                /* clean up to be done on l_cp destruction */
                                _cinfo.Error("Not enough bytes to read Nppm");
                                return false;
                            }
                            N_ppm = CIO.ReadUInt(data, data_pt);
                            data_pt += 4;
                            data_size -= 4;

                            if (data_size >= N_ppm)
                            {
                                Buffer.BlockCopy(data, data_pt, ppm_buffer, _cp.ppm_buffer_pt + (int)ppm_data_size, (int)N_ppm);
                                ppm_data_size += N_ppm;
                                data_size -= (int) N_ppm;
                                data_pt += (int) N_ppm;
                            }
                            else
                            {
                                Buffer.BlockCopy(data, data_pt, ppm_buffer, _cp.ppm_buffer_pt + (int)ppm_data_size, (int)data_size);
                                ppm_data_size += (uint) data_size;
                                N_ppm_remaining = N_ppm - (uint) data_size;
                                data_size = 0;
                            }
                        } while (data_size > 0);
                    }
                    _cp.ppm_markers[i].Data = null;
                }
            }

            _cp.ppm_data = ppm_buffer;
            //_cp.ppm_data_size = _cp.ppm_len;

            _cp.ppm_markers = null;
            _cp.ppm_markers_count = 0U;

            return true;
        }

        /// <summary>
        /// Reads a PPT marker (Packed packet headers, tile-part header)
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_j2k_read_ppt
        /// </remarks>
        internal bool ReadPPT(uint header_size)
        {
            //We need to have the Z_ppt element at minimum
            if (header_size < 1)
            {
                _cinfo.Error("Error reading PPT marker");
                return false;
            }

            if (_cp.ppm)
            {
                _cinfo.Error("Error reading PPT marker: packet header have been previously found in the main header (PPM marker).");
                return false;
            }

            TileCodingParams tcp = _cp.tcps[_current_tile_number];
            tcp.ppt = true;

            int Z_ppt = _cio.ReadByte();
            header_size--;

            //Allocate buffer to read the packet header
            if (tcp.ppt_markers == null)
            {   // First PPT marker
                int count = Z_ppt + 1;
                tcp.ppt_markers = new PPX[count];
                tcp.ppt_markers_count = (uint)count;
            }
            else
            {   // NON-first PPT marker
                int count = Z_ppt + 1;
                Array.Resize(ref tcp.ppt_markers, count);
                Array.Clear(tcp.ppt_markers, (int)tcp.ppt_markers_count, count - (int)tcp.ppt_markers_count);
                tcp.ppt_markers_count = (uint)count;
            }

            if (tcp.ppt_markers[Z_ppt].Data != null)
            {
                _cinfo.Error("Zppt {0} already read", Z_ppt);
                return false;
            }

            tcp.ppt_markers[Z_ppt].Data = new byte[header_size];

            //Read packet header
            _cio.Read(tcp.ppt_markers[Z_ppt].Data, 0, (int)header_size);

            return true;
        }

        /// <summary>
        /// Reads a SIZ marker(image and tile size)
        /// </summary>
        /// <param name="header_size">The size of the data contained in the SIZ marker</param>
        /// <remarks>
        /// 2.5.3 - opj_j2k_read_siz
        /// </remarks>
        internal bool ReadSIZ(uint header_size)
        {
            // minimum size == 39 - 3 (= minimum component parameter)
            if (header_size < 36)
            {
                _cinfo.Error("Error with SIZ marker size");
                return false;
            }

            uint remaining_size = header_size - 36;
            uint n_comps = (uint)remaining_size / 3u;
            if (remaining_size % 3 != 0)
            {
                _cinfo.Error("Error with SIZ marker size");
                return false;
            }

            _cp.rsiz = (J2K_PROFILE) _cio.ReadUShort();

            _private_image.x1 = _cio.ReadUInt();
            _private_image.y1 = _cio.ReadUInt();
            _private_image.x0 = _cio.ReadUInt();
            _private_image.y0 = _cio.ReadUInt();
            _cp.tdx = _cio.ReadUInt();
            _cp.tdy = _cio.ReadUInt();
            _cp.tx0 = _cio.ReadUInt();
            _cp.ty0 = _cio.ReadUInt();
            {
                var tmp = _cio.ReadUShort();

                if (tmp < 16385)
                {
                    _private_image.numcomps = tmp;
                } 
                else
                {
                    _cinfo.Error("Error with SIZ marker: number of component is illegal -> {0}", tmp);
                    return false;
                }
            }

            if (_private_image.numcomps != n_comps)
            {
                _cinfo.Error("Error with SIZ marker: number of component is not compatible with the remaining number of parameters ({0} vs {1})", _private_image.numcomps, n_comps);
                return false;
            }

            if (_private_image.x0 >= _private_image.x1 || _private_image.y0 >= _private_image.y1)
            {
                _cinfo.Error("Error with SIZ marker: negative image size ({0} x {1})", (int)(_private_image.x1 - _private_image.x0), (int)(_private_image.y1 - _private_image.y0));
                return false;
            }

            if (_cp.tdx == 0 || _cp.tdy == 0)
            {
                _cinfo.Error("Error with SIZ marker: invalid tile size (tdx: {0}, tdy: {1})", _cp.tdx, _cp.tdy);
                return false;
            }

            
            var tx1 = MyMath.uint_adds(_cp.tx0, _cp.tdx); // manage overflow
            var ty1 = MyMath.uint_adds(_cp.ty0, _cp.tdy); // manage overflow
            if ((_cp.tx0 > _private_image.x0) || (_cp.ty0 > _private_image.y0) ||
                    (tx1 <= _private_image.x0) || (ty1 <= _private_image.y0))
            {
                _cinfo.Error("Error with SIZ marker: illegal tile offset");
                return false;
            }
#if SUPPORT_DUMP_FLAG
            if (!_dump_state)
#endif
            {
                uint siz_w = (uint) (_private_image.x1 - _private_image.x0);
                uint siz_h = (uint) (_private_image.y1 - _private_image.y0);

                if (_ihdr_w > 0 && _ihdr_h > 0
                        && (_ihdr_w != siz_w || _ihdr_h != siz_h))
                {
                    _cinfo.Error("Error with SIZ marker: IHDR w({0}) h({1}) vs. SIZ w({2}) h({3})", _ihdr_w,
                                  _ihdr_h, siz_w, siz_h);
                    return false;
                }
            }

            // Allocate the resulting image components
            _private_image.comps = new ImageComp[_private_image.numcomps];

            // Read the component information
            OPJ_UINT32 prec0 = 0;
            bool sgnd0 = false;
            for (int i = 0; i < _private_image.comps.Length; i++)
            {
                var comp = new ImageComp();
                uint tmp = _cio.ReadByte();
                comp.prec = (tmp & 0x7f) + 1;
                comp.sgnd = (tmp >> 7) == 1;
#if SUPPORT_DUMP_FLAG
                if (!_dump_state)
#endif
                {
                    if (i == 0)
                    {
                        prec0 = comp.prec;
                        sgnd0 = comp.sgnd;
                    }
                    else if (!_cp.AllowDifferentBitDepthSign
                               && (comp.prec != prec0 || comp.sgnd != sgnd0))
                    {
                        _cinfo.Warn("Despite JP2 BPC!=255, precision and/or sgnd values for comp[{0}] is different than comp[0]:\n"+
                                      "        [0] prec({1}) sgnd({2}) [{3}] prec({4}) sgnd({5})", i, prec0, sgnd0,
                                      i, comp.prec, comp.sgnd);
                    }
                    // TODO: we should perhaps also check against JP2 BPCC values
                }

                comp.dx = _cio.ReadByte();
                comp.dy = _cio.ReadByte();

                // C# note: Org impl. checks if > 255, which is pointless.
                if (comp.dx == 0 || comp.dy == 0)
                {
                    _cinfo.Error("Invalid values for comp = {0} : dx={1} dy={2}\n (should be between 1 and 255 according the JPEG2000 norm)", i, comp.dx, comp.dy);
                    return false;
                }

                /* Avoids later undefined shift in computation of */
                /* p_j2k->m_specific_param.m_decoder.m_default_tcp->tccps[i].m_dc_level_shift = 1
                            << (l_image->comps[i].prec - 1); */
                if (comp.prec > 31)
                {
                    _cinfo.Error("Invalid values for comp = {0} : prec={1} (should be between 1 and 38 according to the JPEG2000 norm. OpenJpeg only supports up to 31)",
                        i, comp.prec);
                    return false;
                }

                comp.resno_decoded = 0; // number of resolution decoded
                comp.factor = _cp.specific_param.dec.reduce; // reducing factor per component

                _private_image.comps[i] = comp;
            }

            if (_cp.tdx == 0 || _cp.tdy == 0)
            {
                return false;
            }

            //Computes the number of tiles
            _cp.tw = (uint) MyMath.uint_ceildiv(_private_image.x1 - _cp.tx0, _cp.tdx);
            _cp.th = (uint) MyMath.uint_ceildiv(_private_image.y1 - _cp.ty0, _cp.tdy);

            //Check that the number of tiles is valid
            if (_cp.tw == 0 || _cp.th == 0 || _cp.tw > 65535 / _cp.th)
            {
                _cinfo.Error("Invalid number of tiles : {0} x {1} (maximum fixed by jpeg2000 norm is 65535 tiles)", _cp.tw, _cp.th);
                return false;
            }
            uint n_tiles = _cp.tw * _cp.th;

            //Define the tiles which will be decoded
            if (_specific_param.decoder.discard_tiles)
            {
                _specific_param.decoder.start_tile_x = ((_specific_param.decoder.start_tile_x - _cp.tx0) / _cp.tdx);
                _specific_param.decoder.start_tile_y = ((_specific_param.decoder.start_tile_y - _cp.ty0) / _cp.tdy);
                _specific_param.decoder.end_tile_x = MyMath.uint_ceildiv(_specific_param.decoder.end_tile_x - _cp.tx0, _cp.tdx);
                _specific_param.decoder.end_tile_y = MyMath.uint_ceildiv(_specific_param.decoder.end_tile_y - _cp.ty0, _cp.tdy);
            }
            else
            {
                _specific_param.decoder.start_tile_x = 0;
                _specific_param.decoder.start_tile_y = 0;
                _specific_param.decoder.end_tile_x = _cp.tw;
                _specific_param.decoder.end_tile_y = _cp.th;
            }

            // memory allocations
            _cp.tcps = new TileCodingParams[n_tiles];
            _specific_param.decoder.default_tcp = new TileCodingParams();
            _specific_param.decoder.default_tcp.tccps = TileCompParams.Create(_private_image.numcomps);
            _specific_param.decoder.default_tcp.mct_records = new MctData[Constants.MCT_DEFAULT_NB_RECORDS];
            _specific_param.decoder.default_tcp.n_max_mct_records = Constants.MCT_DEFAULT_NB_RECORDS;
            _specific_param.decoder.default_tcp.mcc_records = new SimpleMccDecorrelationData[Constants.MCC_DEFAULT_NB_RECORDS];
            _specific_param.decoder.default_tcp.n_max_mcc_records = Constants.MCC_DEFAULT_NB_RECORDS;

            // Set up default dc level shift
            for (int i = 0; i < _private_image.numcomps; i++)
            {
                if (!_private_image.comps[i].sgnd)
                    _specific_param.decoder.default_tcp.tccps[i].dc_level_shift = 1 << ((int)_private_image.comps[i].prec - 1);
            }

            for (int c = 0; c < _cp.tcps.Length; c++)
            {
                var tcp = new TileCodingParams();

                //C# impl. note.
                //The SOT marker will overwrite these, so one might
                //get away with a "new TileCompParams[_image.numcomps]"
                tcp.tccps = TileCompParams.Create(_private_image.numcomps);

                _cp.tcps[c] = tcp;
            }

            //Allocate and initialize some elements of codestrem index
            AllocateTileElementCstrIndex();

            _specific_param.decoder.state = J2K_STATUS.MH;
            _private_image.CompHeaderUpdate(_cp);

            return true;
        }

        /// <summary>
        /// Reads a COD marker (Coding style defaults)
        /// </summary>
        /// <param name="header_size">The size of the data contained in the COD marker</param>
        /// <returns>True on success</returns>
        /// <remarks>
        /// 2.5 - opj_j2k_read_cod
        /// </remarks>
        internal bool ReadCOD(uint header_size)
        {
            var tcp = _specific_param.decoder.state == J2K_STATUS.TPH ? 
                _cp.tcps[_current_tile_number] : _specific_param.decoder.default_tcp;
            tcp.cod = true;

            if (header_size < 5)
            {
                _cinfo.Error("Error reading COD marker");
                return false;
            }

            tcp.csty = (CP_CSTY) _cio.ReadByte();
            tcp.prg = (PROG_ORDER)_cio.ReadByte();

            if (tcp.prg > PROG_ORDER.CPRL)
            {
                _cinfo.Error("Unknown progression order in COD marker");
                tcp.prg = PROG_ORDER.PROG_UNKNOWN;
            }

            tcp.numlayers = _cio.ReadUShort();
            if (tcp.numlayers == 0)
            {
                _cinfo.Error("Invalid number of layers in COD marker : {0} not in range [1-65535]",
                    tcp.numlayers);
                return false;
            }

            //If user didn't set a number layer to decode take the max specify in the codestream.
            if (_cp.specific_param.dec.layer != 0)
                tcp.num_layers_to_decode = _cp.specific_param.dec.layer;
            else
                tcp.num_layers_to_decode = tcp.numlayers;

            tcp.mct = _cio.ReadByte();
            if (tcp.mct > 1)
            {
                _cinfo.Error("Invalid multiple component transformation");
                return false;
            }

            header_size -= 5;
            for (int i = 0; i < _private_image.numcomps; i++)
                tcp.tccps[i].csty = tcp.csty & CP_CSTY.PRT;

            if (!ReadSPCod_SPCoc(0, ref header_size))
            {
                _cinfo.Error("Error reading COD marker");
                return false;
            }

            if (header_size != 0)
            {
                _cinfo.Error("Error reading COD marker");
                return false;
            }

            CopyTileComponentParameters();

            return true;
        }

        //2.5
        void CopyTileComponentParameters()
        {
            var tcp = _specific_param.decoder.state == J2K_STATUS.TPH ? _cp.tcps[_current_tile_number] : _specific_param.decoder.default_tcp;

            var tccps = tcp.tccps;
            var ref_tccp = tccps[0];
            var prc_size = ref_tccp.numresolutions * sizeof(int);

            for (int i = 1; i < _private_image.numcomps; i++)
            {
                var copied_tccp = tccps[i];
                copied_tccp.numresolutions = ref_tccp.numresolutions;
                copied_tccp.cblkw = ref_tccp.cblkw;
                copied_tccp.cblkh = ref_tccp.cblkh;
                copied_tccp.cblksty = ref_tccp.cblksty;
                copied_tccp.qmfbid = ref_tccp.qmfbid;
                Buffer.BlockCopy(ref_tccp.prcw, 0, copied_tccp.prcw, 0, (int)prc_size);
                Buffer.BlockCopy(ref_tccp.prch, 0, copied_tccp.prch, 0, (int)prc_size);
            }
        }

        //2.5 - opj_j2k_read_SPCod_SPCoc
        internal bool ReadSPCod_SPCoc(int compno, ref uint header_size)
        {
            var tcp = _specific_param.decoder.state == J2K_STATUS.TPH ? _cp.tcps[_current_tile_number] : _specific_param.decoder.default_tcp;
            var tccp = tcp.tccps[compno];

            if (header_size < 5)
            {
                _cinfo.Error("Error reading SPCod SPCoc element");
                return false;
            }

            tccp.numresolutions = _cio.ReadByte() + 1u;
            if (tccp.numresolutions > Constants.J2K_MAXRLVLS)
            {
                _cinfo.Error("Invalid value for numresolutions : {0}, max value is set in Constants.cs at {1}",
                    tccp.numresolutions, Constants.J2K_MAXRLVLS);
                return false;
            }

            if (_cp.specific_param.dec.reduce >= tccp.numresolutions)
            {
                _cinfo.Error("Error decoding component {0}.\n"+
                    "The number of resolutions to remove is higher than the number "+
                    "of resolutions of this component\nModify the cp_reduce parameter.\n", compno);
                _specific_param.decoder.state |= J2K_STATUS.ERR;
                return false;
            }

            tccp.cblkw = _cio.ReadByte() + 2u;
            tccp.cblkh = _cio.ReadByte() + 2u;

            if (tccp.cblkw > 10 || tccp.cblkh > 10 || tccp.cblkw + tccp.cblkh > 12)
            {
                _cinfo.Error("Error reading SPCod SPCoc element, Invalid cblkw/cblkh combination");
                return false;
            }

            tccp.cblksty = (CCP_CBLKSTY) _cio.ReadByte();

            if ((tccp.cblksty & CCP_CBLKSTY.HTMIXED) != 0)
            {
                _cinfo.Error("Error reading SPCod SPCoc element. Unsupported Mixed HT code-block style found");
                return false;
            }

            tccp.qmfbid = _cio.ReadByte();

            if (tccp.qmfbid > 1)
            {
                _cinfo.Error("Error reading SPCod SPCoc element, Invalid transformation found");
                return false;
            }

            header_size -= 5;

            if ((tccp.csty & CP_CSTY.PRT) == CP_CSTY.PRT)
            {
                if (header_size < tccp.numresolutions)
                {
                    _cinfo.Error("Error reading SPCod SPCoc element");
                    return false;
                }

                for (int i = 0; i < tccp.numresolutions; i++)
                {
                    uint tmp = _cio.ReadByte();
                    //Precinct exponent 0 is only allowed for lowest resolution level (Table A.21)
                    if (i != 0 && ((tmp & 0xf) == 0 || (tmp >> 4) == 0))
                    {
                        _cinfo.Error("Invalid precinct size");
                        return false;
                    }
                    tccp.prcw[i] = tmp & 0xf;
                    tccp.prch[i] = tmp >> 4;
                }

                header_size -= tccp.numresolutions;
            }
            else
            {
                //Set default size for the precinct width and height
                for (int i = 0; i < tccp.numresolutions; i++)
                {
                    tccp.prcw[i] = 15;
                    tccp.prch[i] = 15;
                }
            }

            return true;
        }

        //2.5 - opj_j2k_read_SQcd_SQcc
        internal bool ReadSQcd_SQcc(int compno, ref uint header_size)
        {
            var tcp = _specific_param.decoder.state == J2K_STATUS.TPH ? _cp.tcps[_current_tile_number] : _specific_param.decoder.default_tcp;
            var tccp = tcp.tccps[compno];

            if (header_size < 1)
            {
                _cinfo.Error("Error reading SQcd or SQcc element");
                return false;
            }
            header_size -= 1;

            uint tmp = _cio.ReadByte();
            tccp.qntsty = (CCP_QNTSTY) (tmp & 0x1f);
            tccp.numgbits = tmp >> 5;
            uint num_bands;
            if (tccp.qntsty == CCP_QNTSTY.SIQNT)
                num_bands = 1;
            else
            {
                num_bands = tccp.qntsty == CCP_QNTSTY.NOQNT ? header_size : header_size / 2;

                if (num_bands > Constants.J2K_MAXBANDS)
                {
                    _cinfo.Warn("While reading CCP_QNTSTY element inside QCD or QCC marker segment, " +
                        "number of subbands ({0}) is greater to OPJ_J2K_MAXBANDS ({1}). So we limit the number of elements stored to " +
                         "OPJ_J2K_MAXBANDS ({1}) and skip the rest. ", num_bands, Constants.J2K_MAXBANDS);
                }
            }

            if (tccp.qntsty == CCP_QNTSTY.NOQNT)
            {
                for (int bandno = 0; bandno < num_bands; bandno++)
                {
                    tmp = _cio.ReadByte();
                    if (bandno < Constants.J2K_MAXBANDS)
                        tccp.stepsizes[bandno] = new StepSize((int)(tmp >> 3), 0);
                }

                if (header_size < num_bands)
                    return false;
                header_size -= num_bands;
            }
            else
            {
                for (int bandno = 0; bandno < num_bands; bandno++)
                {
                    tmp = _cio.ReadUShort();
                    if (bandno < Constants.J2K_MAXBANDS)
                        tccp.stepsizes[bandno] = new StepSize((int)(tmp >> 11), (int)(tmp & 0x7ff));
                }

                if (header_size < 2 * num_bands)
                    return false;
                header_size -= 2 * num_bands;
            }

            // if scalar_derived -> compute other stepsizes
            if (tccp.qntsty == CCP_QNTSTY.SIQNT)
            {
                for (int bandno = 1; bandno < Constants.J2K_MAXBANDS; bandno++)
                {
                    int tmp2 = (bandno - 1) / 3;
                    tccp.stepsizes[bandno] = new StepSize(
                        tccp.stepsizes[0].expn - tmp2 > 0 ?
                            tccp.stepsizes[0].expn - tmp2 : 0, tccp.stepsizes[0].mant);
                }
            }

            return true;
        }

        /// <summary>
        /// Reads a COC marker (Coding Style Component)
        /// </summary>
        /// <param name="header_size">The size of the data contained in the COC marker</param>
        /// <remarks>2.5 - opj_j2k_read_coc</remarks>
        internal bool ReadCOC(uint header_size)
        {
            var tcp = _specific_param.decoder.state == J2K_STATUS.TPH ? _cp.tcps[_current_tile_number] : 
                _specific_param.decoder.default_tcp;

            uint comp_room = _private_image.numcomps <= 256 ? 1u : 2;
            if (header_size < comp_room + 1)
            {
                _cinfo.Error("Error reading COC marker");
                return false;
            }
            header_size -= comp_room + 1;

            int compno = (comp_room == 1) ? _cio.ReadByte() : _cio.ReadUShort();
            if (compno >= _private_image.numcomps)
            {
                _cinfo.Error("Error reading COC marker (bad number of components)");
                return false;
            }
            tcp.tccps[compno].csty = (CP_CSTY)_cio.ReadByte();

            if (!ReadSPCod_SPCoc(compno, ref header_size) || header_size != 0)
            {
                _cinfo.Error("Error reading COC marker");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads a CRG marker (Component registration)
        /// </summary>
        internal bool ReadCRG(uint header_size)
        {
            uint numcomps = _private_image.numcomps;
            if (header_size != numcomps * 4)
            {
                _cinfo.Error("Error reading CRG marker");
                return false;
            }

            _cio.Skip(header_size);
            return true;
        }

        /// <summary>
        /// Reads a COM marker (comments)
        /// </summary>
        //2.5
        internal bool ReadCOM(uint header_size)
        {
            _cio.Skip(header_size);
            return true;
        }

        /// <summary>
        /// Reads a CBD marker (Component bit depth definition)
        /// </summary>
        //2.1
        internal bool ReadCBD(uint header_size)
        {
            OPJ_UINT32 n_comps, num_comps;

            num_comps = _private_image.numcomps;

            if (header_size != (_private_image.numcomps + 2))
            {
                _cinfo.Error("Error reading CBD marker");
                return false;
            }

            n_comps = _cio.ReadUShort();                             /* Ncbd */

            if (n_comps != num_comps)
            {
                _cinfo.Error("Error reading CBD marker");
                return false;
            }

            for (int i = 0; i < num_comps; i++)
            {
                ImageComp comp = _private_image.comps[i];
                OPJ_UINT32 comp_def = _cio.ReadByte();                    /* Component bit depth */
                comp.sgnd = ((comp_def >> 7) & 1) != 0;
                comp.prec = (comp_def & 0x7f) + 1;
            }

            return true;
        }

        /// <summary>
        /// Reads a CAP marker (extended capabilities definition). Empty implementation.
        /// Found in HTJ2K files.
        /// </summary>
        /// <remarks>
        /// 2.5
        /// </remarks>
        internal bool ReadCAP(uint header_size)
        {
            _cio.Skip(header_size);

            return true;
        }

        /// <summary>
        /// Reads a MCO marker (Multiple Component Transform Ordering)
        /// </summary>
        //2.1
        internal bool ReadMCO(uint p_header_size)
        {
            OPJ_UINT32 l_tmp, i;
            OPJ_UINT32 l_nb_stages;
            TileCodingParams l_tcp;

            l_tcp = _specific_param.decoder.state == J2K_STATUS.TPH ?
                            _cp.tcps[_current_tile_number] :
                            _specific_param.decoder.default_tcp;

            if (p_header_size < 1)
            {
                _cinfo.Error("Error reading MCO marker");
                return false;
            }

            l_nb_stages = _cio.ReadByte();                           /* Nmco : only one tranform stage*/

            if (l_nb_stages > 1)
            {
                _cinfo.Warn("Cannot take in charge multiple transformation stages.");
                _cio.Skip(p_header_size - 1);
                return true;
            }

            if (p_header_size != l_nb_stages + 1)
            {
                _cinfo.Error("Error reading MCO marker");
                return false;
            }

            for (i = 0; i < _private_image.numcomps; ++i)
            {
                l_tcp.tccps[i].dc_level_shift = 0;
            }

            if (l_tcp.mct_decoding_matrix != null)
            {
                l_tcp.mct_decoding_matrix = null;
            }

            for (i = 0; i < l_nb_stages; ++i)
            {
                l_tmp = _cio.ReadByte();

                if (!AddMCT(l_tcp, _private_image, l_tmp))
                {
                    return false;
                }
            }

            return true;
        }

        //2.5 - opj_j2k_add_mct
        bool AddMCT(TileCodingParams tcp, JPXImage image, OPJ_UINT32 index)
        {
            OPJ_UINT32 i;
            SimpleMccDecorrelationData mcc_record;
            ArPtr<MctData> deco_array, offset_array_ptr;
            OPJ_UINT32 data_size,mct_size, offset_size;
            OPJ_UINT32 nb_elem;
            OPJ_UINT32[] offset_data;
            TileCompParams tccp;
            mcc_record = tcp.mcc_records[0];

            for (i=0;i<tcp.n_mcc_records;i++) {
                mcc_record = tcp.mcc_records[i];
                if (mcc_record.index == index) {
                    break;
                }
            }

            if (i == tcp.n_mcc_records) {
                // element discarded
                return true;
            }

            if (mcc_record.n_comps != image.numcomps) {
                // do not support number of comps != image
                return true;
            }

            deco_array = mcc_record.decorrelation_array;

            if (deco_array != null)
            {
                data_size = MCT.ELEMENT_SIZE[(int) deco_array.Deref.element_type] * image.numcomps * image.numcomps;
                if (deco_array.Deref.data_size != data_size) {
                        return false;
                }

                nb_elem = image.numcomps * image.numcomps;
                mct_size = nb_elem;
                tcp.mct_decoding_matrix = new float[mct_size];

                deco_array.Deref.data.CopyToFloat(tcp.mct_decoding_matrix, (int) mct_size);
            }

            offset_array_ptr = mcc_record.offset_array;

            if (offset_array_ptr != null) 
            {
                data_size = MCT.ELEMENT_SIZE[(int) offset_array_ptr.Deref.element_type] * image.numcomps;
                if (offset_array_ptr.Deref.data_size != data_size) {
                    return false;
                }

                nb_elem = image.numcomps;
                offset_size = nb_elem;
                offset_data = new uint[offset_size];

                deco_array.Deref.data.CopyToInt(offset_data, (int)offset_size);

                int current_offset_data = 0;

                for (i=0;i<image.numcomps;++i) 
                {
                    tccp = tcp.tccps[i];
                    tccp.dc_level_shift = (int) offset_data[current_offset_data++];
                }
            }

            return true;
        }

        /// <summary>
        /// Reads a MCC marker (Multiple Component Collection)
        /// </summary>
        /// <remarks>2.5 - opj_j2k_read_mcc</remarks>
        internal bool ReadMCC(uint header_size)
        {
            OPJ_UINT32 i, j;
            OPJ_UINT32 tmp;
            OPJ_UINT32 indix;
            TileCodingParams tcp;
            SimpleMccDecorrelationData[] mcc_records;
            SimpleMccDecorrelationData mcc_record;
            OPJ_UINT32 n_collections;
            OPJ_UINT32 n_comps;
            OPJ_UINT32 n_bytes_by_comp;
            bool new_mcc = false;

            tcp = _specific_param.decoder.state == J2K_STATUS.TPH ?
                            _cp.tcps[_current_tile_number] :
                            _specific_param.decoder.default_tcp;

            if (header_size < 2)
            {
                _cinfo.Error("Error reading MCC marker");
                return false;
            }

            // first marker
            tmp = _cio.ReadUShort();                         // Zmcc
            if (tmp != 0)
            {
                _cinfo.Error("Cannot take in charge multiple data spanning");
                _cio.Skip(header_size - 2);
                return true;
            }

            if (header_size < 7)
            {
                _cinfo.Error("Error reading MCC marker");
                return false;
            }

            indix = _cio.ReadByte(); // Imcc . no need for other values, take the first

            mcc_records = tcp.mcc_records;

            for (i = 0; i < tcp.n_mcc_records; ++i)
            {
                mcc_record = mcc_records[i];
                if (mcc_record.index == indix)
                {
                    break;
                }
            }

            // NOT FOUND
            if (i == tcp.n_mcc_records)
            {
                if (tcp.n_mcc_records == tcp.n_max_mcc_records)
                {
                    tcp.n_max_mcc_records += Constants.MCC_DEFAULT_NB_RECORDS;
                    Array.Resize(ref tcp.mcc_records, (int)tcp.n_max_mcc_records);
                    mcc_records = tcp.mcc_records;
                    for (uint c = i; i < mcc_records.Length; c++)
                        mcc_records[c] = new SimpleMccDecorrelationData();
                }
                i = tcp.n_mcc_records;
                new_mcc = true;
            }
            mcc_record = mcc_records[i];
            mcc_record.index = indix;

            // only one marker atm
            tmp = _cio.ReadUShort();                         // Ymcc
            if (tmp != 0)
            {
                _cinfo.Warn("Cannot take in charge multiple data spanning");
                _cio.Skip(header_size - 5);
                return true;
            }

            n_collections = _cio.ReadUShort();                              // Qmcc -> number of collections -> 1

            if (n_collections > 1)
            {
                _cinfo.Warn("Cannot take in charge multiple data spanning");
                _cio.Skip(header_size - 7);
                return true;
            }

            header_size -= 7;

            for (i = 0; i < n_collections; ++i)
            {
                if (header_size < 3)
                {
                    _cinfo.Error("Error reading MCC marker");
                    return false;
                }

                tmp = _cio.ReadByte(); // Xmcci type of component transformation -> array based decorrelation

                if (tmp != 1)
                {
                    _cinfo.Warn("Cannot take in charge collections other than array decorrelation");
                    _cio.Skip(header_size - 1);
                    return true;
                }

                n_comps = _cio.ReadUShort();;

                header_size -= 3;

                n_bytes_by_comp = 1 + (n_comps >> 15);
                mcc_record.n_comps = n_comps & 0x7fff;

                if (header_size < (n_bytes_by_comp * mcc_record.n_comps + 2))
                {
                    _cinfo.Error("Error reading MCC marker");
                    return false;
                }

                header_size -= n_bytes_by_comp * mcc_record.n_comps + 2;

                for (j = 0; j < mcc_record.n_comps; ++j)
                {
                    tmp = _cio.ReadByte();        // Cmccij Component offset

                    if (tmp != j)
                    {
                        _cinfo.Warn("Cannot take in charge collections with indix shuffle");
                        _cio.Skip(header_size + mcc_record.n_comps - j);
                        return true;
                    }
                }

                n_comps = _cio.ReadUShort();

                n_bytes_by_comp = 1 + (n_comps >> 15);
                n_comps &= 0x7fff;

                if (n_comps != mcc_record.n_comps)
                {
                    _cinfo.Warn("Cannot take in charge collections without same number of indixes");
                    _cio.Skip(header_size - 2);
                    return true;
                }

                if (header_size < (n_bytes_by_comp * mcc_record.n_comps + 3))
                {
                    _cinfo.Error("Error reading MCC marker");
                    return false;
                }

                header_size -= n_bytes_by_comp * mcc_record.n_comps + 3;

                for (j = 0; j < mcc_record.n_comps; ++j)
                {
                    tmp = _cio.Read(n_bytes_by_comp);        // Wmccij Component offset

                    if (tmp != j)
                    {
                        _cinfo.Warn("Cannot take in charge collections with indix shuffle");
                        _cio.Skip(header_size + (mcc_record.n_comps - j) * n_bytes_by_comp);
                        return true;
                    }
                }

                tmp = _cio.Read(3u); /* Wmccij Component offset*/

                mcc_record.is_irreversible = ((tmp >> 16) & 1) != 0;
                mcc_record.decorrelation_array = null;
                mcc_record.offset_array = null;

                indix = tmp & 0xff;
                if (indix != 0)
                {
                    for (j = 0; j < tcp.n_mct_records; j++)
                    {
                        var mct_data = tcp.mct_records[j];
                        if (mct_data.index == indix)
                        {
                            mcc_record.decorrelation_array = new ArPtr<MctData>(tcp.mct_records, (int)j);
                            break;
                        }
                    }

                    if (mcc_record.decorrelation_array == null)
                    {
                        _cinfo.Error("Error reading MCC marker");
                        return false;
                    }
                }

                indix = (tmp >> 8) & 0xff;
                if (indix != 0)
                {
                    for (j = 0; j < tcp.n_mct_records; ++j)
                    {
                        var mct_data = tcp.mct_records[j];
                        if (mct_data.index == indix)
                        {
                            mcc_record.offset_array = new ArPtr<MctData>(tcp.mct_records, (int)j);
                            break;
                        }
                    }

                    if (mcc_record.offset_array == null)
                    {
                        _cinfo.Error("Error reading MCC marker");
                        return false;
                    }
                }
            }

            if (header_size != 0)
            {
                _cinfo.Error("Error reading MCC marker");
                return false;
            }

            if (new_mcc)
                tcp.n_mcc_records++;

            return true;
        }

        /// <summary>
        /// Reads a MCT marker (Multiple Component Transform)
        /// </summary>
        /// <remarks>2.5 - opj_j2k_read_mct</remarks>
        internal bool ReadMCT(uint header_size)
        {
            OPJ_UINT32 i;
            TileCodingParams tcp;
            OPJ_UINT32 tmp;
            OPJ_UINT32 indix;
            MctData mct_data;

            tcp = _specific_param.decoder.state == J2K_STATUS.TPH ?
                            _cp.tcps[_current_tile_number] :
                            _specific_param.decoder.default_tcp;

            if (header_size < 2)
            {
                _cinfo.Error("Error reading MCT marker");
                return false;
            }

            // first marker
            tmp = _cio.ReadUShort(); // Zmct
            if (tmp != 0)
            {
                _cinfo.Error("Cannot take in charge mct data within multiple MCT records");
                return true;
            }

            if (header_size <= 6)
            {
                _cinfo.Error("Error reading MCT marker");
                return false;
            }

            // Imct -> no need for other values, take the first, type is double with decorrelation x0000 1101 0000 0000
            tmp = _cio.ReadUShort();                         // Imct

            indix = tmp & 0xff;
            var l_mct_data_ar = tcp.mct_records;
            mct_data = l_mct_data_ar[0];

            for (i = 0; i < tcp.n_mct_records; ++i)
            {
                Debug.Assert(l_mct_data_ar[i] != null, "Whops, forgot to initialize");
                mct_data = l_mct_data_ar[i];
                if (mct_data.index == indix)
                {
                    break;
                }
            }

            /* NOT FOUND */
            if (i == tcp.n_mct_records)
            {
                if (tcp.n_mct_records == tcp.n_max_mct_records)
                {
                    tcp.n_max_mct_records += Constants.MCT_DEFAULT_NB_RECORDS;

                    Array.Resize<MctData>(ref tcp.mct_records, (int) tcp.n_max_mct_records);
                    mct_data = tcp.mct_records[tcp.n_mct_records];
                    for (uint c = tcp.n_mct_records; c < tcp.n_max_mct_records; c++)
                        tcp.mct_records[c] = new MctData();
                }

                mct_data = tcp.mct_records[tcp.n_mct_records];
                ++tcp.n_mct_records;
            }

            if (!mct_data.data.IsNull)
            {
                mct_data.data.Null();
            }

            mct_data.index = (int)indix;
            mct_data.array_type = (MCT_ARRAY_TYPE)((tmp >> 8) & 3);
            mct_data.element_type = (MCT_ELEMENT_TYPE)((tmp >> 10) & 3);

            tmp = _cio.ReadUShort();                         // Ymct
            if (tmp != 0)
            {
                _cinfo.Warn("Cannot take in charge multiple MCT markers");
                return true;
            }

            header_size -= 6;

            mct_data.data = new ShortOrIntOrFloatOrDoubleAr(mct_data.element_type,
                ShortOrIntOrFloatOrDoubleAr.SizeDiv(mct_data.element_type, (int)header_size));
            mct_data.data_size = (int)header_size;
            byte[] tmp_buffer = new byte[header_size];
            _cio.Read(tmp_buffer, 0, (int)header_size);
            mct_data.data.SetBytes(tmp_buffer);

            return true;
        }

        //2.5 - opj_j2k_read_unk
        internal bool ReadUNK(out J2K_Marker next_marker)
        {
            _cinfo.Warn("Unknown marker");
            uint l_size_unk = 2;

            while (true)
            {
                if (_cio.BytesLeft < 2)
                {
                    _cinfo.Error("Stream too short");
                    next_marker = J2K_Marker.NONE;
                    return false;
                }
                next_marker = (J2K_Marker)_cio.ReadUShort();

                if (!((ushort)next_marker < 0xFF00))
                {
                    var handler = _dec_tab[next_marker];
                    if ((_specific_param.decoder.state & handler.States) == J2K_STATUS.NONE)
                    {
                        _cinfo.Error("Marker is not compliant with its position");
                        return false;
                    }
                    else
                    {
                        if (handler.Mark != J2K_Marker.NONE)
                        {
                            // next marker is known and well located
                            break;
                        }
                        else
                        {
                            l_size_unk += 2;
                        }
                    }
                }      
            }

            return true;
        }

        /// <summary>
        ///  Reads a RGN marker (Region Of Interest)
        /// </summary>
        /// <remarks>2.5 - opj_j2k_read_rgn</remarks>
        internal bool ReadRGN(uint header_size)
        {
            int comp_room = (_private_image.numcomps <= 256) ? 1 : 2;
            if (header_size != 2 + comp_room)
            {
                _cinfo.Error("Error reading RGN marker");
                return false;
            }
            var tcp = _specific_param.decoder.state == J2K_STATUS.TPH ? 
                _cp.tcps[_current_tile_number] : 
                _specific_param.decoder.default_tcp;
            int compno = (comp_room == 1) ? _cio.ReadByte() : _cio.ReadUShort();
            int roisty = _cio.ReadByte();
            if (compno >= _private_image.numcomps)
            {
                _cinfo.Error("bad component number in RGN ({0} when there are only {1})",
                    compno, _private_image.numcomps);
                return false;
            }
            tcp.tccps[compno].roishift = _cio.ReadByte();
            return true;
        }

        /// <summary>
        /// Reads a QCD marker (Quantization defaults)
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_j2k_read_qcd
        /// </remarks>
        internal bool ReadQCD(uint header_size)
        {
            if (!ReadSQcd_SQcc(0, ref header_size))
            {
                _cinfo.Error("Error reading QCD marker");
                return false;
            }

            if (header_size != 0)
            {
                _cinfo.Error("Error reading QCD marker");
                return false;
            }

            // Apply the quantization parameters to other components of the current tile
            // or the m_default_tcp
            CopyTileQuantizationParameters();

            return true;
        }

        //2.5
        void CopyTileQuantizationParameters()
        {
            var tcp = _specific_param.decoder.state == J2K_STATUS.TPH ? 
                _cp.tcps[_current_tile_number] : _specific_param.decoder.default_tcp;

            var tccps = tcp.tccps;
            var ref_tccp = tccps[0];
            const int size = Constants.J2K_MAXBANDS;

            for (int i = 1; i < _private_image.numcomps; i++)
            {
                var copied_tccp = tccps[i];
                copied_tccp.qntsty = ref_tccp.qntsty;
                copied_tccp.numgbits = ref_tccp.numgbits;
                Array.Copy(ref_tccp.stepsizes, 0, copied_tccp.stepsizes, 0, size);
            }
        }

        /// <summary>
        /// Reads a QCC marker (Quantization component)
        /// </summary>
        /// <param name="header_size">The size of the data contained in the QCC marker.</param>
        /// <returns>False on failure</returns>
        /// <remarks>2.5 - opj_j2k_read_qcc</remarks>
        internal bool ReadQCC(uint header_size)
        {
            uint num_comp = _private_image.numcomps;
            int compno;

            if (num_comp <= 256)
            {
                if (header_size < 1)
                {
                    _cinfo.Error("Error reading QCC marker");
                    return false;
                }
                compno = _cio.ReadByte();
                header_size -= 1;
            }
            else
            {
                if (header_size < 2)
                {
                    _cinfo.Error("Error reading QCC marker");
                    return false;
                }
                compno = _cio.ReadUShort();
                header_size -= 2;
            }

            if (compno >= _private_image.numcomps)
            {
                _cinfo.Error("Invalid component number: {0}, regarding the number of components {1}",
                    compno, _private_image.numcomps);
                return false;
            }

            if (!ReadSQcd_SQcc(compno, ref header_size) || header_size != 0)
            {
                _cinfo.Error("Error reading QCC marker");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads a POC marker (Progression Order Change)
        /// </summary>
        /// <remarks>2.5 - opj_j2k_read_poc</remarks>
        internal bool ReadPOC(uint header_size)
        {
            uint numcomps = _private_image.numcomps;

            uint comp_room = numcomps <= 256 ? 1u : 2u;
            uint chunk_size = 5u + 2u * comp_room;
            uint current_poc_nb = (uint)header_size / chunk_size;

            if (current_poc_nb <= 0 || header_size % chunk_size != 0)
            {
                _cinfo.Error("Error reading POC marker");
                return false;
            }

            TileCodingParams tcp = _specific_param.decoder.state == J2K_STATUS.TPH ? _cp.tcps[_current_tile_number] : _specific_param.decoder.default_tcp;
            uint old_poc = tcp.POC ? tcp.numpocs + 1 : 0u;
            current_poc_nb += old_poc;

            if (current_poc_nb >= 32)
            {
                _cinfo.Error("Too many POCs {0}", current_poc_nb);
                return false;
            }

            // Now poc is in use
            tcp.POC = true;

            for (uint i = old_poc; i < current_poc_nb; i++)
            {
                ProgOrdChang poc = tcp.pocs[i];
                poc.resno0 = _cio.ReadByte();
                poc.compno0 = _cio.Read(comp_room);
                poc.layno1 = _cio.ReadUShort();
                poc.resno1 = _cio.ReadByte();
                poc.compno1 = Math.Min(
                    _cio.Read(comp_room), numcomps);
                poc.prg = (PROG_ORDER)_cio.ReadByte();
            }

            tcp.numpocs = current_poc_nb - 1;
            return true;
        }

        #endregion

        #region Helper functions

        //2.5.1 - opj_j2k_update_image_dimensions
        private bool UpdateImageDimensions(JPXImage image)
        {
            uint it_comp;
            int comp_x1, comp_y1;
            ImageComp[] img_comp_ar = null;

            img_comp_ar = image.comps;
            for (it_comp = 0; it_comp < image.numcomps; ++it_comp)
            {
                int h, w;
                if (image.x0 > (OPJ_UINT32)int.MaxValue ||
                    image.y0 > (OPJ_UINT32)int.MaxValue ||
                    image.x1 > (OPJ_UINT32)int.MaxValue ||
                    image.y1 > (OPJ_UINT32)int.MaxValue)
                {
                    _cinfo.Error("Image coordinates above INT_MAX are not supported");
                    return false;
                }
                var img_comp = img_comp_ar[it_comp];

                img_comp.x0 = MyMath.uint_ceildiv(image.x0, img_comp.dx);
                img_comp.y0 = MyMath.uint_ceildiv(image.y0, img_comp.dy);
                comp_x1 = MyMath.int_ceildiv((int)image.x1, (int)img_comp.dx);
                comp_y1 = MyMath.int_ceildiv((int)image.y1, (int)img_comp.dy);

                w = MyMath.int_ceildivpow2(comp_x1, (int)img_comp.factor)
                      - MyMath.int_ceildivpow2((int)img_comp.x0, (int)img_comp.factor);
                if (w < 0)
                {
                    _cinfo.Error("Size x of the decoded component image is incorrect (comp[{0}].w={0}).",
                                  it_comp, w);
                    return false;
                }
                img_comp.w = (OPJ_UINT32)w;

                h = MyMath.int_ceildivpow2(comp_y1, (int)img_comp.factor)
                      - MyMath.int_ceildivpow2((int)img_comp.y0, (int)img_comp.factor);
                if (h < 0)
                {
                    _cinfo.Error("Size y of the decoded component image is incorrect (comp[{0}].h={1}).\n",
                                  it_comp, h);
                    return false;
                }
                img_comp.h = (OPJ_UINT32)h;
            }

            return true;
        }

        //2.5
        internal bool SetDecodeArea(JPXImage image, int start_x, int start_y, int end_x, int end_y)
        {
            if (_cp.tw == 1 && _cp.th == 1 &&
                _cp.tcps[0].data != null)
            {
                // In the case of a single-tiled image whose codestream we have already
                // ingested, go on
            }
            else if (_specific_param.decoder.state != J2K_STATUS.TPHSOT)
            {
                _cinfo.Error("Need to decode the main header before begin to decode the remaining codestream");
                return false;
            }

            // Update the comps[].factor member of the output image with the one
            // of m_reduce
            for (var it_comp = 0; it_comp < _private_image.numcomps; ++it_comp)
            {
                image.comps[it_comp].factor = _cp.specific_param.dec.reduce;
            }

            if (start_x == 0 && start_y == 0 && end_x == 0 && end_y == 0)
            {
                _cinfo.Info("No decoded area parameters, set the decoded area to the whole image");

                _specific_param.decoder.start_tile_x = 0;
                _specific_param.decoder.start_tile_y = 0;
                _specific_param.decoder.end_tile_x = _cp.tw;
                _specific_param.decoder.end_tile_y = _cp.th;

                image.x0 = _private_image.x0;
                image.y0 = _private_image.y0;
                image.x1 = _private_image.x1;
                image.y1 = _private_image.y1;

                return UpdateImageDimensions(image);
            }

            //Left
            if (start_x < 0)
            {
                _cinfo.Error("Left position of the decoded area (region_x0={0}) should be >= 0.",
                              start_x);
                return false;
            }
            else if (start_x > _private_image.x1)
            {
                _cinfo.Error("Left position of the decoded area (region_x0={0}) is outside the image area (Xsiz={1}).",
                    start_x, _private_image.x1);

                return false;
            }
            if (start_x < _private_image.x0)
            {
                _cinfo.Warn("Left position of the decoded area (region_x0={0}) is outside the image area (XOsiz={1}).",
                    start_x, _private_image.x0);

                _specific_param.decoder.start_tile_x = 0;
                image.x0 = _private_image.x0;
            }
            else
            {
                _specific_param.decoder.start_tile_x = (OPJ_UINT32) ((start_x - _cp.tx0) / _cp.tdx);
                image.x0 = (uint)start_x;
            }

            //Up
            if (start_y < 0)
            {
                _cinfo.Error("Up position of the decoded area (region_y0={0}) should be >= 0.",
                              start_y);
                return false;
            }
            else if (start_y > _private_image.y1)
            {
                _cinfo.Error("Up position of the decoded area (region_y0={0}) is outside the image area (Ysiz={1}).",
                    start_y, _private_image.y1);

                return false;
            }
            if (start_y < _private_image.y0)
            {
                _cinfo.Warn("Up position of the decoded area (region_y0={0}) is outside the image area (YOsiz={1}).",
                    start_y, _private_image.y0);

                _specific_param.decoder.start_tile_y = 0;
                image.y0 = _private_image.x0;
            }
            else
            {
                _specific_param.decoder.start_tile_y = (OPJ_UINT32)((start_y - _cp.ty0) / _cp.tdy);
                image.y0 = (uint)start_y;
            }

            //Right
            if (end_x < 0)
            {
                _cinfo.Error("Right position of the decoded area (region_x1={0}) should be >= 0.",
                              end_x);
                return false;
            }
            else if(end_x < _private_image.x0)
            {
                _cinfo.Error("Right position of the decoded area (region_x1={0}) is outside the image area (XOsiz={1}).",
                    end_x, _private_image.x0);

                return false;
            }
            if (end_x > _private_image.x1)
            {
                _cinfo.Warn("Right position of the decoded area (region_x1={0}) is outside the image area (Xsiz={1}).",
                    end_x, _private_image.x1);

                _specific_param.decoder.end_tile_x = _cp.tw;
                image.x1 = _private_image.x1;
            }
            else
            {
                _specific_param.decoder.end_tile_x = MyMath.uint_ceildiv(((uint)end_x - _cp.tx0) , _cp.tdx);
                image.x1 = (uint)end_x;
            }

            //Bottom
            if (end_y < 0)
            {
                _cinfo.Error("Bottom position of the decoded area (region_y1={0}) should be >= 0.",
                              end_y);
                return false;
            }
            else if (end_y < _private_image.y0)
            {
                _cinfo.Error("Bottom position of the decoded area (region_y1={0}) is outside the image area (YOsiz={1}).",
                    end_y, _private_image.y0);

                return false;
            }
            if (end_y > _private_image.y1)
            {
                _cinfo.Warn("Bottom position of the decoded area (region_y1={0}) is outside the image area (Ysiz={1}).",
                    end_y, _private_image.y1);

                _specific_param.decoder.end_tile_y = _cp.th;
                image.y1 = _private_image.y1;
            }
            else
            {
                _specific_param.decoder.end_tile_y = MyMath.uint_ceildiv((uint)end_y - _cp.ty0, _cp.tdy);
                image.y1 = (uint)end_y;
            }

            _specific_param.decoder.discard_tiles = true;

            var ret = UpdateImageDimensions(image);

            if (ret)
                _cinfo.Info("Setting decoding area to {0},{1},{2},{3}", image.x0, image.y0, image.x1, image.y1);

            return ret;
        }

#endregion

#region Spesific params

        //[System.Runtime.InteropServices.StructLayoutAttribute(System.Runtime.InteropServices.LayoutKind.Explicit)]
        struct J2KEncOrDec
        {
            //[System.Runtime.InteropServices.FieldOffsetAttribute(0)]
            public DecParams decoder;
        }

        /// <summary>
        /// Entry of a TLM marker segment
        /// </summary> 
        /// <remarks>
        /// 2.5.3 - opj_j2k_tlm_tile_part_info
        /// </remarks>
        struct TlmTilePartInfo
        {
            /// <summary>
            /// Tile index of the tile part. Ttlmi field
            /// </summary>
            public ushort tile_index;

            /// <summary>
            /// Length in bytes, from the beginning of the SOT marker to the end of
            /// the bit stream data for that tile-part. Ptlmi field
            /// </summary>
            public uint length;
        }

        /// <summary>
        /// Information got from the concatenation of TLM marker semgnets.
        /// </summary>
        /// <remarks>
        /// 2.5.3 - opj_j2k_tlm_info 
        /// </remarks>
        class TlmInfo
        {
            /// <summary>
            /// Number of entries in m_tile_part_infos.
            /// </summary>
            public uint entries_count;

            /// <summary>
            /// Array of m_entries_count values.
            /// </summary>
            public TlmTilePartInfo[] tile_part_infos;

            public bool is_invalid;
        }

        //2.5.3 - opj_j2k_dec 
        struct DecParams
        {
            /// <summary>
            /// Locate in which part of the codestream the decoder is (main header, tile header, end)
            /// </summary>
            public J2K_STATUS state;

            /**
             * store decoding parameters common to all tiles (information like COD, COC in main header)
             */
            public TileCodingParams default_tcp;

            internal byte[] header_data;
            internal int header_data_size;

            /// <summary>
            /// To tell the tile part length
            /// </summary>
            public OPJ_UINT32 sot_length;

            /// <summary>
            /// Only tiles index in the correct range will be decoded
            /// </summary>
            public OPJ_UINT32 start_tile_x, start_tile_y, end_tile_x, end_tile_y;

            /// <summary>
            /// Decoded area set by the user
            /// </summary>
            /// <summary>
            /// Index of the tile to decode (used in get_tile)
            /// </summary>
            public int tile_ind_to_dec;

            /// <summary>
            /// Position of the last SOT marker read
            /// </summary>
            public long last_sot_read_pos;

            /// <summary>
            /// Indicate that the current tile-part is assume as the last tile part of the codestream.
	        /// It is useful in the case of PSot is equal to zero. The sot length will be compute in the
	        /// SOD reader function. FIXME NOT USED for the moment
            /// </summary>
            public bool last_tile_part;
            internal TlmInfo tlm;

            // Below if used when there's TLM information available and we use
            // opj_set_decoded_area() to a subset of all tiles.

            /// <summary>
            /// Current index in m_intersecting_tile_parts_offset[] to seek to
            /// </summary>
            internal OPJ_UINT32 idx_intersecting_tile_parts;
            /// <summary>
            /// Number of elements of m_intersecting_tile_parts_offset[]
            /// </summary>
            internal OPJ_UINT32 num_intersecting_tile_parts;
            /// <summary>
            /// Start offset of contributing tile parts
            /// </summary>
            internal long[] intersecting_tile_parts_offset;

            uint bitvector1;

            /// <summary>
            /// To tell that a tile can be decoded.
            /// </summary>
            public bool can_decode
            {
                get
                {
                    return (this.bitvector1 & 1u) != 0;
                }
                set
                {
                    this.bitvector1 = value ? 1u | bitvector1 : ~1u & bitvector1;
                }
            }
            public bool discard_tiles
            {
                get
                {
                    return (this.bitvector1 & 2u) != 0;
                }
                set
                {
                    this.bitvector1 = value ? 2u | bitvector1 : ~2u & bitvector1;
                }
            }

            public bool skip_data
            {
                get
                {
                    return (bitvector1 & 4u) != 0;
                }
                set
                {
                    bitvector1 = value ? 4u | bitvector1 : ~4u & bitvector1;
                }
            }

            public bool n_tile_parts_correction_checked
            {
                get
                {
                    return (bitvector1 & 8u) != 0;
                }
                set
                {
                    bitvector1 = value ? 8u | bitvector1 : ~8u & bitvector1;
                }
            }

            public bool n_tile_parts_correction
            {
                get
                {
                    return (bitvector1 & 16u) != 0;
                }
                set
                {
                    bitvector1 = value ? 16u | bitvector1 : ~16u & bitvector1;
                }
            }
        }

#endregion
    }
}
