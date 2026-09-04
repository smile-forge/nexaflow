using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Syntax;

namespace Nexaflow.Services.Initiatives.Graph.Store;

public static partial class GraphArchive
{
    // ── Reading ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole snapshot, or null when there isn't one to read — no file, not this format, or written by a
    /// different layout or extractor version. Null is the normal answer as often as the exceptional one: it
    /// is what a first run and a schema bump both look like, and both mean the same thing to the caller,
    /// which is "build it".
    /// </summary>
    public static GraphSnapshot? Read(string fullPath) =>
        Open(fullPath, (reader, sections, pool) => new GraphSnapshot
        {
            Graph = ReadGraphFrom(reader, sections, pool),
            Cache = ReadCacheFrom(reader, sections, pool),
            Files = ReadFilesFrom(reader, sections, pool).Stamps,
            Tree  = ReadTreeStamp(reader, sections, pool),
        });

    /// <summary>
    /// Just the assembled graph — every query wants this and none of them wants the per-file material, which
    /// is two thirds of the file. Skipping a section costs nothing to skip, which is the reason the sections
    /// exist.
    /// </summary>
    public static KnowledgeGraph? ReadGraph(string fullPath) =>
        Open(fullPath, ReadGraphFrom);

    /// <summary>Just the file stamps, for a caller deciding what needs re-extracting before it commits to
    /// loading anything else.</summary>
    public static Dictionary<string, FileStamp>? ReadFileIndex(string fullPath) =>
        Open(fullPath, (reader, sections, pool) => ReadFilesFrom(reader, sections, pool).Stamps);

    /// <summary>Just the per-file extraction, for a rebuild that will reuse what has not changed.</summary>
    public static GraphCache? ReadCache(string fullPath) =>
        Open(fullPath, ReadCacheFrom);

