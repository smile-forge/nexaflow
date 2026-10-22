// Ported without nullable annotations; they are added as the file is reworked.
#nullable disable
#region License
/*
 * Based on PdfLib.BitStream.cs
 * 
 * This class is contributed to the Public Domain.
 * Use it at your own risk.
 */
#endregion
using System.IO;

namespace Nexaflow.Media.Jpeg2000.Internal
{
    /// <summary>
    /// Bit In/Out
    /// </summary>
    /// <remarks>
    /// Based on PdfLib.BitStream, but this is not a plain
    /// bit reader as some bits are automatically skipped
    /// </remarks>
    internal class BIO
    {
        #region Variables and properties

        /// <summary>
        /// The data
        /// </summary>
        readonly byte[] _data;

        /// <summary>
        /// Position in the data stream
        /// </summary>
        int _data_pos, _start_pos, _end_pos;

        /// <summary>
        /// A buffer containing up to 8 bits
        /// </summary>
        /// <remarks>
        /// Biggest read is 7 bits, with one potential exception 
        /// that may exceed the 24-bit limit of the org impl.
        /// anyway.
        /// </remarks>
        internal int bit_buffer = 0;

        /// <summary>
        /// Encoder: Number of free bites in the buffer
        /// Decoder: Number of bits in the buffer
        /// </summary>
        internal int n_buf_bits = 0;

        /// <summary>
        /// Mask for the first bit in the bit buffer
        /// </summary>
        const byte FIRST_BIST = 0x80;

