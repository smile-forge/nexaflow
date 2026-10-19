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
using OPJ_UINT32 = System.UInt32;
#endregion

namespace Nexaflow.Media.Jpeg2000.Internal
{
    /// <summary>
    /// A packet iterator that permits to get the next
    /// packet following the progression order and change of it.
    /// </summary>
    internal sealed class PacketIterator
    {
        #region Variables and properties

        /// <summary>
        /// The parent compression info obj.
        /// </summary>
        /// <remarks>Not needed for encode</remarks>
        readonly CompressionInfo _cinfo;

        /// <summary>
        /// Precise if the packet has been already used 
        /// (usefull for progression order change)
        /// </summary>
        bool[] include;

        /// <summary>
        /// Layer step used to localize the packet in the include vector
        /// </summary>
        uint step_l;

        /// <summary>
        /// Resolution step used to localize the packet in the include vector
        /// </summary>
        uint step_r;

        /// <summary>
        /// Component step used to localize the packet in the include vector
        /// </summary>
        uint step_c;

        /// <summary>
        /// Precinct step used to localize the packet in the include vector
        /// </summary>
        uint step_p;

        /// <summary>
        /// component that identify the packet
        /// </summary>
        internal uint compno;

        /// <summary>
        /// Resolution that identify the packet
        /// </summary>
        internal uint resno;

        /// <summary>
        /// Precinct that identify the packet
        /// </summary>
        internal uint precno;

        /// <summary>
        /// Quality layer that identify the packet
        /// </summary>
        internal uint layno;

        /// <summary>
        /// Progression order change information
        /// </summary>
        internal ProgOrdChang poc = new ProgOrdChang();

        /// <summary>
        /// Number of components in the image
        /// </summary>
        /// <remarks>This variable can probably be dropped for comps.Length</remarks>
        uint numcomps;

        /// <summary>
        /// Components
        /// </summary>
        PIComp[] comps;

        uint tx0, ty0, tx1, ty1;
        uint x, y, dx, dy;

        #endregion

        #region Init

        private PacketIterator(CompressionInfo cinfo)
        {
            _cinfo = cinfo;
        }

        #endregion

