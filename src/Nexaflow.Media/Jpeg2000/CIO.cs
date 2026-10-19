// Ported without nullable annotations; they are added as the file is reworked.
#nullable disable
//#define DEBUG_STREAM
using System;
using System.IO;

namespace Nexaflow.Media.Jpeg2000
{
    /// <summary>
    /// This class represents a stream, with convenient
    /// methods for reading and writing bytes.
    /// </summary>
    /// <remarks>
    /// IO structure:
    /// 
    /// When reading: All reads are done through the CIO
    /// class. When reading bits, there's a bit oriented
    /// BIO class that reads from CIO. BIO will buffer up
    /// to one byte for that purpose.
    /// 
    /// When writing: CIO lays at the bottom. Writes to
    /// CIO goes straight to the underlying stream with
    /// no buffering done.
    /// 
    /// There's a buffered CIO class that has a 64KB
    /// buffer. It is used by most markers, though I've
    /// not done any testing to see if there's any 
    /// performance gained by buffering these small writes.
    /// One advantage in any case is that BCIO will
    /// automatically track the size of the marker, and
    /// write that size when commiting.
    /// 
    /// Bits are written using the WBIO class. It buffers
    /// up to 256 bytes, and has to be manually flushed
    /// after use.
    /// </remarks>
    internal class CIO // Character In/Out
    {
        #region Variables and properties

        /// <summary>
        /// Read buffer
        /// </summary>
        byte[] _bytes = new byte[4];

        /// <summary>
        /// Source or destination stream
        /// </summary>
        Stream _s;


        /// <summary>
        /// How this stream was opened
        /// </summary>
        readonly OpenMode _m;

        /// <summary>
        /// Owner reference
        /// </summary>
        /// <remarks>Is only used for a sanity check in one location, so may as well drop this</remarks>
        internal readonly CompressionInfo _cinfo;

        /// <summary>
        /// Get/Set position in the stream
        /// </summary>
        internal long Pos 
        { 
            get { return _s.Position; }
            set { _s.Position = value; }
        }

        /// <summary>
        /// Bytes left in the stream
        /// </summary>
        internal long BytesLeft 
        { 
            get 
            {
                return _s.Length - _s.Position;
            } 
        }

        /// <summary>
        /// Is the underlying stream seekable
        /// </summary>
        public bool CanSeek { get { return _s.CanSeek; } }

        #endregion

        #region Init

        internal CIO(CompressionInfo parent, Stream s, OpenMode m) 
        { 
            _s = s;
            _m = m;
            _cinfo = parent;
        }

        #endregion

        internal void Skip(uint n)
        {
            _s.Seek(n, SeekOrigin.Current);
        }

        #region Write

        #endregion

        #region Read

        public int Read(byte[] buf, int offset, int count)
        {
            for (int nToRead = count; nToRead > 0; )
            {
                int read = _s.Read(buf, offset, nToRead);
                if (read == 0)
                    return count - nToRead;

                offset += read;
                nToRead -= read;
            }
            return count;
        }

        /// <summary>
        /// Reads n bytes
        /// </summary>
        /// <param name="n">Number of bytes</param>
        /// <returns>Value</returns>
        public uint Read(uint n)
        {
            int i;
            uint v;
            v = 0;
            for (i = (int)(n - 1); i >= 0; i--)
            {
                v += (uint)ReadByte() << (i << 3);
            }
            return v;
        }

        /// <summary>
        /// Reads a boolean byte from the stream.
        /// </summary>
        public bool ReadBool()
        {
            int ret = _s.ReadByte();
            if (ret == -1) throw new EndOfStreamException();
            return ret != 0;
        }

        public byte ReadByte()
        {
            int ret = _s.ReadByte();
            if (ret == -1) throw new EndOfStreamException();
            return (byte)ret;
        }

        public ushort ReadUShort()
        {
            if (Read(_bytes, 0, 2) != 2)
                throw new EndOfStreamException();

            return (ushort)(_bytes[0] << 8 | _bytes[1]);
        }

        public int ReadInt()
        {
            if (Read(_bytes, 0, 4) != 4)
                throw new EndOfStreamException();

            return
                (_bytes[0]) << 24 |
                (_bytes[1]) << 16 |
                (_bytes[2]) << 8 |
                 _bytes[3];
        }

        public uint ReadUInt()
        {
            if (Read(_bytes, 0, 4) != 4)
                throw new EndOfStreamException();

            return
                ((uint)_bytes[0]) << 24 |
                ((uint)_bytes[1]) << 16 |
                ((uint)_bytes[2]) << 8 |
                 (uint)_bytes[3];
        }

        internal static uint ReadUInt(byte[] data, int pos)
        {
            return
                ((uint)data[pos + 0]) << 24 |
                ((uint)data[pos + 1]) << 16 |
                ((uint)data[pos + 2]) << 8 |
                 (uint)data[pos + 3];
        }

        #endregion
    }
}
