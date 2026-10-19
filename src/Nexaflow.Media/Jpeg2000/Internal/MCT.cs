// Ported without nullable annotations; they are added as the file is reworked.
#nullable disable
using System;
using System.Diagnostics;
using System.Runtime.Intrinsics;

namespace Nexaflow.Media.Jpeg2000.Internal
{
    /// <summary>
    /// Multi-component transforms (MCT)
    /// </summary>
    internal static class MCT
    {
        internal static uint[] ELEMENT_SIZE = { 2, 4, 4, 8 };

        /// <remarks>
        /// 2.5
        /// 
        /// C# Original implementation shifts between floats and integers. We work
        ///    around this headace by using an interopt struct. It's probably not
        ///    the best solution.
        ///    
        ///    What I need is to be able to convert a int to a float, without actually
        ///    converting anything. 
        /// </remarks>
        internal static void DecodeCustom(float[] mct, int n, int[][] data, uint n_comps, bool isSigned)
        {
            IntOrFloat[] current_data = new IntOrFloat[n_comps];
            IntOrFloat[] current_result = new IntOrFloat[n_comps]; 

            for (int i = 0, d = 0; i < n; i++)
            {
                int mct_c = 0;
                for (int j = 0; j < data.Length; j++)
                    current_data[j].I = data[j][d];

                for (int j = 0; j < data.Length; j++)
                {
                    current_result[j].F = 0;
                    for (int k = 0; k < n_comps; k++)
                        current_result[j].F += mct[mct_c++] * current_data[k].F;

                    data[j][d++] = current_result[j].I;
                }
            }
        }

        /// <summary>
        /// Apply a reversible multi-component inverse transform to an image
        /// </summary>
        /// <param name="c0">Samples for luminance component</param>
        /// <param name="c1">Samples for red chrominance component</param>
        /// <param name="c2">Samples for blue chrominance component</param>
        /// <param name="n">Number of samples for each component</param>
        ///<remarks>
        /// 2.5 - opj_mct_decode
        ///
        /// Eight samples at a time, as libopenjp2's SSE2 variant does four: integer arithmetic, so exactly the scalar result.
        /// </remarks>
        internal static void Decode(int[] c0, int[] c1, int[] c2, int n)
        {
            var i = 0;
            for (; i + Vector256<int>.Count <= n; i += Vector256<int>.Count)
            {
                var y = Vector256.Create(c0.AsSpan(i, Vector256<int>.Count));
                var u = Vector256.Create(c1.AsSpan(i, Vector256<int>.Count));
                var v = Vector256.Create(c2.AsSpan(i, Vector256<int>.Count));
                var g = y - Vector256.ShiftRightArithmetic(u + v, 2);
                (v + g).CopyTo(c0.AsSpan(i, Vector256<int>.Count));
                g.CopyTo(c1.AsSpan(i, Vector256<int>.Count));
                (u + g).CopyTo(c2.AsSpan(i, Vector256<int>.Count));
            }

            for (; i < n; ++i)
            {
                int y = c0[i];
                int u = c1[i];
                int v = c2[i];
                int g = y - ((u + v) >> 2);
                int r = v + g;
                int b = u + g;
                c0[i] = r;
                c1[i] = g;
                c2[i] = b;
            }
        }

        /// <summary>
        /// Apply an irreversible multi-component inverse transform to an image
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_mct_decode_real
        ///
        /// The components hold the bits of floats. Eight samples at a time, each lane the scalar's products and sums in its
        /// order at single precision, with no fused multiply-add, so the result is bit for bit the scalar one.
        /// </remarks>
        /// <param name="c0">Samples for luminance component</param>
        /// <param name="c1">Samples for red chrominance component</param>
        /// <param name="c2">Samples for blue chrominance component</param>
        /// <param name="n">Number of samples for each component</param>
        internal static void DecodeReal(int[] c0, int[] c1, int[] c2, int n)
        {
            var i = 0;
            for (; i + Vector256<int>.Count <= n; i += Vector256<int>.Count)
            {
                var y = Vector256.Create(c0.AsSpan(i, Vector256<int>.Count)).AsSingle();
                var u = Vector256.Create(c1.AsSpan(i, Vector256<int>.Count)).AsSingle();
                var v = Vector256.Create(c2.AsSpan(i, Vector256<int>.Count)).AsSingle();
                (y + v * 1.402f).AsInt32().CopyTo(c0.AsSpan(i, Vector256<int>.Count));
                (y - u * 0.34413f - v * 0.71414f).AsInt32().CopyTo(c1.AsSpan(i, Vector256<int>.Count));
                (y + u * 1.772f).AsInt32().CopyTo(c2.AsSpan(i, Vector256<int>.Count));
            }

            for (; i < n; ++i)
            {
                float y = BitConverter.Int32BitsToSingle(c0[i]);
                float u = BitConverter.Int32BitsToSingle(c1[i]);
                float v = BitConverter.Int32BitsToSingle(c2[i]);
                c0[i] = BitConverter.SingleToInt32Bits(y + (v * 1.402f));
                c1[i] = BitConverter.SingleToInt32Bits(y - (u * 0.34413f) - (v * 0.71414f));
                c2[i] = BitConverter.SingleToInt32Bits(y + (u * 1.772f));
            }
        }
    }


    internal enum MCT_ELEMENT_TYPE
    {
        NULL = -1,

        /// <summary>
        /// MCT data is stored as signed shorts
        /// </summary>
        MCT_TYPE_INT16 = 0,

        /// <summary>
        /// MCT data is stored as signed integers
        /// </summary>
        MCT_TYPE_INT32 = 1,

        /// <summary>
        /// MCT data is stored as floats
        /// </summary>
        MCT_TYPE_FLOAT = 2,

        /// <summary>
        /// MCT data is stored as doubles
        /// </summary>
        MCT_TYPE_DOUBLE = 3,
    }

    internal enum MCT_ARRAY_TYPE
    {
        MCT_TYPE_DEPENDENCY = 0,
        MCT_TYPE_DECORRELATION = 1,
        MCT_TYPE_OFFSET = 2,
    }

    /// <summary>
    /// Multi component transform data
    /// </summary>
    internal class MctData : ICloneable
    {
        public MCT_ELEMENT_TYPE element_type;
        public MCT_ARRAY_TYPE array_type;
        public int index;
        public ShortOrIntOrFloatOrDoubleAr data;
        public int data_size;

        public object Clone() { return MemberwiseClone(); }
        //Size of the data in bytes. 
        //public int data_size;
    }

    internal class SimpleMccDecorrelationData : ICloneable
    {
        public uint index;
        public uint n_comps;
        public ArPtr<MctData> decorrelation_array;
        public ArPtr<MctData> offset_array;
        public bool is_irreversible;

        public object Clone()
        {
            var clone = (SimpleMccDecorrelationData)MemberwiseClone();
            if (decorrelation_array != null)
            {
                var ap = new ArPtr<MctData>(new MctData[decorrelation_array.Data.Length], decorrelation_array.Pos);
                for(int c=0; c < ap.Data.Length; c++)
                    ap.Data[c] = (MctData) decorrelation_array.Data[c].Clone();
                clone.decorrelation_array = ap;
            }
            if (offset_array != null)
            {
                var ap = new ArPtr<MctData>(new MctData[offset_array.Data.Length], offset_array.Pos);
                for (int c = 0; c < ap.Data.Length; c++)
                    ap.Data[c] = (MctData) offset_array.Data[c].Clone();
                clone.offset_array = ap;
            }
            return clone;
        }
    }
}