        /// <remarks>
        /// 2.5
        ///C# About resolution_ptrs and resolutions.
        ///In the org impl, resolution_ptrs is pointers into the resolutions
        ///array. Since we do not have pointers we instead update the data directly.
        ///
        /// An alternative solution would be to use uint?, as that could achive the
        /// same result as the org impl.
        /// </remarks>
        static void GetAllEncodingParameters(JPXImage image,
                                             CodingParameters cp,
                                             OPJ_UINT32 tileno,
                                             out uint tx0,
                                             out uint tx1,
                                             out uint ty0,
                                             out uint ty1,
                                             out OPJ_UINT32 dx_min,
                                             out OPJ_UINT32 dy_min,
                                             out OPJ_UINT32 max_prec,
                                             out OPJ_UINT32 max_res,
                                             OPJ_UINT32[] resolution_ptrs,
                                             ref OPJ_UINT32[] resolutions)
        {
            //C# I don't think there's a need for this feature, and it simplifies the impl.
            if (resolution_ptrs == null)
                throw new NotImplementedException("Null argument for resolution_ptrs");
            
            // loop
	        OPJ_UINT32 compno, resno;

	        // to store l_dx, l_dy, w and h for each resolution and component.
	        OPJ_UINT32 resolution_ptr;

	        // preconditions in debug
	        Debug.Assert(cp != null);
            Debug.Assert(image != null);
            Debug.Assert(tileno < cp.tw * cp.th);

	        // initializations
	        TileCodingParams tcp = cp.tcps[tileno];
	        TileCompParams[] tccps = tcp.tccps;
	        ImageComp[] img_comps = image.comps;

	        // position in x and y of tile
            OPJ_UINT32 p = tileno % (OPJ_UINT32) cp.tw;
            OPJ_UINT32 q = tileno / (OPJ_UINT32) cp.tw;

            // here calculation of tx0, tx1, ty0, ty1 
            {
                uint l_tx0 = cp.tx0 + p * cp.tdx;
                tx0 = Math.Max(l_tx0, image.x0);
                tx1 = Math.Min(MyMath.uint_adds(l_tx0, cp.tdx), image.x1);
                uint l_ty0 = cp.ty0 + q * cp.tdy;
                ty0 = Math.Max(l_ty0, image.y0);
                ty1 = Math.Min(MyMath.uint_adds(l_ty0, cp.tdy), image.y1);
            }

	        // max precision and resolution is 0 (can only grow)
	        max_prec = 0;
	        max_res = 0;

	        // take the largest value for dx_min and dy_min
	        dx_min = 0x7fffffff;
	        dy_min = 0x7fffffff;

	        for (compno = 0; compno < image.numcomps; ++compno) 
            {
		        // aritmetic variables to calculate
		        OPJ_UINT32 level_no;
                OPJ_UINT32 rx0, ry0, rx1, ry1;
                OPJ_UINT32 px0, py0, px1, py1;
		        OPJ_UINT32 product;
                OPJ_UINT32 tcx0, tcy0, tcx1, tcy1;
		        OPJ_UINT32 pdx, pdy, pw, ph;

                resolution_ptr = resolution_ptrs[compno];

                //C# Sets the "pointers"
                var img_comp = img_comps[compno];
                var tccp = tccps[compno];

		        tcx0 = MyMath.uint_ceildiv(tx0, img_comp.dx);
                tcy0 = MyMath.uint_ceildiv(ty0, img_comp.dy);
                tcx1 = MyMath.uint_ceildiv(tx1, img_comp.dx);
                tcy1 = MyMath.uint_ceildiv(ty1, img_comp.dy);

		        if (tccp.numresolutions > max_res)
			        max_res = tccp.numresolutions;

		        // use custom size for precincts
		        level_no = tccp.numresolutions;
		        for (resno = 0; resno < tccp.numresolutions; ++resno) 
                {
			        OPJ_UINT32 dx, dy;

                    --level_no;

                    // precinct width and height
                    pdx = tccp.prcw[resno];
			        pdy = tccp.prch[resno];
                    resolutions[resolution_ptr++] = pdx;
                    resolutions[resolution_ptr++] = pdy;
                    if (pdx + level_no < 32 &&
                        img_comp.dx <= uint.MaxValue / (1u << (int)(pdx + level_no)))
                    {
                        dx = img_comp.dx * (1u << (int)(pdx + level_no));
                        // take the minimum size for l_dx for each comp and resolution
                        dx_min = Math.Min(dx_min, dx);
                    }
                    if (pdy + level_no < 32 &&
                        img_comp.dy <= uint.MaxValue / (1u << (int)(pdy + level_no)))
                    {
                        dy = img_comp.dy * (1u << (int)(pdy + level_no));
                        dy_min = Math.Min(dy_min, dy);
                    }

			        /* various calculations of extents*/
                    rx0 = MyMath.uint_ceildivpow2(tcx0, (int)level_no);
                    ry0 = MyMath.uint_ceildivpow2(tcy0, (int)level_no);
                    rx1 = MyMath.uint_ceildivpow2(tcx1, (int)level_no);
                    ry1 = MyMath.uint_ceildivpow2(tcy1, (int)level_no);
                    px0 = MyMath.uint_floordivpow2(rx0, (int)pdx) << (int)pdx;
                    py0 = MyMath.uint_floordivpow2(ry0, (int)pdy) << (int)pdy;
                    px1 = MyMath.uint_ceildivpow2(rx1, (int)pdx) << (int)pdx;
                    py1 = MyMath.uint_ceildivpow2(ry1, (int)pdy) << (int)pdy;
			        pw = (rx0==rx1)?0:(OPJ_UINT32)((px1 - px0) >> (int)pdx);
			        ph = (ry0==ry1)?0:(OPJ_UINT32)((py1 - py0) >> (int)pdy);
                    resolutions[resolution_ptr++] = pw;
                    resolutions[resolution_ptr++] = ph;
			        product = pw * ph;
			
                    // update precision
			        if (product > max_prec) {
				        max_prec = product;
			        }
		        }
                //C# Snip pointer increments
	        }
        }

        /// <remarks>
        /// 2.5
        /// Org method: pi.c opj_pi_iterator_t *opj_pi_create(opj_image_t *image, opj_cp_t *cp, int tileno)
        /// </remarks>
        internal static PacketIterator[] Create(JPXImage image, CodingParameters cp, uint tileno, CompressionInfo cinfo)
        {
            TileCodingParams tcp = cp.tcps[tileno];
            //uint poc_bound = tcp.numpocs + 1; C# uses pi_ar.Length instead

            var pi_ar = new PacketIterator[tcp.numpocs + 1];

            //C# current_pi is set inside the loop
            for (int pino = 0; pino < pi_ar.Length; pino++)
            {
                TileCompParams tccp;
                var current_pi = pi_ar[pino] = new PacketIterator(cinfo);

                current_pi.numcomps = image.numcomps;
                current_pi.comps = new PIComp[image.numcomps];

                for (int compno = 0; compno < current_pi.comps.Length; compno++)
                {
                    var comp = current_pi.comps[compno] = new PIComp();

                    tccp = tcp.tccps[compno];

                    comp.resolutions = new PIResolution[tccp.numresolutions];
                    comp.numresolutions = tccp.numresolutions;

                    for (int resno = 0; resno < comp.resolutions.Length; resno++)
                        comp.resolutions[resno] = new PIResolution();
                }     
            }

            return pi_ar;
        }

