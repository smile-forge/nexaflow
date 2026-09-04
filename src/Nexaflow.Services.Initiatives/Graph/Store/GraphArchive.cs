using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Syntax;

namespace Nexaflow.Services.Initiatives.Graph.Store;

/// <summary>
/// The on-disk form of a <see cref="GraphSnapshot"/>: one binary file, sectioned, with every string written
/// once.
/// <para>
/// It replaces a pair of JSON files that between them cost 300 MB and a second and a half to read on this
/// repo, nearly all of it parsing. Three things about the shape do that work. Strings are interned, which
/// matters because a node id is a long path and every edge carried two of them — the edges alone were 56 MB
/// of mostly repeated text. Sections are addressed from a table at the head, so a reader that wants the
/// graph does not pay for the per-file material, and one that wants a single file's material does not pay
/// for the graph. And each file's contribution is a self-contained record at a known offset, so refreshing
/// one file reads one record rather than all fifteen thousand.
/// </para>
/// <para>
/// It is derived, gitignored and regenerated, so there is no migration path and none is wanted: a file whose
/// magic, layout version or extractor schema version disagrees is not read at all, and the caller rebuilds.
/// </para>
/// </summary>
public static partial class GraphArchive
{
    private static readonly byte[] Magic = [0x4E, 0x46, 0x49, 0x47, 0x52, 0x50, 0x48];   // "NFIGRPH"

    /// <summary>Layout version. Bump on any change to how the bytes are arranged; an older file is then
    /// discarded rather than misread.</summary>
    public const int FormatVersion = 1;

    private const int SectionStrings       = 1;
    private const int SectionMetadata      = 2;
    private const int SectionNodes         = 3;
    private const int SectionEdges         = 4;
    private const int SectionHyperEdges    = 5;
    private const int SectionFiles         = 6;
    private const int SectionContributions = 7;

    /// <summary>The authored tree's own stamp. A section rather than a field so an archive written
    /// before it existed simply lacks it and reads as unknown, which costs one re-derivation instead of
    /// a forced rebuild of every graph on the machine.</summary>
    private const int SectionTreeStamp     = 8;

    // ── Writing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the snapshot to <paramref name="fullPath"/> atomically — a whole-repo graph is large enough
    /// that a reader meeting a half-written file is a real possibility rather than a theoretical one.
    /// </summary>
    public static void Write(string fullPath, GraphSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var tmp = fullPath + ".tmp";

        using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            WriteTo(file, snapshot);

        File.Move(tmp, fullPath, overwrite: true);
    }

    /// <summary>
    /// The bytes themselves, for a caller with its own destination. The string pool has to be complete
    /// before anything referring to it can be written, so each section is built in memory first and the
    /// pool is emitted in front of them.
    /// </summary>
    public static void WriteTo(Stream stream, GraphSnapshot snapshot)
    {
        var pool = new StringPool();

        var metadata      = Section(w => WriteMetadata(w, snapshot.Graph.Metadata, pool));
        var nodes         = Section(w => WriteNodes(w, snapshot.Graph.Nodes, pool));
        var edges         = Section(w => WriteEdges(w, snapshot.Graph.Edges, pool));
        var hyper         = Section(w => WriteHyperEdges(w, snapshot.Graph.HyperEdges, pool));
        var contributions = WriteContributions(snapshot.Cache, pool, out var records);
        var files         = Section(w => WriteFiles(w, snapshot, records, pool));
        var tree          = Section(w => WriteStamp(w, snapshot.Tree, pool));
        var strings       = Section(pool.WriteTo);   // built last, written first: it defines all the rest

        (int Id, byte[] Bytes)[] body =
        [
            (SectionStrings, strings), (SectionMetadata, metadata), (SectionNodes, nodes),
            (SectionEdges, edges), (SectionHyperEdges, hyper), (SectionFiles, files),
            (SectionContributions, contributions), (SectionTreeStamp, tree),
        ];

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(GraphSchema.Version);
        writer.Write(body.Length);

        // The table is fixed width, so the first section's offset is known before any section is written.
        long offset = Magic.Length + sizeof(int) * 3 + body.Length * (sizeof(int) + sizeof(long) * 2);
        foreach (var (id, bytes) in body)
        {
            writer.Write(id);
            writer.Write(offset);
            writer.Write((long)bytes.Length);
            offset += bytes.Length;
        }
        foreach (var (_, bytes) in body) writer.Write(bytes);
    }

