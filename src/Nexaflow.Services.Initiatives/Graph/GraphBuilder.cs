using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Nexaflow.IO.Common;
using Nexaflow.Services.Initiatives.Graph.Communities;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Syntax;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// Builds a <see cref="KnowledgeGraph"/> from a product's tree ⊕ its snaplinks ⊕ the code AST. This pass emits
/// the <b>structural</b>, extracted graph: product containment, snaplink links into code/docs, and file→type→
/// member containment + inheritance + imports (all confidence 1.0). Inferred cross-file edges (calls/references/
/// instantiates), hyperedges, and community detection are layered on by later passes.
/// </summary>
/// <remarks>
/// <see cref="Build"/> is a pure function of its inputs — it never reads the clock (the caller stamps
/// <see cref="GraphMetadata.GeneratedAt"/>), and it sorts its output, so identical inputs serialize identically.
/// Single-threaded, mirroring <c>SnaplinkValidator</c>.
/// </remarks>
public sealed class GraphBuilder
{
    private readonly ProductState _state;
    private readonly string _root;
    private readonly GraphBuildOptions _opts;

    private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Source, string Target, string Rel), GraphEdge> _edges = new();
    private readonly Dictionary<string, GraphHyperEdge> _hyperEdges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CodeOutline?> _outlineCache = new(StringComparer.Ordinal);
    private readonly CodeRelationshipExtractor _relExtractor = new();
    private readonly List<(string RelPath, RawRef Ref)> _rawRefs = [];
    private readonly List<(string RelPath, RawSignature Sig)> _signatures = [];
    private readonly List<(string RelPath, RawAttribute Attr)> _attributes = [];
    private readonly List<(string RelPath, RawCall Call)> _calls = [];
    private readonly List<(string RelPath, RawFileRef Ref)> _fileRefs = [];
    private readonly List<(string TypeId, string Name, bool IsInterface, string Rel)> _bases = [];

    private readonly GraphCache _cache;   // reused across builds (in) + repopulated (out); never null internally

    private GraphBuilder(ProductState state, string root, GraphBuildOptions opts, GraphCache? cache)
    {
        _state = state;
        _root = root;
        _opts = opts;
        // Reuse the cache only when incremental AND the extractor schema still matches; otherwise start clean
        // (a schema bump forces every file to be re-parsed). A null/mismatched cache means a full first scan.
        _cache = opts.Incremental && cache is { } c && c.SchemaVersion == GraphSchema.Version
            ? c
            : new GraphCache { SchemaVersion = GraphSchema.Version };
    }

    public static KnowledgeGraph Build(ProductState state, string productRoot, GraphBuildOptions options) =>
        BuildWithCache(state, productRoot, options, null).Graph;

    /// <summary>Builds the graph, reusing <paramref name="cache"/> for unchanged files and returning the updated
    /// cache to persist. The returned graph is byte-identical whether built fresh or from a warm cache.</summary>
    public static (KnowledgeGraph Graph, GraphCache Cache) BuildWithCache(
        ProductState state, string productRoot, GraphBuildOptions options, GraphCache? cache)
    {
        var builder = new GraphBuilder(state, productRoot, options, cache);
        var graph = builder.BuildInternal();
        return (graph, builder._cache);
    }

    private KnowledgeGraph BuildInternal()
    {
        BuildProductLayer();
        BuildSnaplinkLayer();
        BuildCodeLayer();
        BuildStructuredLayer();
        BuildAssetLayer();
        ResolveReferences();
        ResolveFileMentions();

        var graph = new KnowledgeGraph
        {
            Nodes = _nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToList(),
            Edges = _edges.Values
                          .OrderBy(e => e.Source, StringComparer.Ordinal)
                          .ThenBy(e => e.Relationship, StringComparer.Ordinal)
                          .ThenBy(e => e.Target, StringComparer.Ordinal)
                          .ToList(),
            HyperEdges = _hyperEdges.Values
                          .OrderBy(h => h.Relationship, StringComparer.Ordinal)
                          .ThenBy(h => h.Endpoints.Count > 0 ? h.Endpoints[0].Node : string.Empty, StringComparer.Ordinal)
                          .ThenBy(h => string.Join(",", h.Endpoints.Select(p => p.Node)), StringComparer.Ordinal)
                          .ToList(),
        };

        // Phase E — communities (Louvain + component split) → node.Community.
        var communities = CommunityDetector.Detect(graph);
        foreach (var node in graph.Nodes)
            if (communities.TryGetValue(node.Id, out var community)) node.Community = community;

        graph.Metadata.NodeCount = graph.Nodes.Count;
        graph.Metadata.CommunityCount = communities.Count > 0 ? communities.Values.Max() + 1 : 0;
        graph.Metadata.EdgeCount = graph.Edges.Count;
        graph.Metadata.HyperEdgeCount = graph.HyperEdges.Count;
        graph.Metadata.Scope = _opts.Scope == GraphScope.WholeRepo ? "whole_repo" : "product_anchored";
        graph.Metadata.ProductName = _state.Product?.Product;
        graph.Metadata.GeneratedAt = _opts.GeneratedAt;
        graph.Metadata.SchemaVersion = GraphSchema.Version;
        return graph;
    }

    // ── Phase A — product tree ─────────────────────────────────────────────────────────────────────
    private void BuildProductLayer()
    {
        foreach (var (id, node) in _state.Nodes)
        {
            var n = Node("product:" + id, NodeType.Product, string.IsNullOrEmpty(node.Title) ? id : node.Title, source: "product");
            Meta(n, "node_id", id);
            Meta(n, "status", ProductAggregator.EffectiveStatus(_state, id).ToString().ToLowerInvariant());
            if (!string.IsNullOrEmpty(node.Description)) Meta(n, "description", node.Description!);
        }

        foreach (var (id, node) in _state.Nodes)
            foreach (var child in node.Children)
                if (_state.Nodes.ContainsKey(child))
                    Edge("product:" + id, "product:" + child, EdgeRelationship.Contains, provenance: "product");

        // A single central start point: when the tree is a forest (several top-level nodes), add a synthetic
        // super-root that contains them, so the viewer has one node to centre on.
        var roots = ProductAggregator.Roots(_state).ToList();
        if (roots.Count > 1)
        {
            const string superId = "product:@root";
            var sn = Node(superId, NodeType.Product, _state.Product?.Product ?? "Product", source: "product");
            Meta(sn, "node_id", "@root");
            Meta(sn, "status", "should");
            foreach (var r in roots)
                Edge(superId, "product:" + r, EdgeRelationship.Contains, provenance: "product");
        }
    }

    // ── Phase B — snaplinks (product → code/doc/url) ─────────────────────────────────────────────────
    private void BuildSnaplinkLayer()
    {
        foreach (var (id, node) in _state.Nodes)
        {
            var productId = "product:" + id;
            if (node.Snaplinks is { } sl)
                foreach (var link in sl) LinkSnaplink(productId, link, concernTag: null);
            if (node.Concerns is { } cs)
                foreach (var c in cs)
                    if (c.Snaplinks is { } csl)
                        foreach (var link in csl) LinkSnaplink(productId, link, c.Tag);
        }
    }

    private void LinkSnaplink(string productId, Snaplink link, string? concernTag)
    {
        var rel = RelFor(link, concernTag);

        if (link.Type == "url")
        {
            if (string.IsNullOrWhiteSpace(link.Target)) return;
            var ext = "external:" + link.Target;
            Node(ext, NodeType.External, link.Target!, confidence: GraphConfidence.Speculative);
            Edge(productId, ext, EdgeRelationship.References, provenance: "product");
            return;
        }

        if (string.IsNullOrWhiteSpace(link.Doc)) return;
        var relPath = link.Doc!.Replace('\\', '/');
        var full = Path.Combine(_root, relPath.Replace('/', Path.DirectorySeparatorChar));
        Node("file:" + relPath, NodeType.File, Path.GetFileName(relPath), file: relPath, language: TreeSitterLanguages.ForFile(relPath), source: relPath);

        var target = "file:" + relPath;
        if (!string.IsNullOrEmpty(link.Class) || !string.IsNullOrEmpty(link.Method) || !string.IsNullOrEmpty(link.Ast))
            target = ResolveCodeNode(relPath, full, link) ?? target;

        Edge(productId, target, rel, provenance: "product");
    }

    private static string RelFor(Snaplink link, string? concernTag)
    {
        if (concernTag is not null)
        {
            var t = concernTag.ToLowerInvariant();
            return t switch
            {
                "tests" => EdgeRelationship.Tests,
                "docs" or "documentation" => EdgeRelationship.Documents,
                _ => t,   // raw concern tag as the relationship
            };
        }
        return link.Type == "markdown" ? EdgeRelationship.Documents : EdgeRelationship.References;
    }

    /// <summary>Resolves a code snaplink to the specific type/member node (creating it + its containment), or null.</summary>
    private string? ResolveCodeNode(string relPath, string fullPath, Snaplink link)
    {
        var outline = OutlineFor(relPath, fullPath);
        if (outline is null) return null;

        var (type, member) = FindTarget(outline, link);
        if (type is null) return null;

        var lang = TreeSitterLanguages.ForFile(relPath);
        var typeId = "code:" + relPath + "#" + type.AstPath;
        AddCodeNode(typeId, NodeType.Type, type.Name, relPath, lang, type.Kind.ToString(), type.Line, type.AstPath);
        Edge("file:" + relPath, typeId, EdgeRelationship.Contains, provenance: relPath);

        if (member is null) return typeId;

        var memberId = "code:" + relPath + "#" + member.AstPath;
        AddCodeNode(memberId, NodeType.Member, member.Name, relPath, lang, member.Kind.ToString(), member.Line, member.AstPath);
        Edge(typeId, memberId, EdgeRelationship.Contains, provenance: relPath);
        return memberId;
    }

    private static (OutlineType? Type, OutlineMember? Member) FindTarget(CodeOutline outline, Snaplink link)
    {
        if (!string.IsNullOrEmpty(link.Ast))
        {
            foreach (var t in outline.Types)
            {
                if (t.AstPath == link.Ast) return (t, null);
                var m = t.Members.FirstOrDefault(mm => mm.AstPath == link.Ast);
                if (m is not null) return (t, m);
            }
        }
        if (!string.IsNullOrEmpty(link.Class))
        {
            var type = outline.Types.FirstOrDefault(t => t.Name == link.Class);
            if (type is null) return (null, null);
            if (string.IsNullOrEmpty(link.Method)) return (type, null);
            return (type, type.Members.FirstOrDefault(mm => mm.Name == link.Method));
        }
        return (null, null);
    }

    // ── Phase C — code AST (whole repo): files, types, members, inheritance, imports ─────────────────
    // Incremental: each file's extraction is cached by content hash, so an unchanged file is never re-parsed. The
    // global inheritance resolution below (and Phase D) still run over every file's contribution, cached or fresh —
    // that's what lets a change in one file re-point an edge asserted by another.
    private void BuildCodeLayer()
    {
        if (_opts.Scope != GraphScope.WholeRepo) return;   // product-anchored scoping is a later pass

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var full in RepoFiles.EnumerateSource(_root, _opts.MaxFiles))
        {
            var rel = Path.GetRelativePath(_root, full).Replace('\\', '/');
            seen.Add(rel);
            ApplyContribution(rel, GetContribution(rel, full));
        }

        // Prune cache entries for files that no longer exist (deleted / renamed away).
        foreach (var stale in _cache.Files.Keys.Where(k => !seen.Contains(k)).ToList())
            _cache.Files.Remove(stale);

        // Resolve inheritance against the global type-name index: a unique match is a real edge, else external.
        var typesByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var n in _nodes.Values)
            if (n.Type == NodeType.Type)
            {
                if (!typesByName.TryGetValue(n.Label, out var list)) typesByName[n.Label] = list = [];
                list.Add(n.Id);
            }

        foreach (var (typeId, name, isInterface, rel) in _bases)
        {
            var relKind = isInterface ? EdgeRelationship.Implements : EdgeRelationship.Extends;
            if (typesByName.TryGetValue(name, out var ids) && ids.Count == 1)
                Edge(typeId, ids[0], relKind, provenance: rel);
            else
            {
                var ext = "external:" + name;
                Node(ext, NodeType.External, name, confidence: GraphConfidence.Speculative);
                Edge(typeId, ext, relKind, provenance: rel);
            }
        }
    }

    /// <summary>The file's contribution — reused from the cache on a content-hash hit, else freshly extracted (and
    /// cached). An unreadable/binary file yields just its file node (no parse, not cached — re-emitting it is free).</summary>
    private FileContribution GetContribution(string rel, string full)
    {
        var text = SnaplinkTargets.ReadText(full);
        if (text is null) return new FileContribution { Nodes = { BareFileNode(rel) } };

        var hash = Hashing.Md5(text);
        if (_cache.Files.TryGetValue(rel, out var cached) && cached.Hash == hash) return cached;

        var fresh = ExtractContribution(rel, full, text, hash);
        _cache.Files[rel] = fresh;
        return fresh;
    }

    private GraphNode BareFileNode(string rel) => new()
    {
        Id = "file:" + rel,
        Type = NodeType.File,
        Label = Path.GetFileName(rel),
        FilePath = rel,
        Language = TreeSitterLanguages.ForFile(rel),
        Source = rel,
    };

    /// <summary>Parses one file into an isolated, cacheable contribution: its file/type/member nodes + <c>contains</c>
    /// + resolved <c>imports</c>, plus the unresolved bases and raw relation sites the global passes consume. Builds
    /// into local dictionaries (never the shared graph) so the result can be serialized and replayed verbatim.</summary>
    private FileContribution ExtractContribution(string rel, string full, string text, string hash)
    {
        var lang = TreeSitterLanguages.ForFile(rel);
        var c = new FileContribution { Hash = hash };
        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var edges = new Dictionary<(string, string, string), GraphEdge>();

        GraphNode Local(string id, string type, string label, string? file, string? language)
        {
            if (nodes.TryGetValue(id, out var ex)) return ex;
            var n = new GraphNode { Id = id, Type = type, Label = label, FilePath = file, Language = language, Source = file };
            nodes[id] = n;
            return n;
        }
        void CodeNode(string id, string type, string label, string kind, int line, string ast)
        {
            var n = Local(id, type, label, rel, lang);
            (n.Metadata ??= new())["kind"] = kind.ToLowerInvariant();
            n.Metadata["line"] = line.ToString();
            n.Metadata["ast"] = ast;
        }
        void LocalEdge(string s, string t, string relationship)
        {
            if (s != t) edges.TryAdd((s, t, relationship), new GraphEdge { Source = s, Target = t, Relationship = relationship, ProvenanceFile = rel });
        }

        var fileId = "file:" + rel;
        Local(fileId, NodeType.File, Path.GetFileName(rel), rel, lang);

        var outline = OutlineFor(rel, full, text);
        if (outline is not null)
        {
            if (lang == "c-sharp")
            {
                var relations = _relExtractor.Extract(lang, text, Path.GetDirectoryName(full));
                c.Refs = [.. relations.Refs];
                c.Signatures = [.. relations.Signatures];
                c.Attributes = [.. relations.Attributes];
                c.Calls = [.. relations.Calls];
                c.FileRefs = [.. relations.FileRefs];
            }

            foreach (var imp in outline.Imports)
                if (!string.IsNullOrEmpty(imp.ResolvedPath) && ToRel(imp.ResolvedPath!) is { } impRel)
                {
                    var impId = "file:" + impRel;
                    Local(impId, NodeType.File, Path.GetFileName(impRel), impRel, TreeSitterLanguages.ForFile(impRel));
                    LocalEdge(fileId, impId, EdgeRelationship.Imports);
                }

            foreach (var t in outline.Types)
            {
                var typeId = "code:" + rel + "#" + t.AstPath;
                CodeNode(typeId, NodeType.Type, t.Name, t.Kind.ToString(), t.Line, t.AstPath);
                LocalEdge(fileId, typeId, EdgeRelationship.Contains);
                foreach (var b in t.Bases) c.Bases.Add(new CachedBase { TypeId = typeId, Name = b.Name, IsInterface = b.IsInterface });

                if (_opts.IncludeMembers)
                    foreach (var m in t.Members)
                    {
                        var memberId = "code:" + rel + "#" + m.AstPath;
                        CodeNode(memberId, NodeType.Member, m.Name, m.Kind.ToString(), m.Line, m.AstPath);
                        LocalEdge(typeId, memberId, EdgeRelationship.Contains);
                    }
            }
        }

        c.Nodes = [.. nodes.Values];
        c.Edges = [.. edges.Values];
        return c;
    }

    /// <summary>Merges a file's contribution into the global graph: nodes (dedup first-wins — a snaplink may already
    /// have created the same code node), structural edges (via the weight-merging <see cref="Edge"/> so cache copies
    /// stay pristine), and the unresolved bases + raw relation sites for the global resolution passes.</summary>
    private void ApplyContribution(string rel, FileContribution c)
    {
        foreach (var n in c.Nodes) _nodes.TryAdd(n.Id, n);
        foreach (var e in c.Edges) Edge(e.Source, e.Target, e.Relationship, e.Confidence, e.ProvenanceFile);
        foreach (var b in c.Bases) _bases.Add((b.TypeId, b.Name, b.IsInterface, rel));
        foreach (var r in c.Refs) _rawRefs.Add((rel, r));
        foreach (var s in c.Signatures) _signatures.Add((rel, s));
        foreach (var a in c.Attributes) _attributes.Add((rel, a));
        foreach (var cc in c.Calls) _calls.Add((rel, cc));
        foreach (var fr in c.FileRefs) _fileRefs.Add((rel, fr));
    }

    // ── Phase C.3 — bare nodes for every other repo file (docs / config / images / resources) ──
    // No structure to extract, but a node lets a `mentions` edge point here, and lets an agent find + locate an
    // asset it can't yet parse. Dedup first-wins keeps a richer code/structured node if one already exists.
    private void BuildAssetLayer()
    {
        if (_opts.Scope != GraphScope.WholeRepo) return;
        foreach (var full in RepoFiles.EnumerateOther(_root, _opts.MaxFiles))
        {
            var rel = Path.GetRelativePath(_root, full).Replace('\\', '/');
            Node("file:" + rel, NodeType.File, Path.GetFileName(rel), file: rel, source: rel);
        }
    }

    // ── Phase C.2 — structured project files (.xaml / .csproj / .slnx): explicit, high-certainty references ──
    // These aren't tree-sitter code, but their references are declared exactly (x:Class, ProjectReference, <Project
    // Path>), so we extract them with XDocument. Not cached (XML is cheap; there are few of these vs .cs).
    private void BuildStructuredLayer()
    {
        if (_opts.Scope != GraphScope.WholeRepo) return;

        foreach (var full in RepoFiles.EnumerateStructured(_root, _opts.MaxFiles))
        {
            var rel = Path.GetRelativePath(_root, full).Replace('\\', '/');
            var ext = Path.GetExtension(rel).ToLowerInvariant();
            var fileId = "file:" + rel;
            Node(fileId, NodeType.File, Path.GetFileName(rel), file: rel, language: ext.TrimStart('.'), source: rel);

            try
            {
                switch (ext)
                {
                    case ".xaml":   ExtractXaml(rel, fileId, XDocument.Load(full)); break;
                    case ".csproj": ExtractCsproj(rel, full, fileId, XDocument.Load(full)); break;
                    case ".slnx":   ExtractSolutionXml(rel, full, fileId, XDocument.Load(full)); break;
                    case ".sln":    ExtractSolutionLegacy(rel, full, fileId); break;
                }
            }
            catch { /* malformed file → keep just the file node */ }
        }
    }

    /// <summary>A WPF view's <c>x:Class</c> is the compiler-enforced pairing to its code-behind partial class — a
    /// rock-solid link from the .xaml to the C# type node (from which its ViewModel is a further hop via the code layer).</summary>
    private void ExtractXaml(string rel, string fileId, XDocument doc)
    {
        var root = doc.Root;
        if (root is null) return;
        Meta(_nodes[fileId], "root", root.Name.LocalName);   // UserControl / Window / ResourceDictionary / …

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var cls = root.Attribute(xaml + "Class")?.Value;
        if (string.IsNullOrWhiteSpace(cls)) return;

        var simple = cls[(cls.LastIndexOf('.') + 1)..];              // Ns.GraphView → GraphView
        var codeTypeId = "code:" + rel + ".cs#T:" + simple;         // GraphView.xaml → GraphView.xaml.cs#T:GraphView
        if (_nodes.ContainsKey(codeTypeId)) Edge(fileId, codeTypeId, EdgeRelationship.ViewOf, provenance: rel);
    }

    /// <summary>A project's <c>ProjectReference</c>/<c>PackageReference</c> — the build dependency graph.</summary>
    private void ExtractCsproj(string rel, string full, string fileId, XDocument doc)
    {
        var dir = Path.GetDirectoryName(full)!;
        foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            if (pr.Attribute("Include")?.Value is { Length: > 0 } inc
                && ToRel(Path.GetFullPath(Path.Combine(dir, inc.Replace('\\', Path.DirectorySeparatorChar)))) is { } target)
            {
                var tid = "file:" + target;
                Node(tid, NodeType.File, Path.GetFileName(target), file: target, language: "csproj", source: target);
                Edge(fileId, tid, EdgeRelationship.DependsOn, provenance: rel);
            }

        foreach (var pk in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
            if (pk.Attribute("Include")?.Value is { Length: > 0 } name)
            {
                var ext = "external:" + name;
                Node(ext, NodeType.External, name, confidence: GraphConfidence.Speculative);
                Edge(fileId, ext, EdgeRelationship.DependsOn, provenance: rel);
            }
    }

    /// <summary>A modern <c>.slnx</c> lists its member projects as <c>&lt;Project Path="..."/&gt;</c>.</summary>
    private void ExtractSolutionXml(string rel, string full, string fileId, XDocument doc)
    {
        var dir = Path.GetDirectoryName(full)!;
        foreach (var p in doc.Descendants().Where(e => e.Name.LocalName == "Project"))
            if (p.Attribute("Path")?.Value is { Length: > 0 } path)
                LinkSolutionProject(rel, dir, fileId, path);
    }

    /// <summary>The legacy <c>.sln</c> format isn't XML — pull member project paths from its <c>Project(...) =</c> lines.</summary>
    private void ExtractSolutionLegacy(string rel, string full, string fileId)
    {
        var dir = Path.GetDirectoryName(full)!;
        foreach (var line in File.ReadLines(full))
        {
            if (!line.StartsWith("Project(", StringComparison.Ordinal)) continue;
            var parts = line.Split(',');
            if (parts.Length >= 2 && parts[1].Trim().Trim('"') is { Length: > 0 } path
                && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                LinkSolutionProject(rel, dir, fileId, path);
        }
    }

    private void LinkSolutionProject(string rel, string solutionDir, string fileId, string projectPath)
    {
        if (ToRel(Path.GetFullPath(Path.Combine(solutionDir, projectPath.Replace('\\', Path.DirectorySeparatorChar)))) is not { } target) return;
        var tid = "file:" + target;
        Node(tid, NodeType.File, Path.GetFileName(target), file: target, language: "csproj", source: target);
        Edge(fileId, tid, EdgeRelationship.Contains, provenance: rel);
    }

    // ── Phase D — inferred edges: resolve call / instantiation / signature / attribute sites against the symbol set ──
    private void ResolveReferences()
    {
        if (_rawRefs.Count == 0 && _signatures.Count == 0 && _attributes.Count == 0 && _calls.Count == 0) return;

        var membersByName = new Dictionary<string, List<GraphNode>>(StringComparer.Ordinal);
        var typesByName = new Dictionary<string, List<GraphNode>>(StringComparer.Ordinal);
        foreach (var n in _nodes.Values)
        {
            if (n.Type == NodeType.Member) Index(membersByName, n.Label, n);
            else if (n.Type == NodeType.Type) Index(typesByName, n.Label, n);
        }

        foreach (var (relPath, r) in _rawRefs)
        {
            var sourceId = "code:" + relPath + "#" + r.FromAst;
            if (!_nodes.ContainsKey(sourceId)) continue;   // enclosing member isn't in the graph

            if (r.Kind == RawRefKind.Call)
            {
                // An unresolved call is a built-in / library method — skip it rather than guess or explode the graph.
                if (ResolveOne(membersByName, r.Name, relPath) is { } hit)
                    Edge(sourceId, hit.Node.Id, EdgeRelationship.Calls, hit.Confidence, provenance: relPath);
            }
            else if (ResolveOne(typesByName, r.Name, relPath) is { } hit)   // New → instantiates
            {
                Edge(sourceId, hit.Node.Id, EdgeRelationship.Instantiates, hit.Confidence, provenance: relPath);
            }
            else
            {
                var ext = "external:" + r.Name;
                Node(ext, NodeType.External, r.Name, confidence: GraphConfidence.Speculative);
                Edge(sourceId, ext, EdgeRelationship.Instantiates, GraphConfidence.Speculative, provenance: relPath);
            }
        }

        ResolveHyperEdges(membersByName, typesByName);
    }

    // ── Phase D.2 — n-ary hyperedges: signature (member→return,params), annotated (target→attr), calls (caller→callee,args) ──
    private void ResolveHyperEdges(
        Dictionary<string, List<GraphNode>> membersByName,
        Dictionary<string, List<GraphNode>> typesByName)
    {
        // signature: a member linked to the repo types in its return + parameters (external/primitive types skipped,
        // so the hyperedge only ever relates intra-repo dependencies, not `string`/`int`/`Task`).
        foreach (var (relPath, s) in _signatures)
        {
            var memberId = "code:" + relPath + "#" + s.MemberAst;
            if (!_nodes.ContainsKey(memberId)) continue;

            var eps = new List<HyperEndpoint> { new() { Node = memberId, Role = EndpointRole.Member } };
            var confidence = GraphConfidence.Extracted;

            if (s.ReturnType is { } rt && ResolveOne(typesByName, rt, relPath) is { } rh)
            {
                eps.Add(new HyperEndpoint { Node = rh.Node.Id, Role = EndpointRole.Return, Confidence = rh.Confidence });
                confidence = Math.Min(confidence, rh.Confidence);
            }
            for (var i = 0; i < s.ParamTypes.Count; i++)
                if (ResolveOne(typesByName, s.ParamTypes[i], relPath) is { } ph)
                {
                    eps.Add(new HyperEndpoint { Node = ph.Node.Id, Role = EndpointRole.Param, Ordinal = i, Confidence = ph.Confidence });
                    confidence = Math.Min(confidence, ph.Confidence);
                }

            if (eps.Count >= 2) HyperEdge(HyperRelationship.Signature, eps, confidence, relPath);
        }

        // annotated: a type/member linked to the attribute applied to it (C# drops the `Attribute` suffix, so try
        // both `Foo` and `FooAttribute`; an unresolved attribute is a library one → an external node).
        foreach (var (relPath, a) in _attributes)
        {
            var targetId = "code:" + relPath + "#" + a.TargetAst;
            if (!_nodes.ContainsKey(targetId)) continue;

            var hit = ResolveOne(typesByName, a.AttrName, relPath)
                      ?? ResolveOne(typesByName, a.AttrName + "Attribute", relPath);
            string attrId;
            double confidence;
            if (hit is { } h)
            {
                attrId = h.Node.Id;
                confidence = h.Confidence;
            }
            else
            {
                attrId = "external:" + a.AttrName;
                Node(attrId, NodeType.External, a.AttrName, confidence: GraphConfidence.Speculative);
                confidence = GraphConfidence.Speculative;
            }

            var eps = new List<HyperEndpoint>
            {
                new() { Node = targetId, Role = EndpointRole.Target },
                new() { Node = attrId, Role = EndpointRole.Attr },
            };
            var edge = HyperEdge(HyperRelationship.Annotated, eps, confidence, relPath);
            if (edge is not null && a.ArgCount > 0) (edge.Metadata ??= new())["arg_count"] = a.ArgCount.ToString();
        }

        // calls (enriched): a resolved call whose arguments create objects → caller, callee, and the arg types.
        foreach (var (relPath, c) in _calls)
        {
            var callerId = "code:" + relPath + "#" + c.FromAst;
            if (!_nodes.ContainsKey(callerId)) continue;
            if (ResolveOne(membersByName, c.Callee, relPath) is not { } callee) continue;

            var eps = new List<HyperEndpoint>
            {
                new() { Node = callerId, Role = EndpointRole.Caller },
                new() { Node = callee.Node.Id, Role = EndpointRole.Callee, Confidence = callee.Confidence },
            };
            for (var i = 0; i < c.NewArgTypes.Count; i++)
                if (ResolveOne(typesByName, c.NewArgTypes[i], relPath) is { } ah)
                    eps.Add(new HyperEndpoint { Node = ah.Node.Id, Role = EndpointRole.Arg, Ordinal = i, Confidence = ah.Confidence });

            if (eps.Count >= 3) HyperEdge(HyperRelationship.Calls, eps, callee.Confidence, relPath);   // ≥1 resolved arg
        }
    }

    // ── Phase D.3 — file mentions: resolve file-like string literals to the repo file they name (a low-confidence hint) ──
    // Uses the same "unique match or skip" discipline as the symbol resolver, so an ambiguous name (README.md, many)
    // is dropped, a distinctive one (default-filemap.json, one) links. Gives an otherwise-parseless asset a starting place.
    private void ResolveFileMentions()
    {
        if (_fileRefs.Count == 0) return;

        var byRel = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, List<GraphNode>>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in _nodes.Values)
            if (n.Type == NodeType.File && n.FilePath is { Length: > 0 } fp)
            {
                byRel[fp] = n;
                Index(byName, Path.GetFileName(fp), n);
            }

        foreach (var (relPath, r) in _fileRefs)
        {
            var sourceId = "code:" + relPath + "#" + r.FromAst;
            if (!_nodes.ContainsKey(sourceId)) continue;

            var hit = ResolveFile(r.Token, byRel, byName);
            if (hit is null || hit.Value.Node.Id == "file:" + relPath) continue;   // unresolved / ambiguous / self
            Edge(sourceId, hit.Value.Node.Id, EdgeRelationship.Mentions, hit.Value.Confidence, provenance: relPath);
        }
    }

    /// <summary>Resolve a file-like literal to a single repo file: an exact/unique-suffix relpath match is strong; a
    /// unique bare filename is weak; anything ambiguous (several files share the name) is dropped, never guessed.</summary>
    private static (GraphNode Node, double Confidence)? ResolveFile(
        string token, Dictionary<string, GraphNode> byRel, Dictionary<string, List<GraphNode>> byName)
    {
        if (byRel.TryGetValue(token, out var exact)) return (exact, GraphConfidence.Strong);

        if (token.Contains('/'))   // a path fragment → match it as a unique tail of some relpath
        {
            var suffix = byRel.Where(kv => kv.Key.EndsWith("/" + token, StringComparison.OrdinalIgnoreCase))
                              .Select(kv => kv.Value).Take(2).ToList();
            if (suffix.Count == 1) return (suffix[0], GraphConfidence.Strong);
            if (suffix.Count > 1) return null;   // ambiguous path fragment
        }

        var name = token.Contains('/') ? token[(token.LastIndexOf('/') + 1)..] : token;
        if (byName.TryGetValue(name, out var named) && named.Count == 1) return (named[0], GraphConfidence.Weak);
        return null;   // no match, or an ambiguous bare filename
    }

    private static void Index(Dictionary<string, List<GraphNode>> map, string name, GraphNode node)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!map.TryGetValue(name, out var list)) map[name] = list = [];
        list.Add(node);
    }

    /// <summary>Resolve a bare name to a single declaration, scoring it by the confidence ladder — or null when
    /// it's ambiguous (several same-name declarations, none uniquely in this file), so we never emit a wrong edge.</summary>
    private static (GraphNode Node, double Confidence)? ResolveOne(
        Dictionary<string, List<GraphNode>> index, string name, string relPath)
    {
        if (!index.TryGetValue(name, out var c) || c.Count == 0) return null;
        if (c.Count == 1)
            return (c[0], c[0].FilePath == relPath ? GraphConfidence.NearCertain : GraphConfidence.Strong);
        var sameFile = c.Where(x => x.FilePath == relPath).ToList();
        return sameFile.Count == 1 ? (sameFile[0], GraphConfidence.Reasonable) : null;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────
    private CodeOutline? OutlineFor(string relPath, string fullPath)
    {
        if (_outlineCache.TryGetValue(relPath, out var cached)) return cached;
        var text = SnaplinkTargets.ReadText(fullPath);
        return OutlineFor(relPath, fullPath, text);
    }

    /// <summary>Outline overload for when the caller already has the file text (the code layer reads it once for both
    /// the outline and the relation extractor + the content hash).</summary>
    private CodeOutline? OutlineFor(string relPath, string fullPath, string? text)
    {
        if (_outlineCache.TryGetValue(relPath, out var cached)) return cached;
        var outline = text is null ? null : SnaplinkTargets.Outline(fullPath, text, Path.GetDirectoryName(fullPath));
        _outlineCache[relPath] = outline;
        return outline;
    }

    private void AddCodeNode(string id, string type, string label, string relPath, string? lang, string kind, int line, string ast)
    {
        var n = Node(id, type, label, file: relPath, language: lang, source: relPath);
        Meta(n, "kind", kind.ToLowerInvariant());
        Meta(n, "line", line.ToString());
        Meta(n, "ast", ast);
    }

    private GraphNode Node(string id, string type, string label, string? file = null, string? language = null,
                           string? source = null, double confidence = GraphConfidence.Extracted)
    {
        if (_nodes.TryGetValue(id, out var existing)) return existing;
        var n = new GraphNode
        {
            Id = id,
            Type = type,
            Label = label,
            FilePath = file,
            Language = language,
            Source = source,
            Confidence = confidence,
        };
        _nodes[id] = n;
        return n;
    }

    private static void Meta(GraphNode n, string key, string value) => (n.Metadata ??= new()).TryAdd(key, value);

    private void Edge(string source, string target, string relationship,
                      double confidence = GraphConfidence.Extracted, string? provenance = null)
    {
        if (source == target) return;
        var key = (source, target, relationship);
        if (_edges.TryGetValue(key, out var e)) { e.Weight += 1; return; }
        _edges[key] = new GraphEdge
        {
            Source = source,
            Target = target,
            Relationship = relationship,
            Confidence = confidence,
            ProvenanceFile = provenance,
        };
    }

    /// <summary>Adds (or dedups by relationship + ordered endpoints) an n-ary hyperedge. A repeat bumps
    /// <see cref="GraphHyperEdge.Weight"/> and returns the existing instance (null only for a degenerate &lt;2-endpoint set).</summary>
    private GraphHyperEdge? HyperEdge(string relationship, List<HyperEndpoint> endpoints, double confidence, string? provenance)
    {
        if (endpoints.Count < 2) return null;
        var key = relationship + "|" + string.Join("|", endpoints.Select(p => p.Role + ":" + p.Node));
        if (_hyperEdges.TryGetValue(key, out var existing)) { existing.Weight += 1; return existing; }
        var h = new GraphHyperEdge
        {
            Relationship = relationship,
            Endpoints = endpoints,
            Confidence = confidence,
            ProvenanceFile = provenance,
        };
        _hyperEdges[key] = h;
        return h;
    }

    /// <summary>An absolute path under the root → repo-relative, or null when it escapes the root.</summary>
    private string? ToRel(string fullPath)
    {
        try
        {
            if (!Path.IsPathRooted(fullPath)) return null;
            var rel = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
            return rel.StartsWith("../", StringComparison.Ordinal) ? null : rel;
        }
        catch { return null; }
    }
}
