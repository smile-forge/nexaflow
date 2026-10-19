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
using System;
using System.Diagnostics;
using System.Text;
using Nexaflow.Media.Jpeg2000.Internal;
using System.IO;

namespace Nexaflow.Media.Jpeg2000
{
    /// <summary>
    /// This is the type of image object that the J2K libary works with internaly.
    /// 
    /// Images are split into components, and each component can have it's own
    /// resolution and bits.
    /// 
    /// J2K supports monocrome, RGB and YUV colorspaces. Alpha is also supported,
    /// ie. RGBA, YUVA, GRAYA.
    /// </summary>
    internal class JPXImage
    {
        #region Variables and properties

        /// <summary>
        /// Dimensions
        /// </summary>
        internal uint x0, y0, x1, y1;

        /// <summary>
        /// Number of components in the image
        /// </summary>
        internal uint numcomps;

        /// <summary>
        /// Color space: sRGB, Greyscale or YUV
        /// </summary>
        /// <remarks>
        /// When decoding J2K files the resulting color space will 
        /// alwyas be RGB(A) or GRAY. Where Gray is 1 or 2 components, and
        /// RGB is 3 or 4 components.
        /// </remarks>
        internal COLOR_SPACE color_space;

        /// <summary>
        /// Image components
        /// </summary>
        internal ImageComp[] comps;

        /// <summary>
        /// Restricted' ICC profile
        /// </summary>
	    internal byte[] icc_profile_buf;

        /// <summary>
        /// C# impel note
        /// 
        /// The images color information
        /// </summary>
        internal JP2cdefInfo[] channel_definitions;

        /// <summary>
        /// Width of the image
        /// </summary>
        public int Width { get { return (int) (x1 - x0); } }

        /// <summary>
        /// Height of the image
        /// </summary>
        public int Height { get { return (int) (y1 - y0); } }

        /// <summary>
        /// The maximum bits per component for this image.
        /// </summary>
        public int MaxBPC
        {
            get
            {
                uint bpc = 0;
                for (int c = 0; c < comps.Length; c++)
                    bpc = Math.Max(bpc, comps[c].prec);
                return (int) bpc;
            }
        }

        /// <summary>
        /// If all channels have the same bit depth
        /// </summary>
        public bool UniformBPC
        {
            get
            {
                uint bpc = comps[0].prec;
                for (int c = 1; c < comps.Length; c++)
                    if (bpc != comps[c].prec)
                        return false;
                return true;
            }
        }

        /// <summary>
        /// If all channels have the same widht / height
        /// </summary>
        public bool UniformSize
        {
            get
            {
                var d = comps[0].Data;
                if (d == null) return false;
                int size = d.Length;
                for (int c = 1; c < comps.Length; c++)
                    if (size != comps[c].Data.Length)
                        return false;
                return true;
            }
        }

