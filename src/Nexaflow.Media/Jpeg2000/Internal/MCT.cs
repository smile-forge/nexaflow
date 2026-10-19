// Ported without nullable annotations; they are added as the file is reworked.
#nullable disable
using System;
using System.Diagnostics;

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
        ///2.5
        ///
        /// There's a SSE2 variant of this algo. 
        /// </remarks>
        internal static void Decode(int[] c0, int[] c1, int[] c2, int n)
        {
            for (int i = 0; i < n; ++i)
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
        /// Apply a reversible multi-component inverse transform to an image
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_mct_decode_real
        /// </remarks>
        /// <param name="c0">Samples for luminance component</param>
        /// <param name="c1">Samples for red chrominance component</param>
        /// <param name="c2">Samples for blue chrominance component</param>
        /// <param name="n">Number of samples for each component</param>
        internal static void DecodeReal(int[] c0, int[] c1, int[] c2, int n)
        {
            IntOrFloat y = new IntOrFloat();
            IntOrFloat u = new IntOrFloat();
            IntOrFloat v = new IntOrFloat();
            IntOrFloat r = new IntOrFloat();
            IntOrFloat g = new IntOrFloat();
            IntOrFloat b = new IntOrFloat();

            //C# Snip SSE2

            //C# Liberal application of (float) to reduce math precision to the same as the org. impl
            for (int i = 0; i < n; ++i)
            {
                y.I = c0[i];
                u.I = c1[i];
                v.I = c2[i];
#if !TEST_MATH_MODE
                r.F = y.F + (v.F * 1.402f);
                g.F = y.F - (u.F * 0.34413f) - (v.F * 0.71414f);
                b.F = y.F + (u.F * 1.772f);
#else
                r.F = y.F + (float)(v.F * 1.402f);
                g.F = y.F - (float)(u.F * 0.34413f) - (float)(v.F * 0.71414f);
                {
                    //c# This isn't more correct than the formula above, the goal
                    //   here is to have the exact same result as the c impl. 
                    //
                    //   This lets us do a byte for byte comparison check, with the
                    //   caviat that we're comparing against the result of a MSVC
                    //   build of the c libary.
                    //
                    //   Other builds may give different results.
                    g.F = y.F;
                    float tmp = u.F * 0.344130009f;
                    g.F -= tmp;
                    tmp = v.F * 0.714139998f;
                    g.F -= tmp;
                }
                b.F = y.F + (float)(u.F * 1.772f);
#endif
                c0[i] = r.I;
                c1[i] = g.I;
                c2[i] = b.I;

                //C# Code useful for math comparison
                //if (i == 12)
                //{
                //    IntOrFloat calc1, calc2, res;

                //    calc1.F = (float)(v.F * 0.714139998f);
                //    calc2.F = (float)(u.F * 0.344130009f);
                //    res.F = y.F;
                //    res.F -= calc2.F;
                //    res.F -= calc1.F;

                //    i = i;
                //}
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