        /// <remarks>
        /// 2.5
        /// Org method: pi.c opj_pi_iterator_t *pi_create_decode(opj_image_t *image, opj_cp_t *cp, int tileno)
        /// </remarks>
        internal static PacketIterator[] CreateDecode(JPXImage image, CodingParameters cp, uint tileno, CompressionInfo cinfo)
        {
            TileCodingParams tcp = cp.tcps[tileno];
            //uint bound = tcp.numpocs + 1; C# uses pi_ar.Length instead

            //to store w, h, dx and dy fro all components and resolutions
            const int data_stride = 4 * Constants.J2K_MAXRLVLS;
            OPJ_UINT32[] tmp_data = new OPJ_UINT32[image.numcomps * data_stride];
            OPJ_UINT32[] tmp_ptr = new OPJ_UINT32[image.numcomps];

            /* encoding prameters to set*/
            OPJ_UINT32 max_res;
            OPJ_UINT32 max_prec;
            OPJ_UINT32 tx0, tx1, ty0, ty1;
            OPJ_UINT32 dx_min, dy_min;
            OPJ_UINT32 step_p, step_c, step_r, step_l;

            var pi_ar = Create(image, cp, (uint) tileno, cinfo);

            // C# l_encoding_value_ptr is a pointer, so we don't use it. The workaround is
            //    to add tmp_data as a parameter to GetAllEncodingParameters

            //Updates pointer array. Starting from 1, as first position is always 0
            for (uint compno = 1; compno < tmp_ptr.Length; compno++)
                tmp_ptr[compno] = compno * data_stride;

            GetAllEncodingParameters(image, cp, (OPJ_UINT32)tileno, out tx0, out tx1, out ty0, out ty1, out dx_min, out dy_min, out max_prec, out max_res, tmp_ptr, ref tmp_data);

            // step calculations
            step_p = 1;
            step_c = max_prec * step_p;
            step_r = image.numcomps * step_c;
            step_l = max_res * step_r;

            // set values on the first packet iterator
            var current_pi = pi_ar[0];
            current_pi.include = null;
            if (step_l <= int.MaxValue / (tcp.numlayers + 1))
            {
                //C# impl. Instead of shorts we use bool, which takes 2 x the amount of
                //         memory, but looks cleaner.
                current_pi.include = new bool[(tcp.numlayers + 1) * step_l];
            }

            if (current_pi.include == null)
            {
                return null;
            }

            // Special treatment for the first packet iterator
            PIComp[] current_comps = current_pi.comps;
            var img_comps = image.comps;
            //var tccps = tcp.tccps;

            current_pi.tx0 = tx0;
            current_pi.ty0 = ty0;
            current_pi.tx1 = tx1;
            current_pi.ty1 = ty1;

            current_pi.step_p = step_p;
            current_pi.step_c = step_c;
            current_pi.step_r = step_r;
            current_pi.step_l = step_l;

            // allocation for components and number of components has already been calculated by opj_pi_create
            for (int compno = 0; compno < current_pi.numcomps; ++compno)
            {
                //C# Updating pointers
                PIComp current_comp = current_comps[compno];
                ImageComp img_comp = img_comps[compno];

                PIResolution[] res_ar = current_comp.resolutions;

                //C# impl. This is a pointer into tmp_data, which is why we
                //         have the tmp_data as an extra parameter to this func
                uint encoding_value_ptr = tmp_ptr[compno];

                current_comp.dx = img_comp.dx;
                current_comp.dy = img_comp.dy;

                /* resolutions have already been initialized */
                for (int resno = 0; resno < current_comp.numresolutions; resno++)
                {
                    //C# Updates pointers
                    PIResolution res = res_ar[resno];

                    res.pdx = tmp_data[encoding_value_ptr++];
                    res.pdy = tmp_data[encoding_value_ptr++];
                    res.pw = tmp_data[encoding_value_ptr++];
                    res.ph = tmp_data[encoding_value_ptr++];
                }
            }

            for (int pino = 1; pino < pi_ar.Length; ++pino)
            {
                //C# Updates the ponters
                current_pi = pi_ar[pino];

                current_comps = current_pi.comps;
                img_comps = image.comps;
                //C# tccp is not used in this loop

                current_pi.tx0 = tx0;
                current_pi.ty0 = ty0;
                current_pi.tx1 = tx1;
                current_pi.ty1 = ty1;
                //current_pi.dx = dx_min;
                //current_pi.dy = dy_min;
                current_pi.step_p = step_p;
                current_pi.step_c = step_c;
                current_pi.step_r = step_r;
                current_pi.step_l = step_l;

                // allocation for components and number of components has already been calculated by opj_pi_create
                for (int compno = 0; compno < current_pi.numcomps; ++compno)
                {
                    //C# Updates the pointers
                    PIComp current_comp = current_comps[compno];
                    ImageComp img_comp = img_comps[compno];

                    PIResolution[] res_ar = current_comp.resolutions;
                    uint encoding_value_ptr = tmp_ptr[compno];

                    current_comp.dx = img_comp.dx;
                    current_comp.dy = img_comp.dy;
                    /* resolutions have already been initialized */
                    for (int resno = 0; resno < current_comp.numresolutions; resno++)
                    {
                        //C# updates the pointers
                        PIResolution l_res = res_ar[resno];

                        l_res.pdx = tmp_data[encoding_value_ptr++];
                        l_res.pdy = tmp_data[encoding_value_ptr++];
                        l_res.pw = tmp_data[encoding_value_ptr++];
                        l_res.ph = tmp_data[encoding_value_ptr++];
                    }
                }

                // special treatment
                current_pi.include = pi_ar[pino - 1].include;
            }

            if (tcp.POC)
                UpdateDecodePoc(pi_ar, tcp, max_prec, max_res);
            else
                UpdateDecodeNotPoc(pi_ar, tcp, max_prec, max_res);

            return pi_ar;
        }

