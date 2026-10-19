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
using System.IO;

namespace Nexaflow.Media.Jpeg2000
{
    /// <summary>
    /// This class contains all information needed
    /// for compression or decompression
    /// </summary>
    internal sealed class CompressionInfo
    {
        /// <summary>
        /// Version number of this libary
        /// </summary>
        public static string Version { get { return "2.5.3"; } }

        /// <summary>
        /// Type of compression or decompression
        /// </summary>
        CodecFormat _codec_format;

        /// <summary>
        /// Handles j2k stuff
        /// </summary>
        J2K _j2k;

        /// <summary>
        /// Handles JP2 stuff
        /// </summary>
        JP2 _jp2;

        /// <summary>
        /// For sending messages back to the client
        /// </summary>
        EventMgr _mgr;

        /// <summary>
        /// Get or set event manager
        /// </summary>
        public EventMgr EventManager { get { return _mgr; } set { _mgr = value; } }

        /// <summary>
        /// Optional data object for events
        /// </summary>
        public object ClientData { get; set; }

        /// <summary>
        /// Allow multithreading
        /// </summary>
        internal bool DisableMultiThreading { get; set; }

        /// <summary>
        /// Functions for decompresssing an image
        /// </summary>
        /// <remarks>Openjpeg 2.1 API</remarks>
        private Decompression DFuncs = new Decompression();

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="format">Whether the data is a bare codestream or a JP2 file</param>
        /// <remarks>
        /// opj_create_decompress
        /// </remarks>
        public CompressionInfo(CodecFormat format)
        {
            _codec_format = format;
            _j2k = J2K.Create(this);
            if (format == CodecFormat.Jpeg2P)
            {
                _jp2 = new JP2(this, _j2k);
                DFuncs.SetupDecoder = new Decompression.SetupDecoderFunc(_jp2.SetupDecode);
                DFuncs.ReadHeader = new Decompression.ReadHeaderFunc(_jp2.ReadHeader);
                DFuncs.SetDecodeArea = new Decompression.SetDecodeAreaFunc(_jp2.SetDecodeArea);
                DFuncs.Decode = new Decompression.DecodeFunc(_jp2.Decode);
                DFuncs.TileDecode = new Decompression.TileDecodeFunc(_jp2.Decode);
                DFuncs.EndDecompress = new Decompression.EndDecompressFunc(_jp2.EndDecompress);
            }
            else if (format == CodecFormat.Jpeg2K)
            {
                DFuncs.SetupDecoder = new Decompression.SetupDecoderFunc(_j2k.SetupDecode);
                DFuncs.ReadHeader = new Decompression.ReadHeaderFunc(_j2k.ReadHeader);
                DFuncs.SetDecodeArea = new Decompression.SetDecodeAreaFunc(_j2k.SetDecodeArea);
                DFuncs.Decode = new Decompression.DecodeFunc(_j2k.Decode);
                DFuncs.TileDecode = new Decompression.TileDecodeFunc(_j2k.Decode);
                DFuncs.EndDecompress = new Decompression.EndDecompressFunc(_j2k.EndDecompress);
            }
            else
                throw new NotSupportedException(format.ToString());
        }