        /// <summary>
        /// True if the image have the same subsampling, bit depth and sign
        /// </summary>
        public bool Uniform
        {
            get
            {
                var numcomps = Math.Min(NumberOfComponents, 4);
                for (int compno = 1; compno < numcomps; ++compno)
                {
                    if (comps[0].dx != comps[compno].dx ||
                        comps[0].dy != comps[compno].dy ||
                        comps[0].prec != comps[compno].prec ||
                        comps[0].sgnd != comps[compno].sgnd)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Whenever multi component transform should be used or not
        /// </summary>
        /// <remarks>todo: CMYK testing, I suspect CMYK is a no here</remarks>
        public bool UseMCT 
        { 
            get 
            {
                if (comps.Length == 3)
                {
                    var c0 = comps[0];

                    //Should perhaps check the width and height parameters as well.
                    uint dx = c0.dx, dy = c0.dy;
                    for (int c = 1; c < comps.Length; c++)
                    {
                        var cn = comps[c];
                        if (cn.dx != dx || cn.dy != dy)
                            return false;
                    }
                    return true;
                }
                return false;
            } 
        }

        /// <summary>
        /// Whenever this image has an alpha channel or not
        /// </summary>
        public bool HasAlpha
        {
            get
            {
                if (channel_definitions == null) return false;
                for (int c = 0; c < channel_definitions.Length; c++)
                {
                    var typ = channel_definitions[c].typ;
                    if (typ == 1 || typ == 2)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Embeded ICC profile
        /// </summary>
        public byte[] ICCProfile 
        { get { return icc_profile_buf; } set { icc_profile_buf = value; } }

        /// <summary>
        /// The size of this image, in number of bits.
        /// </summary>
        public int ImageSize
        {
            get
            {
                uint img_size = 0;
                for (int i = 0; i < comps.Length; i++)
                    img_size += (comps[i].w * comps[i].h * comps[i].prec);
                return (int) img_size;
            }
        }

        /// <summary>
        /// The number of components in the image.
        /// </summary>
        public int NumberOfComponents { get { return (int)numcomps; } }

        /// <summary>
        /// The number of opague components in the image.
        /// </summary>
        public int NumberOfOpagueComponents 
        { 
            get 
            { 
                if (channel_definitions == null)
                    return (int)numcomps;
                int ncomps = 0;
                for (int c = 0; c < channel_definitions.Length; c++)
                    if (channel_definitions[c].typ == 0 && channel_definitions[c].asoc != 0)
                        ncomps++;
                return ncomps;
            } 
        }

        /// <summary>
        /// The components in this image
        /// </summary>
        public ImageComp[] Components { get { return comps; } }

        public ImageComp[] AlphaComponents
        {
            get
            {
                if (channel_definitions == null) return null;
                var nalpha = 0;
                for (int c = 0; c < channel_definitions.Length; c++)
                    if (channel_definitions[c].typ != 0)
                        nalpha++;
                if (nalpha == 0) return null;
                var ac = new ImageComp[nalpha];
                nalpha = 0;
                for (int c = 0; c < channel_definitions.Length; c++)
                    if (channel_definitions[c].typ != 0)
                    {
                        //Note that jp2.ApplyCDEF messes this up by moving
                        //the alpha channels. Todo: fix this
                        Debug.Assert(false, "If an alpha channel has been moved, the following code is faulty");
                        ac[nalpha++] = comps[channel_definitions[c].cn];
                    }
                return ac;
            }
        }

        /// <summary>
        /// Color space of this image
        /// </summary>
        public COLOR_SPACE ColorSpace 
        { 
            get 
            {
                if (color_space != COLOR_SPACE.sYCC && numcomps == 3 && comps[0].dx == comps[0].dy && comps[1].dx != 1)
                    return COLOR_SPACE.sYCC;
                else if (numcomps <= 2)
                    return COLOR_SPACE.GRAY;

                return color_space; 
            } 
        }

        /// <summary>
        /// Returns the "real" colorspace
        /// </summary>
        public COLOR_SPACE GetColorSpace() { return color_space; }

        #endregion

        #region Init

        //2.5
        internal JPXImage() { }

        #endregion

        /// <summary>
        /// Copies the header of an image and its component header (no data is copied)
        /// If this image has data, it will be freed
        /// </summary>
        /// <param name="src">Source image</param>
        /// <remarks>
        /// 2.5
        /// </remarks>
        internal void CopyImageHeader(JPXImage src)
        {
            x0 = src.x0;
            y0 = src.y0;
            x1 = src.x1;
            y1 = src.y1;

            //Snip freeing memory. We effectivly do this by overwriting
            //comps with a new array anyway.

            numcomps = src.numcomps;
            comps = new ImageComp[numcomps];

            for (int compno = 0; compno < comps.Length; compno++)
            {
                var comp = (ImageComp) src.comps[compno].Clone();
                comp.data = null;
                comps[compno] = comp;
            }

            color_space = src.color_space;
            icc_profile_buf = src.icc_profile_buf;
            // ^The org lib makes a copy of this buffer, but there's no need
            //  for that here as it's never modified and GC will handle
            //  everything else.
        }

        /// <summary>
        /// Updates the components characteristics of the image from the coding parameters.
        /// </summary>
        /// <param name="cp">The coding parameters from which to update the image</param>
        /// <remarks>
        /// 2.5
        /// </remarks>
        internal void CompHeaderUpdate(CodingParameters cp)
        {
            uint i, l_width, l_height;
            uint l_x0, l_y0, l_x1, l_y1;
            uint l_comp_x0, l_comp_y0, l_comp_x1, l_comp_y1;

            l_x0 = Math.Max(cp.tx0, x0);
            l_y0 = Math.Max(cp.ty0, y0);
            l_x1 = cp.tx0 + (cp.tw - 1U) *
                cp.tdx; /* validity of p_cp members used here checked in opj_j2k_read_siz. Can't overflow. */
            l_y1 = cp.ty0 + (cp.th - 1U) * cp.tdy; /* can't overflow */
            l_x1 = Math.Min(MyMath.uint_adds(l_x1, cp.tdx), x1);
            l_y1 = Math.Min(MyMath.uint_adds(l_y1, cp.tdy), y1);

            for (i = 0; i < comps.Length; i++)
            {
                var comp = comps[i];
                l_comp_x0 = MyMath.uint_ceildiv(l_x0, comp.dx);
                l_comp_y0 = MyMath.uint_ceildiv(l_y0, comp.dy);
                l_comp_x1 = MyMath.uint_ceildiv(l_x1, comp.dx);
                l_comp_y1 = MyMath.uint_ceildiv(l_y1, comp.dy);
                l_width = MyMath.uint_ceildivpow2(l_comp_x1 - l_comp_x0, (int)comp.factor);
                l_height = MyMath.uint_ceildivpow2(l_comp_y1 - l_comp_y0, (int)comp.factor);
                comp.w = l_width;
                comp.h = l_height;
                comp.x0 = l_comp_x0;
                comp.y0 = l_comp_y0;
            }
        }

        /// <summary>
        /// When the SMaskInData flag is set it's not guranteed that
        /// there's alpha channel data (though Adobe appears to require this,
        /// but perhaps not for J2K streams as I've only tested JP2)
        /// </summary>
        public void SetAlphaOnLastChannel(ushort type)
        {
            if (type == 0) return;
            int index = comps.Length - 1;
            if (channel_definitions == null)
            {
                SetAlpha(index);
                channel_definitions[index].typ = type;
            }
            else
            {
                for (int c = 0; c < channel_definitions.Length; c++)
                {
                    if (channel_definitions[c].asoc - 1 == index || channel_definitions[c].asoc == 0)
                    {
                        channel_definitions[c].typ = type;
                        channel_definitions[c].asoc = 0;
                        return;
                    }
                }
            }
        }

        public void SetAlpha(int index)
        {
            if (channel_definitions == null)
            {
                channel_definitions = new JP2cdefInfo[numcomps];
                for (int c = 0; c < channel_definitions.Length; c++)
                {
                    channel_definitions[c].cn = (ushort)c;
                    channel_definitions[c].asoc = (ushort)(c + 1);
                    channel_definitions[c].typ = (ushort)0;
                }
            }
            channel_definitions[index].typ = 1;
            channel_definitions[index].asoc = 0;
        }

        /// <summary>
        /// Fetches all the non-alpha channels
        /// </summary>
        public ImageComp[] GetOpagueComponents()
        {
            if (channel_definitions == null)
                return comps;

            int nopague = 0;
            for (int c = 0; c < channel_definitions.Length; c++)
                if (channel_definitions[c].typ == 0 && channel_definitions[c].asoc != 0)
                    nopague = Math.Max(channel_definitions[c].asoc, nopague);
            var oc = new ImageComp[nopague];
            nopague = 0;
            for (int c = 0; c < channel_definitions.Length; c++)
                if (channel_definitions[c].typ == 0 && channel_definitions[c].asoc != 0)
                    //Note that "comps[channel_definitions[c].assoc - 1]" is not the 
                    //correct value, that would be comps[channel_definitions[c].cn], but
                    //JP2.ApplyCDEF shuffles the data around.
                    oc[channel_definitions[c].asoc - 1] = comps[channel_definitions[c].asoc - 1];
            return oc;
        }

        /// <summary>
        /// Crude CMYK to RGB conversion
        /// </summary>
        /// <param name="cinfo">Optional cinfo</param>
        /// <returns>If it was able to do the conversion</returns>
        public bool CMYKtoRGB(CompressionInfo cinfo = null)
        {
            if (ColorSpace != COLOR_SPACE.CMYK)
            {
                string msg = "Colorspace must be CMYK";
                if (cinfo == null)
                    throw new NotSupportedException(msg);
                cinfo.Error(msg);
                return false;
            }

            uint w = comps[0].w;
            uint h = comps[0].h;

            if ((numcomps < 4) ||
                (comps[0].dx != comps[1].dx) ||
                (comps[0].dx != comps[2].dx) ||
                (comps[0].dx != comps[3].dx) ||
                (comps[0].dy != comps[1].dy) ||
                (comps[0].dy != comps[2].dy) ||
                (comps[0].dy != comps[3].dy)
    )
            {
                string msg = "color_cmyk_to_rgb - can't convert";
                if (cinfo == null)
                    throw new NotSupportedException(msg);
                cinfo.Error(msg);
                return false;
            }

            uint max = w * h;

            float sC = 1.0F / ((1 << (int)comps[0].prec) - 1);
            float sM = 1.0F / ((1 << (int)comps[1].prec) - 1);
            float sY = 1.0F / ((1 << (int)comps[2].prec) - 1);
            float sK = 1.0F / ((1 << (int)comps[3].prec) - 1);

            for (int i = 0; i < max; ++i)
            {
                // CMYK values from 0 to 1
                float C = (comps[0].data[i]) * sC;
                float M = (comps[1].data[i]) * sM;
                float Y = (comps[2].data[i]) * sY;
                float K = (comps[3].data[i]) * sK;

                // Invert all CMYK values
                C = 1.0F - C;
                M = 1.0F - M;
                Y = 1.0F - Y;
                K = 1.0F - K;

#if TEST_MATH_MODE
                comps[0].data[i] = (int)(float)((float)(255.0F * C) * K); // R
                comps[1].data[i] = (int)(float)((float)(255.0F * M) * K); // G
                comps[2].data[i] = (int)(float)((float)(255.0F * Y) * K); // B
#else
                // CMYK -> RGB : RGB results from 0 to 255
                comps[0].data[i] = (int)(255.0F * C * K); // R
                comps[1].data[i] = (int)(255.0F * M * K); // G
                comps[2].data[i] = (int)(255.0F * Y * K); // B
#endif
            }

            Array.Resize(ref comps, 3);
            numcomps = 3;
            comps[0].prec = 8;
            comps[1].prec = 8;
            comps[2].prec = 8;

            color_space = COLOR_SPACE.sRGB;

            //C# Snip. Org impl will now copy over any additional components.

            return true;
        }

        /// <remarks>
        /// 2.5 - color_esycc_to_rgb
        /// 
        /// This code has been adapted from sjpx_openjpeg.c of ghostscript
        /// </remarks>
        public bool ESyccToRGB(CompressionInfo cinfo = null)
        {
            if ((numcomps < 3) || ColorSpace != COLOR_SPACE.eYCC ||
                (comps[0].dx != comps[1].dx) ||
                (comps[0].dx != comps[2].dx) || 
                (comps[0].dy != comps[1].dy) ||
                (comps[0].dy != comps[2].dy)
            )
            {
                string msg = "color_esycc_to_rgb - Colorspace must be sYYC";
                if (cinfo == null)
                    throw new NotSupportedException(msg);
                cinfo.Error(msg);
                return false;
            }

            int flip_value = (1 << ((int)comps[0].prec - 1));
            int max_value = (1 << (int)comps[0].prec) - 1;

            uint w = comps[0].w;
            uint h = comps[0].h;

            bool sign1 = comps[1].sgnd;
            bool sign2 = comps[2].sgnd;

            uint max = w * h;

            for (uint i = 0; i < max; ++i)
            {
                int y = comps[0].data[i];
                int cb = comps[1].data[i];
                int cr = comps[2].data[i];

                if (!sign1)
                    cb -= flip_value;
                if (!sign2)
                    cr -= flip_value;

#if TEST_MATH_MODE
                int val = (int)(float)((float)((float)(y - (float)(0.0000368f * cb)) + (float)(1.40199f * cr)) + 0.5f);
#else
                int val = (int)(y - 0.0000368f * cb + 1.40199f * cr + 0.5f);
#endif

                if (val > max_value)
                    val = max_value;
                else if (val < 0)
                    val = 0;
                comps[0].data[i] = val;

#if TEST_MATH_MODE
                //Should probably have another (float)( ) before the +0.5f
                val = (int)((float)((float)(1.0003f * y) - (float)(0.344125f * cb)) - (float)(0.7141128f * cr) + 0.5f);
#else
                val = (int)(1.0003f * y - 0.344125f * cb - 0.7141128f * cr + 0.5f);
#endif

                if (val > max_value)
                    val = max_value;
                else if (val < 0)
                    val = 0;
                comps[1].data[i] = val;

#if TEST_MATH_MODE
                //Should probably have another (float)( ) before the +0.5f
                val = (int)((float)((float)(0.999823f * y) + (float)(1.77204 * cb)) - (float)(0.000008 * cr) + 0.5f);
#else
                val = (int)(0.999823f * y + 1.77204 * cb - 0.000008 * cr + 0.5f);
#endif

                if (val > max_value)
                    val = max_value;
                else if (val < 0)
                    val = 0;
                comps[2].data[i] = val;
            }
            color_space = COLOR_SPACE.sRGB;

            return true;
        }

        public bool SyccToRGB(CompressionInfo cinfo = null)
        {
            if (ColorSpace != COLOR_SPACE.sYCC)
            {
                string msg = "Colorspace must be sYYC";
                if (cinfo == null)
                    throw new NotSupportedException(msg);
                cinfo.Error(msg);
                return false;
            }

            if (numcomps < 3)
            {
                color_space = COLOR_SPACE.GRAY;
                return true;
            }

            if ((comps[0].dx == 1)
                && (comps[1].dx == 2)
                && (comps[2].dx == 2)
                && (comps[0].dy == 1)
                && (comps[1].dy == 2)
                && (comps[2].dy == 2))
            { /* horizontal and vertical sub-sample */
                sycc420_to_rgb();
            }
            else if ((comps[0].dx == 1)
                       && (comps[1].dx == 2)
                       && (comps[2].dx == 2)
                       && (comps[0].dy == 1)
                       && (comps[1].dy == 1)
                       && (comps[2].dy == 1))
            { /* horizontal sub-sample only */
                sycc422_to_rgb();
            }
            else if ((comps[0].dx == 1)
                       && (comps[1].dx == 1)
                       && (comps[2].dx == 1)
                       && (comps[0].dy == 1)
                       && (comps[1].dy == 1)
                       && (comps[2].dy == 1))
            { /* no sub-sample */
                sycc444_to_rgb();
            }
            else
            {
                string msg = "Can't covert this image to RGB";
                if (cinfo == null)
                    throw new NotImplementedException(msg);
                cinfo.Error(msg);
                return false;
            }

            return true;
        }

        private void sycc444_to_rgb()
        {
            int[] d0, d1, d2;
            int r, g, b;
            int y, cb, cr;
            long maxw, maxh, max, i;
            int offset, upb;

            upb = (int)comps[0].prec;
            offset = 1 << (upb - 1);
            upb = (1 << upb) - 1;

            maxw = comps[0].w;
            maxh = comps[0].h;
            max = maxw * maxh;

            int[] y_ar = comps[0].data;
            int[] cb_ar = comps[1].data;
            int[] cr_ar = comps[2].data;
            y = cb = cr = 0;

            d0 = new int[max];
            d1 = new int[max];
            d2 = new int[max];
            r = g = b = 0;

            for (i = 0U; i < max; ++i)
            {
                sycc_to_rgb(offset, upb, y_ar[y], cb_ar[cb], cr_ar[cr], out d0[r], out d1[g], out d2[b]);
                ++y;
                ++cb;
                ++cr;
                ++r;
                ++g;
                ++b;
            }
            comps[0].data = d0;
            comps[1].data = d1;
            comps[2].data = d2;
            color_space = COLOR_SPACE.sRGB;
        }

        private void sycc422_to_rgb()
        {
            int[] d0, d1, d2;
            int r, g, b;
            int y, cb, cr;
            long maxw, maxh, max, offx, loopmaxw;
            int offset, upb;
            long i;

            upb = (int)comps[0].prec;
            offset = 1 << (upb - 1);
            upb = (1 << upb) - 1;

            maxw = comps[0].w;
            maxh = comps[0].h;
            max = maxw * maxh;

            int[] y_ar = comps[0].data;
            int[] cb_ar = comps[1].data;
            int[] cr_ar = comps[2].data;
            y = cb = cr = 0;

            d0 = new int[max];
            d1 = new int[max];
            d2 = new int[max];
            r = g = b = 0;

            // if img->x0 is odd, then first column shall use Cb/Cr = 0
            offx = x0 & 1U;
            loopmaxw = maxw - offx;

            for (i = 0U; i < maxh; ++i)
            {
                long j;

                if (offx > 0U)
                {
                    sycc_to_rgb(offset, upb, y_ar[y], 0, 0, out d0[r], out d1[g], out d2[b]);
                    ++y;
                    ++r;
                    ++g;
                    ++b;
                }

                for (j = 0U; j < (loopmaxw & ~1L); j += 2U)
                {
                    sycc_to_rgb(offset, upb, y_ar[y], cb_ar[cb], cr_ar[cr], out d0[r], out d1[g], out d2[b]);
                    ++y;
                    ++r;
                    ++g;
                    ++b;
                    sycc_to_rgb(offset, upb, y_ar[y], cb_ar[cb], cr_ar[cr], out d0[r], out d1[g], out d2[b]);
                    ++y;
                    ++r;
                    ++g;
                    ++b;
                    ++cb;
                    ++cr;
                }
                if (j < loopmaxw)
                {
                    sycc_to_rgb(offset, upb, y_ar[y], cb_ar[cb], cr_ar[cr], out d0[r], out d1[g], out d2[b]);
                    ++y;
                    ++r;
                    ++g;
                    ++b;
                    ++cb;
                    ++cr;
                }
            }

            comps[0].data = d0;
            comps[1].data = d1;
            comps[2].data = d2;

            comps[1].w = comps[2].w = comps[0].w;
            comps[1].h = comps[2].h = comps[0].h;
            comps[1].dx = comps[2].dx = comps[0].dx;
            comps[1].dy = comps[2].dy = comps[0].dy;
            color_space = COLOR_SPACE.sRGB;
        }

        private void sycc420_to_rgb()
        {
            int[] d0, d1, d2;
            int r, g, b, nr, ng, nb;
            int y, cb, cr, ny;
            long maxw, maxh, max, offx, loopmaxw, offy, loopmaxh;
            int offset, upb;
            long i;

            upb = (int)comps[0].prec;
            offset = 1 << (upb - 1);
            upb = (1 << upb) - 1;

            maxw = comps[0].w;
            maxh = comps[0].h;
            max = maxw * maxh;

            int[] y_ar = comps[0].data;
            int[] cb_ar = comps[1].data;
            int[] cr_ar = comps[2].data;
            y = cb = cr = 0;

            d0 = new int[max];
            d1 = new int[max];
            d2 = new int[max];
            r = g = b = 0;

            // if x0 is odd, then first column shall use Cb/Cr = 0
            offx = x0 & 1U;
            loopmaxw = maxw - offx;
            // if y0 is odd, then first line shall use Cb/Cr = 0
            offy = y0 & 1U;
            loopmaxh = maxh - offy;

            if (offy > 0U)
            {
                long j;

                for (j = 0; j < maxw; ++j)
                {
                    sycc_to_rgb(offset, upb, y_ar[y], 0, 0, out d0[r], out d1[g], out d2[b]);
                    ++y;
                    ++r;
                    ++g;
                    ++b;
                }
            }

            for (i = 0U; i < (loopmaxh & ~1L); i += 2U)
            {
                long j;

                ny = (int)(y + maxw); // pointer to y_ar
                nr = (int)(r + maxw); // pointer to d0
                ng = (int)(g + maxw); // pointer to d1
                nb = (int)(b + maxw); // pointer to d2

                if (offx > 0U)
                {
                    sycc_to_rgb(offset, upb, y_ar[y], 0, 0, out d0[r], out d1[g], out d2[b]);
                    ++y;
                    ++r;
                    ++g;
                    ++b;
                    sycc_to_rgb(offset, upb, y_ar[ny], cb_ar[cb], cr_ar[cr], out d0[nr], out d1[ng], out d2[nb]);
                    ++ny;
                    ++nr;
                    ++ng;
                    ++nb;
                }

                for (j = 0; j < (loopmaxw & ~1L); j += 2U)
                {
                    sycc_to_rgb(offset, upb, y_ar[y], cb_ar[cb], cr_ar[cr], out d0[r], out d1[g], out d2[b]);
                    ++y;
                    ++r;
                    ++g;
                    ++b;
                    sycc_to_rgb(offset, upb, y_ar[y], cb_ar[cb], cr_ar[cr], out d0[r], out d1[g], out d2[b]);
                    ++y;
                    ++r;
                    ++g;
                    ++b;

                    sycc_to_rgb(offset, upb, y_ar[ny], cb_ar[cb], cr_ar[cr], out d0[nr], out d1[ng], out d2[nb]);
                    ++ny;
                    ++nr;
                    ++ng;
                    ++nb;
                    sycc_to_rgb(offset, upb, y_ar[ny], cb_ar[cb], cr_ar[cr], out d0[nr], out d1[ng], out d2[nb]);
                    ++ny;
                    ++nr;
                    ++ng;
                    ++nb;
                    ++cb;
                    ++cr;
                }
                if (j < loopmaxw)
                {
                    sycc_to_rgb(offset, upb, y_ar[y], cb_ar[cb], cr_ar[cr], out d0[r], out d1[g], out d2[b]);
                    ++y;
                    ++r;
                    ++g;
                    ++b;

                    sycc_to_rgb(offset, upb, y_ar[ny], cb_ar[cb], cr_ar[cr], out d0[nr], out d1[ng], out d2[nb]);
                    ++ny;
                    ++nr;
                    ++ng;
                    ++nb;
                    ++cb;
                    ++cr;
                }
                y += (int)maxw;
                r += (int)maxw;
                g += (int)maxw;
                b += (int)maxw;
            }
            if (i < loopmaxh)
            {
                long j;

                for (j = 0U; j < (maxw & ~1L); j += 2U)
                {
                    sycc_to_rgb(offset, upb, y_ar[y], cb_ar[cb], cr_ar[cr], out d0[r], out d1[g], out d2[b]);

                    ++y;
                    ++r;
                    ++g;
                    ++b;

                    sycc_to_rgb(offset, upb, y_ar[y], cb_ar[cb], cr_ar[cr], out d0[r], out d1[g], out d2[b]);

                    ++y;
                    ++r;
                    ++g;
                    ++b;
                    ++cb;
                    ++cr;
                }
                if (j < maxw)
                {
                    sycc_to_rgb(offset, upb, y_ar[y], cb_ar[cb], cr_ar[cr], out d0[r], out d1[g], out d2[b]);
                }
            }

            comps[0].data = d0;
            comps[1].data = d1;
            comps[2].data = d2;

            comps[1].w = comps[2].w = comps[0].w;
            comps[1].h = comps[2].h = comps[0].h;
            comps[1].dx = comps[2].dx = comps[0].dx;
            comps[1].dy = comps[2].dy = comps[0].dy;
            color_space = COLOR_SPACE.sRGB;
            return;
        }

        /// <remarks>
        /// Matrix for sYCC, Amendment 1 to IEC 61966-2-1
        ///
        /// Y :   0.299   0.587    0.114   :R
        /// Cb:  -0.1687 -0.3312   0.5     :G
        /// Cr:   0.5    -0.4187  -0.0812  :B
        ///
        /// Inverse:
        ///
        /// R: 1        -3.68213e-05    1.40199      :Y
        /// G: 1.00003  -0.344125      -0.714128     :Cb - 2^(prec - 1)
        /// B: 0.999823  1.77204       -8.04142e-06  :Cr - 2^(prec - 1)
        /// </remarks>
        private static void sycc_to_rgb(int offset, int upb, int y, int cb, int cr,
                        out int out_r, out int out_g, out int out_b)
        {
            int r, g, b;

            cb -= offset;
            cr -= offset;
            r = y + (int)(1.402 * (float)cr);
            if (r < 0)
            {
                r = 0;
            }
            else if (r > upb)
            {
                r = upb;
            }
            out_r = r;

            g = y - (int)(0.344 * (float)cb + 0.714 * (float)cr);
            if (g < 0)
            {
                g = 0;
            }
            else if (g > upb)
            {
                g = upb;
            }
            out_g = g;

            b = y + (int)(1.772 * (float)cb);
            if (b < 0)
            {
                b = 0;
            }
            else if (b > upb)
            {
                b = upb;
            }
            out_b = b;
        }

        /// <summary>
        /// Make the components have a uniform prec
        /// </summary>
        public void MakeUniformBPC()
        {
            var max_bpc = (uint) MaxBPC;
            double max_val = (double)((1 << (int)max_bpc) - 1);
            for (int c=0; c < comps.Length; c++)
            {
                var comp = comps[c];
                if (comp.prec < max_bpc)
                {
                    var ip = new Util.LinearInterpolator(0, (1 << (int)comp.prec) - 1, 0, max_val);
                    var d = comp.data;
                    for(int i=0; i<d.Length; i++)
                        d[i] = (int)Math.Round(ip.Interpolate(d[i]));

                    comp.bpp = (int)max_bpc;
                    comp.prec = max_bpc;
                }
            }
        }
    }
}