        //2.5
        static void UpdateDecodeNotPoc(PacketIterator[] pi_ar, TileCodingParams tcp, OPJ_UINT32 max_precision, OPJ_UINT32 max_res)
        {
            //C# instead of l_bound we use pi_ar.Length
            Debug.Assert(pi_ar.Length == tcp.numpocs + 1);

            for (int pino = 0; pino < pi_ar.Length; pino++)
            {
                //C# updates the pointers
                var current_pi = pi_ar[pino];

                current_pi.poc.prg = tcp.prg;
                //current_pi.first = true;
                current_pi.poc.resno0 = 0;
                current_pi.poc.compno0 = 0;
                current_pi.poc.layno0 = 0;
                current_pi.poc.precno0 = 0;
                current_pi.poc.resno1 = max_res;
                current_pi.poc.compno1 = current_pi.numcomps;
                current_pi.poc.layno1 = tcp.numlayers;
                current_pi.poc.precno1 = max_precision;
            }
        }

        //2.5 - opj_pi_update_decode_poc
        static void UpdateDecodePoc(PacketIterator[] pi_ar, TileCodingParams tcp, OPJ_UINT32 max_precision, OPJ_UINT32 p_max_res)
        {
            //C# instead of l_bound we use pi_ar.Length
            Debug.Assert(pi_ar.Length == tcp.numpocs + 1);

            for (int pino = 0; pino < pi_ar.Length; pino++)
            {
                //C# updates the pointers
                var current_poc = tcp.pocs[pino];
                var current_pi = pi_ar[pino];

                current_pi.poc.prg = current_poc.prg; /* Progression Order #0 */
                //current_pi.first = true;

                current_pi.poc.resno0 = current_poc.resno0; // Resolution Level Index #0 (Start)
                current_pi.poc.compno0 = current_poc.compno0; // Component Index #0 (Start)
                current_pi.poc.layno0 = 0;
                current_pi.poc.precno0 = 0;
                current_pi.poc.resno1 = current_poc.resno1; // Resolution Level Index #0 (End)
                current_pi.poc.compno1 = current_poc.compno1; // Component Index #0 (End)
                current_pi.poc.layno1 = Math.Min(current_poc.layno1, tcp.numlayers); // Layer Index #0 (End)
                current_pi.poc.precno1 = max_precision;
            }
        }

        //2.5 - opj_pi_next
        internal IEnumerator<bool> Next()
        {
            switch (poc.prg)
            {
                case PROG_ORDER.LRCP:
                    return Next_lrcp().GetEnumerator();
                case PROG_ORDER.RLCP:
                    return Next_rlcp().GetEnumerator();
                case PROG_ORDER.RPCL:
                    return Next_rpcl().GetEnumerator();
                case PROG_ORDER.PCRL:
                    return Next_pcrl().GetEnumerator();
                case PROG_ORDER.CPRL:
                    return Next_cprl().GetEnumerator();
                //case PROG_ORDER.PROG_UNKNOWN:
                default:
                    return null;
            }
        }