        /// <summary>
        /// Configures the decoder
        /// </summary>
        /// <param name="cio">File data</param>
        /// <param name="parameters">Configuration</param>
        /// <returns>True if the setup was a success</returns>
        /// <remarks>
        /// 2.5
        /// </remarks>
        public bool SetupDecoder(CIO cio, DecompressionParameters parameters)
        {
            if (cio != null && cio.CanSeek && parameters != null)
            {
                DisableMultiThreading = parameters.DisableMultiThreading;
                DFuncs.SetupDecoder(cio, parameters);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reads the header of the file
        /// </summary>
        /// <param name="image">Image with header information</param>
        /// <returns>True on success</returns>
        /// <remarks>
        /// 2.5
        /// </remarks>
        public bool ReadHeader(out JPXImage image)
        {
            return DFuncs.ReadHeader(out image);
        }

        //2.5
        public bool SetDecodeArea(JPXImage image, int start_x, int start_y, int end_x, int end_y)
        {
            return DFuncs.SetDecodeArea(image, start_x, start_y, end_x, end_y);
        }

        /// <summary>
        /// Opens a CIO stream to read
        /// </summary>
        /// <param name="file">File to read</param>
        /// <remarks>
        /// Openjpeg 2.1 now supports streams. Yay. But since my impl.
        /// already supports streams, I'll keep this old 1.4 method.
        ///
        /// I.e. this is now eq. with "opj_stream_create_default_file_stream"
        /// </remarks>
        public CIO OpenCIO(Stream file)
        {
            if (file == null) throw new ArgumentNullException(); ;

            return new CIO(this, file, OpenMode.Read);
        }

        //2.5
        public bool Decode(JPXImage image)
        {
            if (image == null) throw new ArgumentNullException();

            return DFuncs.Decode(image);
        }

        public bool Decode(JPXImage image, uint tile_no)
        {
            if (image == null) throw new ArgumentNullException();

            return DFuncs.TileDecode(image, tile_no);
        }

        //2.5 - opj_end_decompress
        public bool EndDecompress()
        {
            return DFuncs.EndDecompress();
        }

        /// <summary>
        /// Emits a formated error string
        /// </summary>
        /// <param name="msg">Message</param>
        /// <param name="arg0">Parameters to put into the message</param>
        internal void Error(string msg, params object[] arg0)
        {
            if (_mgr == null || _mgr._error == null) return;

            _mgr._error(string.Format(msg, arg0), ClientData);
        }

        /// <summary>
        /// Emits a formated error string
        /// </summary>
        /// <param name="msg">Message</param>
        /// <param name="arg0">Parameters to put into the message</param>
        internal void ErrorMT(string msg, params object[] arg0)
        {
            if (_mgr == null || _mgr._error == null) return;

            //Locking on _mgr is not ideal, as this object is publically visible
            lock (_mgr) _mgr._error(string.Format(msg, arg0), ClientData);
        }

        /// <summary>
        /// Emits a formated info string
        /// </summary>
        /// <param name="msg">Message</param>
        /// <param name="arg0">Parameters to put into the message</param>
        internal void Info(string msg, params object[] arg0)
        {
            if (_mgr == null || _mgr._info == null) return;

            _mgr._info(string.Format(msg, arg0), ClientData);
        }

        /// <summary>
        /// Emits a formated warning string
        /// </summary>
        /// <param name="msg">Message</param>
        /// <param name="arg0">Parameters to put into the message</param>
        internal void Warn(string msg, params object[] arg0)
        {
            if (_mgr == null || _mgr._warning == null) return;

            _mgr._warning(string.Format(msg, arg0), ClientData);
        }

        /// <summary>
        /// Emits a formated warning string
        /// </summary>
        /// <param name="msg">Message</param>
        /// <param name="arg0">Parameters to put into the message</param>
        internal void WarnMT(string msg, params object[] arg0)
        {
            if (_mgr == null || _mgr._warning == null) return;

            //Locking on _mgr is not ideal, as this object is publically visible
            lock(_mgr) _mgr._warning(string.Format(msg, arg0), ClientData);
        }
    }
    internal struct Decompression
    {
        internal delegate void SetupDecoderFunc(CIO cio, DecompressionParameters param);
        internal delegate bool ReadHeaderFunc(out JPXImage image);
        internal delegate bool SetDecodeAreaFunc(JPXImage image, int start_x, int start_y, int end_x, int end_y);
        internal delegate bool DecodeFunc(JPXImage image);
        internal delegate bool TileDecodeFunc(JPXImage image, uint tile_index);
        internal delegate bool EndDecompressFunc();

        internal SetupDecoderFunc SetupDecoder;
        internal ReadHeaderFunc ReadHeader;
        internal SetDecodeAreaFunc SetDecodeArea;
        internal DecodeFunc Decode;
        internal TileDecodeFunc TileDecode;
        internal EndDecompressFunc EndDecompress;
    }
}
