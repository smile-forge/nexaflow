using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Nexaflow.Media.Jbig2;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// The <c>JBIG2Globals</c> streams lately decoded with, keyed by their bytes. A scanned document's pages share one —
/// a symbol dictionary of every glyph on them — and a filter is handed the stream afresh with each page, so without
/// this every page would decode the whole dictionary again. Held to a few streams and to the memory their decoded
/// dictionaries take, the least lately used let go first; any number of pages may decode with one at a time.
/// </summary>
internal sealed class Jbig2GlobalsCache
{
    /// <summary>The most memory the dictionaries kept may take between them, in bytes.</summary>
    internal const long MemoryLimit = 64L * 1024 * 1024;

    /// <summary>The most streams kept.</summary>
    internal const int Capacity = 16;

    private readonly LinkedList<(string Key, Jbig2Globals Globals)> _byUse = [];
    private readonly Dictionary<string, LinkedListNode<(string Key, Jbig2Globals Globals)>> _byKey = new(StringComparer.Ordinal);
    private readonly System.Threading.Lock _gate = new();

    /// <summary>The globals these bytes hold: kept from an earlier page, or read now and kept.</summary>
    public Jbig2Globals Get(ReadOnlyMemory<byte> data)
    {
        var key = Convert.ToHexString(SHA256.HashData(data.Span));
        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out var node))
            {
                _byUse.Remove(node);
                _byUse.AddFirst(node);
            }
            else
            {
                node = _byUse.AddFirst((key, Jbig2Globals.Read(data.ToArray())));
                _byKey[key] = node;
            }

            Trim(node);
            return node.Value.Globals;
        }
    }

    /// <summary>How many streams are kept.</summary>
    internal int Count
    {
        get
        {
            lock (_gate) return _byUse.Count;
        }
    }

    /// <summary>Lets the least lately used go until the rest fit; the one just asked for always stays.</summary>
    private void Trim(LinkedListNode<(string Key, Jbig2Globals Globals)> keep)
    {
        while (_byUse.Count > 1 && (_byUse.Count > Capacity || _byUse.Sum(entry => entry.Globals.DecodedBytes) > MemoryLimit))
        {
            var last = _byUse.Last!;
            if (last == keep) break;
            _byUse.RemoveLast();
            _byKey.Remove(last.Value.Key);
        }
    }
}