        /// <summary>
        /// Next Layer, Resolution, Component, ProgOrder
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_pi_next_lrcp
        /// 
        /// Org impl uses goto to jump into a codeblock, that is
        /// not allowed in C#.
        /// 
        /// Workaround is to rewrite this method as a generator.
        /// </remarks>
        IEnumerable<bool> Next_lrcp()
        {
            //bool skip;
            PIComp comp;
            PIResolution res;

            if (poc.compno0 >= numcomps ||
                poc.compno1 >= numcomps + 1)
            {
                _cinfo.Error("opj_pi_next_rlcp(): invalid compno0/compno1");
                yield break;
            }

            for (layno = poc.layno0; layno < poc.layno1; layno++)
            {
                for (resno = poc.resno0; resno < poc.resno1; resno++)
                {
                    for (compno = poc.compno0; compno < poc.compno1; compno++)
                    {
                        comp = comps[compno];
                        if (resno >= comp.numresolutions)
                            continue;
                        res = comp.resolutions[resno];
                        poc.precno1 = res.pw * res.ph;
                        
                        for (precno = poc.precno0; precno < poc.precno1; precno++)
                        {
                            //I belive c++ long == c# int (index was long)
                            uint index = layno * step_l + resno * step_r + compno * step_c + precno * step_p;
                            if (index >= include.Length)
                            {
                                //C# impl. note:
                                //This happens with "p0_07.j2k". The org. impl (1.4) will happily
                                //read into unallocated memory. But due to coincidence, it
                                //will always return false.
                                //...but since then they've added this check too :/
                                _cinfo.Error("Invalid access to pi->include");
                                yield break;
                            }
                            if (!include[index])
                            {
                                include[index] = true;
                                yield return true;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Next Resolution, Layer, Component, ProgOrder
        /// </summary>
        /// <remarks>
        /// 2.5
        /// </remarks>
        IEnumerable<bool> Next_rlcp()
        {
            PIComp comp;
            PIResolution res;

            if (poc.compno0 >= numcomps ||
                poc.compno1 >= numcomps + 1)
            {
                _cinfo.Error("opj_pi_next_rlcp(): invalid compno0/compno1");
                yield break;
            }

            for (resno = poc.resno0; resno < poc.resno1; resno++)
            {
                for (layno = poc.layno0; layno < poc.layno1; layno++)
                {
                    for (compno = poc.compno0; compno < poc.compno1; compno++)
                    {
                        comp = comps[compno];
                        if (resno >= comp.numresolutions)
                            continue;
                        res = comp.resolutions[resno];
                        poc.precno1 = res.pw * res.ph;

                        for (precno = poc.precno0; precno < poc.precno1; precno++)
                        {
                            //I belive c++ long == c# int (index was long)
                            uint index = layno * step_l + resno * step_r + compno * step_c + precno * step_p;
                            if (index >= include.Length)
                            {
                                _cinfo.Error("Invalid access to pi->include");
                                yield break; //<-- See as lrcp as to why
                            }
                            if (!include[index])
                            {
                                include[index] = true;
                                yield return true;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Next Resolution, ProgOrder(y then x), Component, Layer
        /// </summary>
        /// <remarks>
        /// 2.5.1
        /// </remarks>
        IEnumerable<bool> Next_rpcl()
        {
            PIComp comp;
            PIResolution res;

            if (poc.compno0 >= numcomps ||
                poc.compno1 >= numcomps + 1)
            {
                _cinfo.Error("opj_pi_next_rlcp(): invalid compno0/compno1");
                yield break;
            }

            { 
                OPJ_UINT32 compno, resno;
                //first = false;
                dx = 0;
                dy = 0;
                for (compno = 0; compno < numcomps; compno++)
                {
                    comp = comps[compno];
                    for (resno = 0; resno < comp.numresolutions; resno++)
                    {
                        OPJ_UINT32 dx, dy;
                        res = comp.resolutions[resno];
                        var tmp = res.pdx + comp.numresolutions - 1 - resno;
                        if (tmp < 32 &&
                            comp.dx <= int.MaxValue / (1 << (int)tmp))
                        {
                            dx = comp.dx * (1u << (int)tmp);
                            this.dx = this.dx == 0 ? dx : Math.Min(this.dx, dx);
                        }
                        tmp = res.pdy + comp.numresolutions - 1 - resno;
                        if (tmp < 32 &&
                            comp.dy <= int.MaxValue / (1 << (int)tmp))
                        {
                            dy = comp.dy * (1u << (int)tmp);
                            this.dy = this.dy == 0 ? dy : Math.Min(this.dy, dy);
                        }
                    }
                }

                if (dx == 0 || dy == 0)
                {
                    yield break;
                }
            }

            poc.ty0 = ty0;
            poc.tx0 = tx0;
            poc.ty1 = ty1;
            poc.tx1 = tx1;

            for (resno = poc.resno0; resno < poc.resno1; resno++)
            {
                for (y = poc.ty0; y < poc.ty1; y += dy - (y % dy))
                {
                    for (x = poc.tx0; x < poc.tx1; x += dx - (x % dx))
                    {
                        for (compno = poc.compno0; compno < poc.compno1; compno++)
                        {
                            uint levelno, trx0, try0, trx1;
                            uint try1, rpx, rpy, prci, prcj;

                            comp = comps[compno];
                            if (resno >= comp.numresolutions)
                                continue;
                            res = comp.resolutions[resno];
                            levelno = comp.numresolutions - 1u - resno;

                            if ((uint)(((ulong)comp.dx << (int) levelno) >> (int) levelno) != comp.dx ||
                            (uint)(((ulong)comp.dy << (int) levelno) >> (int) levelno) != comp.dy)
                            {
                                continue;
                            }

                            trx0 = MyMath.uint64_ceildiv_res_uint32(tx0, (ulong)comp.dx << (int)levelno);
                            try0 = MyMath.uint64_ceildiv_res_uint32(ty0, (ulong)comp.dy << (int)levelno);
                            trx1 = MyMath.uint64_ceildiv_res_uint32(tx1, (ulong)comp.dx << (int)levelno);
                            try1 = MyMath.uint64_ceildiv_res_uint32(ty1, (ulong)comp.dy << (int)levelno);
                            rpx = res.pdx + levelno;
                            rpy = res.pdy + levelno;

                            if ((uint)(((ulong)comp.dx << (int) rpx) >> (int) rpx) != comp.dx ||
                            (uint)(((ulong)comp.dy << (int) rpy) >> (int) rpy) != comp.dy)
                            {
                                continue;
                            }

                            /* See ISO-15441. B.12.1.3 Resolution level-position-component-layer progression */
                            if (!(((ulong)y % ((ulong)comp.dy << (int) rpy) == 0) || ((y == ty0) && (((ulong)try0 << (int) levelno) % ((ulong)1U << (int) rpy)) != 0)))
                                continue;
                            if (!(((ulong)x % ((ulong)comp.dx << (int) rpx) == 0) || ((x == tx0) && (((ulong)trx0 << (int)levelno) % ((ulong)1U << (int)rpx)) != 0)))
                                continue;
                            if ((res.pw == 0) || (res.ph == 0)) continue;
                            if ((trx0 == trx1) || (try0 == try1)) continue;

                            prci = MyMath.uint_floordivpow2(MyMath.uint64_ceildiv_res_uint32(x, (ulong)comp.dx << (int)levelno), (int)res.pdx)
                                    - MyMath.uint_floordivpow2(trx0, (int)res.pdx);
                            prcj = MyMath.uint_floordivpow2(MyMath.uint64_ceildiv_res_uint32(y, (ulong)comp.dy << (int)levelno), (int)res.pdy)
                                    - MyMath.uint_floordivpow2(try0, (int)res.pdy);
                            precno = prci + prcj * res.pw;
                            
                            for (layno = poc.layno0; layno < poc.layno1; layno++)
                            {
                                uint index = layno * step_l + resno * step_r + compno * step_c + precno * step_p;
                                if (index >= include.Length)
                                {
                                    _cinfo.Error("Invalid access to pi->include");
                                    yield break;
                                }
                                if (!include[index])
                                {
                                    include[index] = true;
                                    yield return true;
                                }
                                //C# Skip enters here
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Next ProgOrder(y then x), Component, Resolution, Layer
        /// </summary>
        /// <remarks>2.5.1 - opj_pi_next_pcrl</remarks>
        IEnumerable<bool> Next_pcrl()
        {
            PIComp comp;
            PIResolution res;

            if (poc.compno0 >= numcomps ||
                poc.compno1 >= numcomps + 1)
            {
                _cinfo.Error("opj_pi_next_rlcp(): invalid compno0/compno1");
                yield break;
            }

            {
                OPJ_UINT32 compno, resno;
                //first = false;
                dx = 0;
                dy = 0;
                for (compno = 0; compno < numcomps; compno++)
                {
                    comp = comps[compno];
                    for (resno = 0; resno < comp.numresolutions; resno++)
                    {
                        OPJ_UINT32 dx, dy;
                        res = comp.resolutions[resno];
                        var tmp = res.pdx + comp.numresolutions - 1 - resno;
                        if (tmp < 32 &&
                            comp.dx <= int.MaxValue / (1 << (int)tmp))
                        {
                            dx = comp.dx * (1u << (int)tmp);
                            this.dx = this.dx == 0 ? dx : Math.Min(this.dx, dx);
                        }
                        tmp = res.pdy + comp.numresolutions - 1 - resno;
                        if (tmp < 32 &&
                            comp.dy <= int.MaxValue / (1 << (int)tmp))
                        {
                            dy = comp.dy * (1u << (int)tmp);
                            this.dy = this.dy == 0 ? dy : Math.Min(this.dy, dy);
                        }
                    }
                }

                if (dx == 0 || dy == 0)
                {
                    yield break;
                }
            }

            poc.ty0 = ty0;
            poc.tx0 = tx0;
            poc.ty1 = ty1;
            poc.tx1 = tx1;

            for (y = poc.ty0; y < poc.ty1; y += dy - (y % dy))
            {
                for (x = poc.tx0; x < poc.tx1; x += dx - (x % dx))
                {
                    for (compno = poc.compno0; compno < poc.compno1; compno++)
                    {
                        comp = comps[compno];
                        for (resno = poc.resno0; resno < MyMath.uint_min(poc.resno1, comp.numresolutions); resno++)
                        {
                            uint levelno, trx0, try0, trx1;
                            uint try1, rpx, rpy, prci, prcj;

                            res = comp.resolutions[resno];
                            levelno = comp.numresolutions - 1 - resno;

                            if ((uint)(((ulong)comp.dx << (int)levelno) >> (int)levelno) != comp.dx ||
                            (uint)(((ulong)comp.dy << (int)levelno) >> (int)levelno) != comp.dy)
                            {
                                continue;
                            }

                            trx0 = MyMath.uint64_ceildiv_res_uint32(tx0, (ulong)comp.dx << (int)levelno);
                            try0 = MyMath.uint64_ceildiv_res_uint32(ty0, (ulong)comp.dy << (int)levelno);
                            trx1 = MyMath.uint64_ceildiv_res_uint32(tx1, (ulong)comp.dx << (int)levelno);
                            try1 = MyMath.uint64_ceildiv_res_uint32(ty1, (ulong)comp.dy << (int)levelno);
                            rpx = res.pdx + levelno;
                            rpy = res.pdy + levelno;

                            if ((uint)(((ulong)comp.dx << (int)rpx) >> (int)rpx) != comp.dx ||
                            (uint)(((ulong)comp.dy << (int)rpy) >> (int)rpy) != comp.dy)
                            {
                                continue;
                            }

                            /* See ISO-15441. B.12.1.3 Resolution level-position-component-layer progression */
                            if (!(((ulong)y % ((ulong)comp.dy << (int)rpy) == 0) || ((y == ty0) && (((ulong)try0 << (int)levelno) % ((ulong)1U << (int)rpy)) != 0)))
                                continue;
                            if (!(((ulong)x % ((ulong)comp.dx << (int)rpx) == 0) || ((x == tx0) && (((ulong)trx0 << (int)levelno) % ((ulong)1U << (int)rpx)) != 0)))
                                continue;
                            if ((res.pw == 0) || (res.ph == 0)) continue;
                            if ((trx0 == trx1) || (try0 == try1)) continue;

                            prci = MyMath.uint_floordivpow2(MyMath.uint64_ceildiv_res_uint32(x, (ulong)comp.dx << (int)levelno), (int)res.pdx)
                                    - MyMath.uint_floordivpow2(trx0, (int)res.pdx);
                            prcj = MyMath.uint_floordivpow2(MyMath.uint64_ceildiv_res_uint32(y, (ulong)comp.dy << (int)levelno), (int)res.pdy)
                                    - MyMath.uint_floordivpow2(try0, (int)res.pdy);
                            precno = prci + prcj * res.pw;

                            //C# Part of skip:
                            layno = poc.layno0;
                            
                            for (layno = poc.layno0; layno < poc.layno1; layno++)
                            {
                                uint index = layno * step_l + resno * step_r + compno * step_c + precno * step_p;
                                if (index >= include.Length)
                                {
                                    _cinfo.Error("Invalid access to pi->include");
                                    yield break;
                                }
                                if (!include[index])
                                {
                                    include[index] = true;
                                    yield return true;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Next Component, ProgOrder(y then x), Resolution, Layer
        /// </summary>
        /// <remarks>
        /// 2.5.1 - opj_pi_next_cprl
        /// 
        /// Org impl uses goto to jump into a codeblock, that is
        /// not allowed in C#.
        /// 
        /// Workaround is to rewrite this method as a generator.
        /// </remarks>
        IEnumerable<bool> Next_cprl()
        {
            PIComp comp;
            PIResolution res;

            if (poc.compno0 >= numcomps ||
                poc.compno1 >= numcomps + 1)
            {
                _cinfo.Error("opj_pi_next_rlcp(): invalid compno0/compno1");
                yield break;
            }

            for (compno = poc.compno0; compno < poc.compno1; compno++)
            {
                {
                    OPJ_UINT32 resno;
                    dx = 0;
                    dy = 0;
                    comp = comps[compno];
                    for (resno = 0; resno < comp.numresolutions; resno++)
                    {
                        OPJ_UINT32 dx, dy;
                        res = comp.resolutions[resno];
                        var tmp = res.pdx + comp.numresolutions - 1 - resno;
                        if (tmp < 32 &&
                            comp.dx <= int.MaxValue / (1 << (int)tmp))
                        {
                            dx = comp.dx * (1u << (int)tmp);
                            this.dx = this.dx == 0 ? dx : Math.Min(this.dx, dx);
                        }
                        tmp = res.pdy + comp.numresolutions - 1 - resno;
                        if (tmp < 32 &&
                            comp.dy <= int.MaxValue / (1 << (int)tmp))
                        {
                            dy = comp.dy * (1u << (int)tmp);
                            this.dy = this.dy == 0 ? dy : Math.Min(this.dy, dy);
                        }
                    }

                    if (dx == 0 || dy == 0)
                    {
                        yield break;
                    }

                    poc.ty0 = ty0;
                    poc.tx0 = tx0;
                    poc.ty1 = ty1;
                    poc.tx1 = tx1;
                    y = poc.ty0;
                }

                for (; y < poc.ty1; y += dy - (y % dy))
                {
                    for (x = poc.tx0; x < poc.tx1; x += dx - (x % dx))
                    {
                        for (resno = poc.resno0; resno < MyMath.uint_min(poc.resno1, comp.numresolutions); resno++)
                        {
                            uint levelno, trx0, try0, trx1;
                            uint try1, rpx, rpy, prci, prcj;

                            res = comp.resolutions[resno];
                            levelno = comp.numresolutions - 1 - resno;

                            if ((uint)(((ulong)comp.dx << (int)levelno) >> (int)levelno) != comp.dx ||
                                (uint)(((ulong)comp.dy << (int)levelno) >> (int)levelno) != comp.dy)
                            {
                                continue;
                            }

                            trx0 = MyMath.uint64_ceildiv_res_uint32(tx0, (ulong)comp.dx << (int)levelno);
                            try0 = MyMath.uint64_ceildiv_res_uint32(ty0, (ulong)comp.dy << (int)levelno);
                            trx1 = MyMath.uint64_ceildiv_res_uint32(tx1, (ulong)comp.dx << (int)levelno);
                            try1 = MyMath.uint64_ceildiv_res_uint32(ty1, (ulong)comp.dy << (int)levelno);
                            rpx = res.pdx + levelno;
                            rpy = res.pdy + levelno;

                            if ((uint)(((ulong)comp.dx << (int)rpx) >> (int)rpx) != comp.dx ||
                                (uint)(((ulong)comp.dy << (int)rpy) >> (int)rpy) != comp.dy)
                            {
                                continue;
                            }

                            /* See ISO-15441. B.12.1.3 Resolution level-position-component-layer progression */
                            if (!(((ulong)y % ((ulong)comp.dy << (int)rpy) == 0) || ((y == ty0) && (((ulong)try0 << (int)levelno) % ((ulong)1U << (int)rpy)) != 0)))
                                continue;
                            if (!(((ulong)x % ((ulong)comp.dx << (int)rpx) == 0) || ((x == tx0) && (((ulong)trx0 << (int)levelno) % ((ulong)1U << (int)rpx)) != 0)))
                                continue;
                            if ((res.pw == 0) || (res.ph == 0)) continue;
                            if ((trx0 == trx1) || (try0 == try1)) continue;

                            prci = MyMath.uint_floordivpow2(MyMath.uint64_ceildiv_res_uint32(x, (ulong)comp.dx << (int)levelno), (int)res.pdx)
                                    - MyMath.uint_floordivpow2(trx0, (int)res.pdx);
                            prcj = MyMath.uint_floordivpow2(MyMath.uint64_ceildiv_res_uint32(y, (ulong)comp.dy << (int)levelno), (int)res.pdy)
                                    - MyMath.uint_floordivpow2(try0, (int)res.pdy);
                            precno = prci + prcj * res.pw;
                            
                            for (layno = poc.layno0; layno < poc.layno1; layno++)
                            {
                                //I belive c++ long == c# int (index was long)
                                uint index = layno * step_l + resno * step_r + compno * step_c + precno * step_p;
                                if (index >= include.Length)
                                {
                                    _cinfo.Error("Invalid access to pi->include");
                                    yield break;
                                }
                                if (!include[index])
                                {
                                    include[index] = true;
                                    yield return true;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