    private static byte[] Section(Action<BinaryWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true)) write(writer);
        return buffer.ToArray();
    }

    private static void WriteMetadata(BinaryWriter w, GraphMetadata m, StringPool pool)
    {
        w.Write(m.NodeCount);
        w.Write(m.EdgeCount);
        w.Write(m.HyperEdgeCount);
        w.Write(m.CommunityCount);
        w.Write(m.SchemaVersion);
        w.Write(pool.Id(m.GeneratedAt));
        w.Write(pool.Id(m.Scope));
        w.Write(pool.Id(m.ProductName));
    }

    private static void WriteNodes(BinaryWriter w, List<GraphNode> nodes, StringPool pool)
    {
        w.Write(nodes.Count);
        foreach (var n in nodes)
        {
            w.Write(pool.Id(n.Id));
            w.Write(pool.Id(n.Type));
            w.Write(pool.Id(n.Label));
            w.Write(pool.Id(n.FilePath));
            w.Write(pool.Id(n.Language));
            WriteNullableInt(w, n.Community);
            w.Write(n.Confidence);
            w.Write(pool.Id(n.Source));
            WriteMap(w, n.Metadata, pool);
        }
    }

    private static void WriteEdges(BinaryWriter w, List<GraphEdge> edges, StringPool pool)
    {
        w.Write(edges.Count);
        foreach (var e in edges)
        {
            w.Write(pool.Id(e.Source));
            w.Write(pool.Id(e.Target));
            w.Write(pool.Id(e.Relationship));
            w.Write(e.Weight);
            w.Write(e.Confidence);
            w.Write(pool.Id(e.ProvenanceFile));
            WriteMap(w, e.Metadata, pool);
        }
    }

    private static void WriteHyperEdges(BinaryWriter w, List<GraphHyperEdge> hyper, StringPool pool)
    {
        w.Write(hyper.Count);
        foreach (var h in hyper)
        {
            w.Write(pool.Id(h.Relationship));
            w.Write(h.Weight);
            w.Write(h.Confidence);
            w.Write(pool.Id(h.ProvenanceFile));
            WriteMap(w, h.Metadata, pool);
            w.Write(h.Endpoints.Count);
            foreach (var e in h.Endpoints)
            {
                w.Write(pool.Id(e.Node));
                w.Write(pool.Id(e.Role));
                WriteNullableInt(w, e.Ordinal);
                WriteNullableDouble(w, e.Confidence);
            }
        }
    }

    /// <summary>
    /// Every file's contribution, each a self-contained record, with the offsets handed back so the file
    /// index can point at them. That indirection is the whole point: a refresh reads one record.
    /// </summary>
    private static byte[] WriteContributions(GraphCache cache, StringPool pool,
                                             out Dictionary<string, (long Offset, int Length)> records)
    {
        records = new Dictionary<string, (long, int)>(cache.Files.Count, StringComparer.Ordinal);
        using var buffer = new MemoryStream();

        foreach (var (path, contribution) in cache.Files)
        {
            var at = buffer.Position;
            using (var w = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
                WriteContribution(w, contribution, pool);
            records[path] = (at, (int)(buffer.Position - at));
        }
        return buffer.ToArray();
    }

    private static void WriteContribution(BinaryWriter w, FileContribution c, StringPool pool)
    {
        w.Write(pool.Id(c.Hash));
        WriteNodes(w, c.Nodes, pool);
        WriteEdges(w, c.Edges, pool);

        w.Write(c.Bases.Count);
        foreach (var b in c.Bases)
        {
            w.Write(pool.Id(b.TypeId));
            w.Write(pool.Id(b.Name));
            w.Write(b.IsInterface);
        }

        w.Write(c.Refs.Count);
        foreach (var r in c.Refs)
        {
            w.Write(pool.Id(r.FromAst));
            w.Write((int)r.Kind);
            w.Write(pool.Id(r.Name));
            w.Write(r.Line);
        }

        w.Write(c.Signatures.Count);
        foreach (var s in c.Signatures)
        {
            w.Write(pool.Id(s.MemberAst));
            w.Write(pool.Id(s.ReturnType));
            WriteList(w, s.ParamTypes, pool);
            w.Write(s.Line);
        }

        w.Write(c.Attributes.Count);
        foreach (var a in c.Attributes)
        {
            w.Write(pool.Id(a.TargetAst));
            w.Write(pool.Id(a.AttrName));
            w.Write(a.ArgCount);
            w.Write(a.Line);
        }

        w.Write(c.Calls.Count);
        foreach (var call in c.Calls)
        {
            w.Write(pool.Id(call.FromAst));
            w.Write(pool.Id(call.Callee));
            WriteList(w, call.NewArgTypes, pool);
            w.Write(call.Line);
        }

        w.Write(c.FileRefs.Count);
        foreach (var f in c.FileRefs)
        {
            w.Write(pool.Id(f.FromAst));
            w.Write(pool.Id(f.Token));
            w.Write(f.Line);
        }
    }

    private static void WriteFiles(BinaryWriter w, GraphSnapshot snapshot,
                                   Dictionary<string, (long Offset, int Length)> records, StringPool pool)
    {
        w.Write(records.Count);
        foreach (var (path, record) in records)
        {
            var stamp = snapshot.Files.TryGetValue(path, out var s) ? s : default;
            w.Write(pool.Id(path));
            w.Write(pool.Id(stamp.Hash ?? snapshot.Cache.Files[path].Hash));
            w.Write(stamp.Length);
            w.Write(stamp.ModifiedUtc.Ticks);
            w.Write(record.Offset);
            w.Write(record.Length);
        }
    }

    private static void WriteStamp(BinaryWriter w, FileStamp stamp, StringPool pool)
    {
        w.Write(pool.Id(stamp.Hash));
        w.Write(stamp.Length);
        w.Write(stamp.ModifiedUtc.Ticks);
    }

    private static void WriteMap(BinaryWriter w, Dictionary<string, string>? map, StringPool pool)
    {
        if (map is null) { w.Write(-1); return; }
        w.Write(map.Count);
        foreach (var (key, value) in map) { w.Write(pool.Id(key)); w.Write(pool.Id(value)); }
    }

    private static void WriteList(BinaryWriter w, IReadOnlyList<string> list, StringPool pool)
    {
        w.Write(list.Count);
        foreach (var s in list) w.Write(pool.Id(s));
    }

    private static void WriteNullableInt(BinaryWriter w, int? value)
    {
        w.Write(value.HasValue);
        if (value.HasValue) w.Write(value.Value);
    }

    private static void WriteNullableDouble(BinaryWriter w, double? value)
    {
        w.Write(value.HasValue);
        if (value.HasValue) w.Write(value.Value);
    }

    /// <summary>Every distinct string in the file, written once and referred to by index. Null is -1 rather
    /// than an entry, so a null and an empty string stay different things.</summary>
    private sealed class StringPool
    {
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
        private readonly List<string> _strings = [];

        public int Id(string? value)
        {
            if (value is null) return -1;
            if (_ids.TryGetValue(value, out var id)) return id;
            _ids[value] = id = _strings.Count;
            _strings.Add(value);
            return id;
        }

        public void WriteTo(BinaryWriter w)
        {
            w.Write(_strings.Count);
            foreach (var s in _strings) w.Write(s);
        }
    }
}