    /// <summary>
    /// Opens the file, checks it is one of ours and current, reads the string pool, and hands the rest to
    /// <paramref name="read"/>. Any failure to read is the same answer as no file: this is a cache, and a
    /// caller that has to distinguish "absent" from "corrupt" in order to proceed does not exist.
    /// </summary>
    private static T? Open<T>(string fullPath, Func<BinaryReader, Dictionary<int, SectionSpan>, string?[], T> read)
        where T : class
    {
        if (!File.Exists(fullPath)) return null;

        try
        {
            using var file   = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            using var reader = new BinaryReader(file, Encoding.UTF8);

            var magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length) return null;
            for (var i = 0; i < Magic.Length; i++) if (magic[i] != Magic[i]) return null;

            if (reader.ReadInt32() != FormatVersion) return null;
            if (reader.ReadInt32() != GraphSchema.Version) return null;

            var count    = reader.ReadInt32();
            var sections = new Dictionary<int, SectionSpan>(count);
            for (var i = 0; i < count; i++)
            {
                var id = reader.ReadInt32();
                sections[id] = new SectionSpan(reader.ReadInt64(), reader.ReadInt64());
            }

            return read(reader, sections, ReadStrings(reader, sections));
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or InvalidDataException)
        {
            return null;
        }
    }

    private readonly record struct SectionSpan(long Offset, long Length);

    private static string?[] ReadStrings(BinaryReader reader, Dictionary<int, SectionSpan> sections)
    {
        if (!Seek(reader, sections, SectionStrings)) return [];

        var count   = reader.ReadInt32();
        var strings = new string?[count];
        for (var i = 0; i < count; i++) strings[i] = reader.ReadString();
        return strings;
    }

    /// <summary>Positions the reader at the start of a section, or reports that the file does not carry it —
    /// which a file written by an older layout legitimately might.</summary>
    private static bool Seek(BinaryReader reader, Dictionary<int, SectionSpan> sections, int id)
    {
        if (!sections.TryGetValue(id, out var section)) return false;
        reader.BaseStream.Seek(section.Offset, SeekOrigin.Begin);
        return true;
    }

    private static KnowledgeGraph ReadGraphFrom(BinaryReader reader, Dictionary<int, SectionSpan> sections,
                                                string?[] pool)
    {
        var graph = new KnowledgeGraph();

        if (Seek(reader, sections, SectionMetadata))
            graph.Metadata = new GraphMetadata
            {
                NodeCount      = reader.ReadInt32(),
                EdgeCount      = reader.ReadInt32(),
                HyperEdgeCount = reader.ReadInt32(),
                CommunityCount = reader.ReadInt32(),
                SchemaVersion  = reader.ReadInt32(),
                GeneratedAt    = Str(reader, pool),
                Scope          = Str(reader, pool),
                ProductName    = Str(reader, pool),
            };

        if (Seek(reader, sections, SectionNodes))      graph.Nodes      = ReadNodes(reader, pool);
        if (Seek(reader, sections, SectionEdges))      graph.Edges      = ReadEdges(reader, pool);
        if (Seek(reader, sections, SectionHyperEdges)) graph.HyperEdges = ReadHyperEdges(reader, pool);

        return graph;
    }

    private static GraphCache ReadCacheFrom(BinaryReader reader, Dictionary<int, SectionSpan> sections,
                                            string?[] pool)
    {
        var cache = new GraphCache { SchemaVersion = GraphSchema.Version };
        if (!sections.TryGetValue(SectionContributions, out var body)) return cache;

        foreach (var (path, record) in ReadFilesFrom(reader, sections, pool).Records)
        {
            reader.BaseStream.Seek(body.Offset + record.Offset, SeekOrigin.Begin);
            cache.Files[path] = ReadContribution(reader, pool);
        }
        return cache;
    }

    private static (Dictionary<string, FileStamp> Stamps, List<(string Path, Record Record)> Records)
        ReadFilesFrom(BinaryReader reader, Dictionary<int, SectionSpan> sections, string?[] pool)
    {
        var stamps  = new Dictionary<string, FileStamp>(StringComparer.Ordinal);
        var records = new List<(string, Record)>();
        if (!Seek(reader, sections, SectionFiles)) return (stamps, records);

        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var path     = Str(reader, pool) ?? string.Empty;
            var hash     = Str(reader, pool) ?? string.Empty;
            var length   = reader.ReadInt64();
            var modified = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var offset   = reader.ReadInt64();
            var size     = reader.ReadInt32();

            stamps[path] = new FileStamp(hash, length, modified);
            records.Add((path, new Record(offset, size)));
        }
        return (stamps, records);
    }

    private readonly record struct Record(long Offset, int Length);

    /// <summary>The authored tree's stamp, or nothing when the archive predates it — in which case the
    /// caller re-derives once and records one.</summary>
    private static FileStamp ReadTreeStamp(BinaryReader reader, Dictionary<int, SectionSpan> sections,
                                           string?[] pool)
    {
        if (!Seek(reader, sections, SectionTreeStamp)) return default;

        var hash = Str(reader, pool) ?? string.Empty;
        return new FileStamp(hash, reader.ReadInt64(), new DateTime(reader.ReadInt64(), DateTimeKind.Utc));
    }

    private static FileContribution ReadContribution(BinaryReader reader, string?[] pool)
    {
        var c = new FileContribution { Hash = Str(reader, pool) ?? string.Empty };

        c.Nodes = ReadNodes(reader, pool);
        c.Edges = ReadEdges(reader, pool);

        for (var n = reader.ReadInt32(); n > 0; n--)
            c.Bases.Add(new CachedBase
            {
                TypeId      = Str(reader, pool) ?? string.Empty,
                Name        = Str(reader, pool) ?? string.Empty,
                IsInterface = reader.ReadBoolean(),
            });

        for (var n = reader.ReadInt32(); n > 0; n--)
            c.Refs.Add(new RawRef(Str(reader, pool) ?? string.Empty, (RawRefKind)reader.ReadInt32(),
                                  Str(reader, pool) ?? string.Empty, reader.ReadInt32()));

        for (var n = reader.ReadInt32(); n > 0; n--)
            c.Signatures.Add(new RawSignature(Str(reader, pool) ?? string.Empty, Str(reader, pool),
                                              ReadList(reader, pool), reader.ReadInt32()));

        for (var n = reader.ReadInt32(); n > 0; n--)
            c.Attributes.Add(new RawAttribute(Str(reader, pool) ?? string.Empty,
                                              Str(reader, pool) ?? string.Empty,
                                              reader.ReadInt32(), reader.ReadInt32()));

        for (var n = reader.ReadInt32(); n > 0; n--)
            c.Calls.Add(new RawCall(Str(reader, pool) ?? string.Empty, Str(reader, pool) ?? string.Empty,
                                    ReadList(reader, pool), reader.ReadInt32()));

        for (var n = reader.ReadInt32(); n > 0; n--)
            c.FileRefs.Add(new RawFileRef(Str(reader, pool) ?? string.Empty,
                                          Str(reader, pool) ?? string.Empty, reader.ReadInt32()));

        return c;
    }

    private static List<GraphNode> ReadNodes(BinaryReader reader, string?[] pool)
    {
        var count = reader.ReadInt32();
        var nodes = new List<GraphNode>(count);
        for (var i = 0; i < count; i++)
            nodes.Add(new GraphNode
            {
                Id         = Str(reader, pool) ?? string.Empty,
                Type       = Str(reader, pool) ?? string.Empty,
                Label      = Str(reader, pool) ?? string.Empty,
                FilePath   = Str(reader, pool),
                Language   = Str(reader, pool),
                Community  = ReadNullableInt(reader),
                Confidence = reader.ReadDouble(),
                Source     = Str(reader, pool),
                Metadata   = ReadMap(reader, pool),
            });
        return nodes;
    }

    private static List<GraphEdge> ReadEdges(BinaryReader reader, string?[] pool)
    {
        var count = reader.ReadInt32();
        var edges = new List<GraphEdge>(count);
        for (var i = 0; i < count; i++)
            edges.Add(new GraphEdge
            {
                Source         = Str(reader, pool) ?? string.Empty,
                Target         = Str(reader, pool) ?? string.Empty,
                Relationship   = Str(reader, pool) ?? string.Empty,
                Weight         = reader.ReadDouble(),
                Confidence     = reader.ReadDouble(),
                ProvenanceFile = Str(reader, pool),
                Metadata       = ReadMap(reader, pool),
            });
        return edges;
    }

    private static List<GraphHyperEdge> ReadHyperEdges(BinaryReader reader, string?[] pool)
    {
        var count = reader.ReadInt32();
        var hyper = new List<GraphHyperEdge>(count);
        for (var i = 0; i < count; i++)
        {
            var h = new GraphHyperEdge
            {
                Relationship   = Str(reader, pool) ?? string.Empty,
                Weight         = reader.ReadDouble(),
                Confidence     = reader.ReadDouble(),
                ProvenanceFile = Str(reader, pool),
                Metadata       = ReadMap(reader, pool),
            };
            for (var n = reader.ReadInt32(); n > 0; n--)
                h.Endpoints.Add(new HyperEndpoint
                {
                    Node       = Str(reader, pool) ?? string.Empty,
                    Role       = Str(reader, pool) ?? string.Empty,
                    Ordinal    = ReadNullableInt(reader),
                    Confidence = ReadNullableDouble(reader),
                });
            hyper.Add(h);
        }
        return hyper;
    }

    private static string? Str(BinaryReader reader, string?[] pool)
    {
        var id = reader.ReadInt32();
        return id < 0 || id >= pool.Length ? null : pool[id];
    }

    private static Dictionary<string, string>? ReadMap(BinaryReader reader, string?[] pool)
    {
        var count = reader.ReadInt32();
        if (count < 0) return null;

        var map = new Dictionary<string, string>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var key   = Str(reader, pool);
            var value = Str(reader, pool);
            if (key is not null) map[key] = value ?? string.Empty;
        }
        return map;
    }

    private static List<string> ReadList(BinaryReader reader, string?[] pool)
    {
        var count = reader.ReadInt32();
        var list  = new List<string>(count);
        for (var i = 0; i < count; i++) list.Add(Str(reader, pool) ?? string.Empty);
        return list;
    }

    private static int? ReadNullableInt(BinaryReader reader) =>
        reader.ReadBoolean() ? reader.ReadInt32() : null;

    private static double? ReadNullableDouble(BinaryReader reader) =>
        reader.ReadBoolean() ? reader.ReadDouble() : null;
}
