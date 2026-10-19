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

namespace Nexaflow.Media.Jpeg2000.Internal
{
    /// <summary>
    /// Tag tree decoder
    /// 
    /// The tag tree is an efficient way of storing 2d matrixes. It
    /// basically works like this:
    /// 
    ///  The tag tree has a threshold. When decoding data it decodes
    ///  just enough to identify if the data is greater than or equal
    ///  to the treshold (given as a parameter) or the current value, 
    ///  and when said data is bigger (than either the current value
    ///  or the treshold) it updates the value, and returns if it was
    ///  bigger than the treshold.
    /// 
    ///  For more detail:
    ///  http://www.dmi.unict.it/~impoco/files/tutorial_JPEG2000.pdf
    ///  
    /// The tag tree is used for compressing the packet headers (not
    /// 2d matrixes) as they lend themselves to that.
    /// 
    /// </summary>
    /// <remarks>
    /// C# impl. note: Based on tgt.h and tgt.c
    ///
    /// The nodes are structs in one array, each naming its parent by index, as libopenjp2's point into theirs; a tree
    /// rebuilt for another precinct reuses the array when it is big enough.
    /// </remarks>
    internal sealed class TagTree
    {
        #region Variables and properties

        uint _numleafsh;
        uint _numleafsv;
        uint _numnodes;
        TagNode[] nodes;

        #endregion

        #region Init

        //2.5 - opj_tgt_create
        internal static TagTree Create(uint numleafsh, uint numleafsv, CompressionInfo cinfo)
        {
            var tt = new TagTree { nodes = [] };
            return tt.Build(numleafsh, numleafsv) ? tt : null;
        }

        //2.5
        private TagTree()
        { }

    /// <summary>
    /// Reinitialises a tag-tree from an existing one.
    /// </summary>
    /// <param name="num_leafs_h">the width of the array of leafs of the tree</param>
    /// <param name="num_leafs_v">the height of the array of leafs of the tree</param>
        //2.1
        internal void Init(uint num_leafs_h, uint num_leafs_v)
        {
            if (_numleafsh == num_leafs_h && _numleafsv == num_leafs_v)
                return;

            Build(num_leafs_h, num_leafs_v);
        }

    /// <summary>
    /// Sizes the tree for its leaves, links every node to its parent and resets them; false when it has no nodes.
    /// </summary>
    /// <remarks>2.5 - the body of opj_tgt_create and opj_tgt_init</remarks>
        private bool Build(uint num_leafs_h, uint num_leafs_v)
        {
            _numleafsh = num_leafs_h;
            _numleafsv = num_leafs_v;

            Span<int> nplh = stackalloc int[32];
            Span<int> nplv = stackalloc int[32];
            int num_levels = 0;
            nplh[0] = (int)num_leafs_h;
            nplv[0] = (int)num_leafs_v;
            _numnodes = 0;

            uint n;
            do
            {
                n = (uint)(nplh[num_levels] * nplv[num_levels]);
                nplh[num_levels + 1] = (nplh[num_levels] + 1) / 2;
                nplv[num_levels + 1] = (nplv[num_levels] + 1) / 2;
                _numnodes += n;
                ++num_levels;
            } while (n > 1);

            // ADD
            if (_numnodes == 0)
                return false;

            if (nodes.Length < _numnodes)
                nodes = new TagNode[_numnodes];

            // A node past the last has no parent: the root's parent, and the "invalid pointer" the original computes
            // one past the end of the nodes.
            int Parent(int at) => at >= _numnodes ? -1 : at;

            int node_pos = 0;
            int parent_pos = (int)(num_leafs_h * num_leafs_v);
            int parent0_pos = parent_pos;
            int parent = Parent(parent_pos);
            int parent0 = parent;

            for (int i = 0; i < num_levels - 1; i++)
            {
                for (int j = 0; j < nplv[i]; j++)
                {
                    int k = nplh[i];
                    while (--k >= 0)
                    {
                        nodes[node_pos++].parent = parent;
                        if (--k >= 0)
                            nodes[node_pos++].parent = parent;

                        parent = Parent(++parent_pos);
                    }
                    if ((j & 1) == 1 || j == nplv[i] - 1)
                    {
                        parent0 = parent;
                        parent0_pos = parent_pos;
                    }
                    else
                    {
                        parent = parent0;
                        parent_pos = parent0_pos;
                        parent0_pos += nplh[i];
                    }
                }
            }
            nodes[node_pos].parent = -1;

            Reset(this);
            return true;
        }

    /// <summary>
    /// Resets the tree
    /// </summary>
    /// <remarks>2.5 - opj_tgt_reset</remarks>
        internal static void Reset(TagTree tt)
        {
            if (tt == null) return;
            for (int i = 0; i < tt._numnodes; i++)
            {
                ref var current_node = ref tt.nodes[i];
                current_node.value = 999;
                current_node.low = 0;
            }
        }

        #endregion

    /// <remarks>2.5 - opj_tgt_decode</remarks>
        internal bool Decode(BIO bio, uint leafno, uint threshold)
        {
            // The path from the leaf to the root, walked back down from the root.
            Span<int> stk = stackalloc int[31];
            int stk_pos = -1;

            int node = (int)leafno;
            while (nodes[node].parent >= 0)
            {
                stk[++stk_pos] = node;
                node = nodes[node].parent;
            }

            for (int low = 0; ; )
            {
                ref var current = ref nodes[node];
                if (low > current.low)
                    current.low = low;
                else
                    low = current.low;
                while (low < threshold && low < current.value)
                {
                    if (bio.ReadBool())
                        current.value = low;
                    else
                        low++;
                }
                current.low = low;

                if (stk_pos < 0) break;

                //Gets a descendant/leaf node of this node
                node = stk[stk_pos--];
            }

            return nodes[node].value < threshold;
        }
    }

    /// <summary>A node of a tag tree: its parent's index, or -1 for the root, and what decoding has learned of it.</summary>
    internal struct TagNode
    {
        internal int parent;
        internal int value;
        internal int low;
    }
}