        internal int Position { get { return _data_pos - _start_pos; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="data">Data to read from</param>
        /// <param name="start_pos">Position to start reading from</param>
        internal BIO(byte[] data, int start_pos, int end_pos)
        {
            _data = data;
            _data_pos = start_pos;
            _start_pos = start_pos;
            _end_pos = end_pos;
        }

        #endregion

        ///// <summary>
        ///// Can read up to 32 bits out of the bitstream.
        ///// </summary>
        ///// <param name="n">Number of bits to fetch, max is 32</param>
        ///// <returns>The bits in the lower end of a int</returns>
        ///// <remarks>
        ///// Special note: 0xff skips the next bit. Because of
        ///// this I suspect that there's never reads above 8 bits, so
        ///// this function may not be needed.
        ///// </remarks>
        //internal int ReadInt(int n)
        //{
        //    int ret = 0;
        //    while (n > 8)
        //    {
        //        //Debug.Assert(false, "I suspect there will never be a need for this");
        //        ret = ret << 8 | Read(8);
        //        n -= 8;
        //    }
        //    return ret << n | Read(n);
        //}

        /// <summary>
        /// Can read up to 32 bits out of the bitstream.
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 32</param>
        /// <returns>The bits in the lower end of a int</returns>
        /// <remarks>
        /// Special note: 0xff skips the next bit. Because of
        /// this I suspect that there's never reads above 8 bits, so
        /// this function may not be needed.
        /// </remarks>
        internal uint ReadUInt(uint n)
        {
            uint ret = 0;
            while (n > 8)
            {
                //Debug.Assert(false, "I suspect there will never be a need for this");
                ret = ret << 8 | (uint) Read(8);
                n -= 8;
            }
            return ret << (int) n | (uint) Read((int) n);
        }

        /// <summary>
        /// Can read up to 32 bits out of the bitstream. Reads "0" if reading
        /// beyond the end of the stream.
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 32</param>
        /// <returns>The bits in the lower end of a int</returns>
        /// <remarks>
        /// Special note: 0xff skips the next bit. Because of
        /// this I suspect that there's never reads above 8 bits, so
        /// this function may not be needed.
        /// </remarks>
        internal uint ReadUInt0(uint n)
        {
            uint ret = 0;
            while (n > 8)
            {
                //Debug.Assert(false, "I suspect there will never be a need for this");
                ret = ret << 8 | (uint)Read0(8);
                n -= 8;
            }
            return ret << (int)n | (uint)Read0((int)n);
        }

        /// <summary>
        /// Reads bits out of the bitstream.
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 8</param>
        /// <returns>The bits in the lower end of a int</returns>
        internal int Read(int n)
        {
            int ret = (bit_buffer & 0xff) >> (8 - n);
            if (n > n_buf_bits)
            {
                n -= n_buf_bits;
                if (_data_pos == _end_pos)
                    throw new EndOfStreamException();
                if (((bit_buffer << n_buf_bits) & 0xff00) == 0xff00)
                {
                    //Skips over 1 bit of data
                    bit_buffer = _data[_data_pos++];
                    bit_buffer <<= 1;
                    n_buf_bits = 7 - n;
                    ret |= (bit_buffer & 0xff) >> (8 - n);
                }
                else
                {
                    bit_buffer = _data[_data_pos++];
                    n_buf_bits = 8 - n;
                    ret |= (bit_buffer & 0xff) >> n_buf_bits;
                }
            }
            else
                n_buf_bits -= n;
            bit_buffer <<= n;
            return ret;
        }

        /// <summary>
        /// Reads bits out of the bitstream.
        /// 
        /// In the original impl, if one read beyond the end of a
        /// stream it simply read "0" bits. To match that impl.
        /// we use this function.
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 8</param>
        /// <returns>
        /// The bits in the lower end of a int, or 0 at the end of
        /// the stream.
        /// </returns>
        internal int Read0(int n)
        {
            int ret = (bit_buffer & 0xff) >> (8 - n);
            if (n > n_buf_bits)
            {
                n -= n_buf_bits;
                if (((bit_buffer << n_buf_bits) & 0xff00) == 0xff00)
                {
                    //Skips over 1 bit of data
                    if (_data_pos == _end_pos)
                        bit_buffer = 0;
                    else
                        bit_buffer = _data[_data_pos++];
                    bit_buffer <<= 1;
                    n_buf_bits = 7 - n;
                    ret |= (bit_buffer & 0xff) >> (8 - n);
                }
                else
                {
                    if (_data_pos == _end_pos)
                        bit_buffer = 0;
                    else
                        bit_buffer = _data[_data_pos++];
                    n_buf_bits = 8 - n;
                    ret |= (bit_buffer & 0xff) >> n_buf_bits;
                }
            }
            else
                n_buf_bits -= n;
            bit_buffer <<= n;
            return ret;
        }

        /// <summary>
        /// Reads a single bit out of the bitstream
        /// </summary>
        /// <returns>True if the bit was 1</returns>
        internal bool ReadBool()
        {
            if (n_buf_bits == 0)
            {
                if (_data_pos == _end_pos)
                    return false; //<-- Yes, this is what the org impl. does
                bit_buffer |= _data[_data_pos++];
                n_buf_bits = 8;
                if ((bit_buffer & 0xff00) == 0xff00)
                {
                    //Skips one bit
                    bit_buffer <<= 1;
                    n_buf_bits--;
                }
            }
            bool ret = (bit_buffer & FIRST_BIST) == FIRST_BIST;
            bit_buffer <<= 1;
            n_buf_bits--;
            return ret;
        }

        /// <summary>
        /// Aligns the stream to the next byte.
        /// </summary>
        /// <returns>True if an End of Stream</returns>
        /// <remarks>
        /// Don't read into the buffer as there's code 
        /// reading the position afterward
        /// </remarks>
        internal bool ByteAlign()
        {
            //Skips over a byte if the buffer contains 0xff
            //C# impl. note: Since I bitshift the buffer, make
            // that 0xff00
            if (((bit_buffer << n_buf_bits) & 0xff00) == 0xff00)
            {
                _data_pos++;
                if (_data_pos == _end_pos)
                    return true;
            }
            n_buf_bits = 0;
            return false;
        }
    }
}
