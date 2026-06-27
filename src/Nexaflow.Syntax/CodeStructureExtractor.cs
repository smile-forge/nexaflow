using System.IO;
using System.Text.RegularExpressions;
using TreeSitter;

namespace Nexaflow.Syntax;

/// <summary>
/// Extracts a structural <see cref="CodeOutline"/> (imports + types + members, with line numbers) from a
/// source file by walking the tree-sitter parse tree. Used to build the "As Code" intelligence panel.
///
/// Each type/member carries an <b>AST path</b> — a slash-separated, name-based identifier like
/// <c>T:Outer/M:DoThing</c> (overloads disambiguated with <c>#0</c>/<c>#1</c>). Because it is keyed on names
/// and nesting rather than line numbers, a link to it survives edits (added lines, reformatting, body changes)
/// and only breaks when the structure itself changes (rename/move). <see cref="ResolveLine"/> re-extracts the
/// current text and returns the live line for a path, so navigation always lands on the right line.
///
/// Walking uses <see cref="CodeHighlighter.WithParseTree{T}"/>; tree-sitter nodes are only touched inside that
/// callback. One-thread use (the editor UI thread), same as the highlighter.
/// </summary>
public sealed class CodeStructureExtractor
{
    // Embedded outlines recurse (ERB → HTML → <script>); bound the depth so a pathological file can't spin.
    private const int MaxEmbedDepth = 3;

    public CodeOutline Extract(string grammarId, string text, string? baseDir = null)
        => Extract(grammarId, text, baseDir, 0);

    private CodeOutline Extract(string grammarId, string text, string? baseDir, int depth)
    {
        if (string.IsNullOrEmpty(grammarId) || string.IsNullOrEmpty(text)) return CodeOutline.Empty;
        using var hl = CodeHighlighter.TryCreate(grammarId);
        if (hl is null) return CodeOutline.Empty;

        // One parse yields both the host outline and the embedded-language ranges (plain data; nodes don't escape).
        (CodeOutline Outline, IReadOnlyList<InjectionRange> Injections)? res;
        try
        {
            res = hl.WithParseTree(text, root =>
                (Build(grammarId, root, baseDir),
                 LanguageInjections.HasInjections(grammarId)
                     ? LanguageInjections.Find(grammarId, root, text)
                     : (IReadOnlyList<InjectionRange>)[]));
        }
        catch { return CodeOutline.Empty; }   // never throw — a malformed parse just yields no outline
        if (res is not { } r) return CodeOutline.Empty;

        var host = r.Outline;
        if (depth >= MaxEmbedDepth || r.Injections.Count == 0) return host;

        var embeds = new List<EmbeddedOutline>();
        int i = 0;
        foreach (var inj in r.Injections)
        {
            if (inj.TargetGrammarId == grammarId) continue;                          // self-injection guard
            if (inj.Start < 0 || inj.Length <= 0 || inj.Start + inj.Length > text.Length) continue;
            var sub = text.Substring(inj.Start, inj.Length);
            if (string.IsNullOrWhiteSpace(sub)) continue;

            var child = Extract(inj.TargetGrammarId, sub, baseDir, depth + 1);
            if (!child.HasContent) continue;                                         // nothing structural to show

            // Map the child's lines to absolute document lines and namespace its AST paths so a member link
            // stays unique within the host and re-resolves through ResolveLine.
            var reprojected = Reproject(child, $"E{i}:{inj.TargetGrammarId}", inj.StartRow);
            embeds.Add(new EmbeddedOutline(inj.TargetGrammarId, $"{inj.TargetGrammarId} @ line {inj.StartRow + 1}",
                inj.StartRow + 1, reprojected));
            i++;
        }

        return embeds.Count == 0 ? host : host with { Embedded = embeds };
    }

    /// <summary>The current 1-based line of the element with <paramref name="astPath"/>, or null if it no
    /// longer exists (renamed / removed). Resolved against the live <paramref name="text"/>. Searches embedded
    /// regions too, so links into an embedded language navigate correctly.</summary>
    public int? ResolveLine(string grammarId, string text, string astPath, string? baseDir = null)
    {
        if (string.IsNullOrEmpty(astPath)) return null;
        return Search(Extract(grammarId, text, baseDir), astPath);
    }

    private static int? Search(CodeOutline o, string astPath)
    {
        foreach (var t in o.Types)
        {
            if (t.AstPath == astPath) return t.Line;
            foreach (var m in t.Members)
                if (m.AstPath == astPath) return m.Line;
        }
        foreach (var m in o.TopLevel)
            if (m.AstPath == astPath) return m.Line;
        foreach (var e in o.Embedded)
            if (Search(e.Outline, astPath) is { } line) return line;
        return null;
    }

    /// <summary>Returns a copy of <paramref name="o"/> with every line shifted by <paramref name="lineOffset"/>
    /// and every AST path prefixed by <paramref name="prefix"/> — used to splice an embedded outline into its
    /// host's coordinate space.</summary>
    private static CodeOutline Reproject(CodeOutline o, string prefix, int lineOffset)
    {
        var types = o.Types.Select(t => new OutlineType(
            t.Name, t.Line + lineOffset, t.Kind, $"{prefix}/{t.AstPath}",
            t.Members.Select(m => Shift(m, prefix, lineOffset)).ToList()) { Bases = t.Bases }).ToList();
        var top = o.TopLevel.Select(m => Shift(m, prefix, lineOffset)).ToList();
        var embeds = o.Embedded.Select(e => new EmbeddedOutline(
            e.Language, e.HostLabel, e.HostLine + lineOffset, Reproject(e.Outline, prefix, lineOffset))).ToList();
        return new CodeOutline(o.Imports, types, top) { Embedded = embeds };
    }

    private static OutlineMember Shift(OutlineMember m, string prefix, int lineOffset) =>
        new(m.Name, m.Line + lineOffset, m.Kind, m.Signature, $"{prefix}/{m.AstPath}") { Visibility = m.Visibility };

    private static CodeOutline Build(string grammar, Node root, string? baseDir) => grammar switch
    {
        "c-sharp"    => BuildCSharp(root, baseDir),
        "javascript" => BuildJsTs(root, baseDir, ts: false),
        "typescript" => BuildJsTs(root, baseDir, ts: true),
        "python"     => BuildPython(root, baseDir),
        "ruby"       => BuildRuby(root, baseDir),
        "java"       => BuildJava(root, baseDir),
        "php"        => BuildPhp(root, baseDir),
        "cpp"        => BuildCpp(root, baseDir),
        "rust"       => BuildRust(root, baseDir),
        "razor"      => BuildRazor(root, baseDir),
        _            => CodeOutline.Empty,
    };

    // ── Shared helpers ─────────────────────────────────────────────────────────

    private static int Line(Node n) => n.StartPosition.Row + 1;

    private static string? NameOf(Node n)
    {
        if (n.GetChildForField("name") is { } f) return f.Text;
        foreach (var c in n.NamedChildren)
            if (c.Type.Contains("identifier") || c.Type is "constant") return c.Text;
        return null;
    }

    /// <summary>The bare type name without generic parameters / parens (so the AST path and the diagram box
    /// agree): <c>List&lt;T&gt;</c> / <c>List~T~</c> → <c>List</c>.</summary>
    private static string Simple(string name)
    {
        int cut = name.Length;
        foreach (var ch in stackalloc[] { '<', '~', '(', ' ' })
        {
            int i = name.IndexOf(ch);
            if (i >= 0 && i < cut) cut = i;
        }
        return name[..cut].Trim();
    }

    private static string OneLine(string text)
    {
        var first = text.Replace("\r", "").Split('\n', 2)[0].Trim();
        return first.Length < text.Trim().Length ? first + " …" : first;
    }

    private static IEnumerable<Node> Descendants(Node n)
    {
        foreach (var c in n.NamedChildren)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    /// <summary>The first named child (optionally of one of <paramref name="types"/>), or null. Avoids
    /// <c>FirstOrDefault</c>, whose default <see cref="Node"/> would be unsafe to dereference.</summary>
    private static Node? FirstChild(Node n, params string[] types)
    {
        foreach (var c in n.NamedChildren)
            if (types.Length == 0 || Array.IndexOf(types, c.Type) >= 0) return c;
        return null;
    }

    /// <summary>The text of a named field child, or null if the node has no such field.</summary>
    private static string? Field(Node n, string field) => n.GetChildForField(field) is { } f ? f.Text : null;

    private static string KindInitial(OutlineKind k) => k switch
    {
        OutlineKind.Class or OutlineKind.Struct or OutlineKind.Interface or OutlineKind.Enum => "T",
        OutlineKind.Method or OutlineKind.Constructor => "M",
        OutlineKind.Property => "P",
        _ => "F",
    };

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Collapses runs of whitespace (including newlines from a multi-line signature) to single spaces.</summary>
    private static string Compact(string s) => Whitespace.Replace(s, " ").Trim();

    /// <summary>The display signature for a callable: <c>name(params) returnType</c> (return type after the
    /// closing paren, space-separated — the Mermaid class parser reflows it to <c>name(params) : returnType</c>).
    /// A blank/void return is omitted.</summary>
    private static string CallableSig(string name, string? paramsText, string? returns)
    {
        var sig = name + (string.IsNullOrWhiteSpace(paramsText) ? "()" : Compact(paramsText));
        var ret = string.IsNullOrWhiteSpace(returns) ? "" : Compact(returns);
        return ret is "" or "void" ? sig : $"{sig} {ret}";
    }

    /// <summary>The display signature for an attribute: <c>name : type</c>, or just the name when the type is unknown.</summary>
    private static string FieldSig(string name, string? type)
    {
        var t = string.IsNullOrWhiteSpace(type) ? "" : Compact(type);
        return t.Length == 0 ? name : $"{name} : {t}";
    }

    /// <summary>One raw member before AST-path finalisation.</summary>
    private readonly record struct RawMember(string Name, int Line, OutlineKind Kind, OutlineVisibility Vis, string Signature);

    private static void AddMember(List<RawMember> raw, string? name, int line, OutlineKind kind,
        OutlineVisibility vis = OutlineVisibility.Public, string? signature = null)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return;
        var sig = string.IsNullOrWhiteSpace(signature)
            ? (kind is OutlineKind.Method or OutlineKind.Constructor ? $"{n}()" : n)
            : Compact(signature);
        raw.Add(new RawMember(n, line, kind, vis, sig));
    }

    /// <summary>Turns raw members into <see cref="OutlineMember"/>s with stable AST paths, appending
    /// <c>#index</c> to every member that shares a name+kind with a sibling (so overloads stay distinct).
    /// The AST path stays name-based (not signature-based) so a member link survives parameter edits.</summary>
    private static IReadOnlyList<OutlineMember> Finalize(List<RawMember> raw, string parentPath)
    {
        var segs = new string[raw.Count];
        var total = new Dictionary<string, int>();
        for (int i = 0; i < raw.Count; i++)
        {
            segs[i] = $"{KindInitial(raw[i].Kind)}:{raw[i].Name}";
            total[segs[i]] = total.GetValueOrDefault(segs[i]) + 1;
        }

        var seen = new Dictionary<string, int>();
        var result = new List<OutlineMember>(raw.Count);
        for (int i = 0; i < raw.Count; i++)
        {
            var seg = segs[i];
            int idx = seen.GetValueOrDefault(seg);
            seen[seg] = idx + 1;
            var fseg = total[seg] > 1 ? $"{seg}#{idx}" : seg;
            var path = parentPath.Length > 0 ? $"{parentPath}/{fseg}" : fseg;
            result.Add(new OutlineMember(raw[i].Name, raw[i].Line, raw[i].Kind, raw[i].Signature, path)
            {
                Visibility = raw[i].Vis,
            });
        }
        return result;
    }

    // ── C# ───────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> CsTypeDecls =
        ["class_declaration", "struct_declaration", "interface_declaration", "enum_declaration", "record_declaration", "record_struct_declaration"];

    private static OutlineKind CsTypeKind(string t) => t switch
    {
        "struct_declaration" or "record_struct_declaration" => OutlineKind.Struct,
        "interface_declaration"                             => OutlineKind.Interface,
        "enum_declaration"                                  => OutlineKind.Enum,
        _                                                   => OutlineKind.Class,
    };

    private static CodeOutline BuildCSharp(Node root, string? baseDir)
    {
        var imports = new List<ImportRef>();
        foreach (var n in Descendants(root))
            if (n.Type == "using_directive")
                imports.Add(new ImportRef(OneLine(n.Text), null));   // namespaces, not files

        var types = new List<OutlineType>();
        ScanCsTypes(root, "", types);
        return new CodeOutline(imports, types, []);
    }

    private static void ScanCsTypes(Node container, string parentPath, List<OutlineType> outTypes)
    {
        foreach (var child in container.NamedChildren)
        {
            if (CsTypeDecls.Contains(child.Type))
            {
                var name = Simple(NameOf(child) ?? "");
                if (name.Length == 0) continue;
                var kind = CsTypeKind(child.Type);
                var path = parentPath.Length > 0 ? $"{parentPath}/T:{name}" : $"T:{name}";
                var body = child.GetChildForField("body");
                var members = body is { } b ? CsMembers(b, path, kind) : [];
                outTypes.Add(new OutlineType(name, Line(child), kind, path, members) { Bases = CsBases(child) });
                if (body is { } b2) ScanCsTypes(b2, path, outTypes);   // nested types
            }
            else if (child.Type is "namespace_declaration" or "file_scoped_namespace_declaration" or "declaration_list")
            {
                ScanCsTypes(child, parentPath, outTypes);              // descend transparently
            }
        }
    }

    /// <summary>The parent class + implemented interfaces from a type's <c>base_list</c>. Interface vs class is
    /// inferred by the <c>I</c>-prefix convention (no semantic info in syntax), which the diagram only uses to
    /// pick a dashed vs solid arrow.</summary>
    private static IReadOnlyList<BaseRef> CsBases(Node typeDecl)
    {
        var baseList = FirstChild(typeDecl, "base_list");
        if (baseList is null) return [];
        var bases = new List<BaseRef>();
        foreach (var b in baseList.NamedChildren)
        {
            var n = Simple(b.Text);
            if (n.Length > 0) bases.Add(new BaseRef(n, LooksLikeInterface(n)));
        }
        return bases;
    }

    /// <summary>C# convention: an interface is <c>I</c> followed by an upper-case letter (e.g. <c>IDisposable</c>).</summary>
    private static bool LooksLikeInterface(string name) =>
        name.Length >= 2 && name[0] == 'I' && char.IsUpper(name[1]);

    /// <summary>Maps a declaration's explicit access modifiers to a UML visibility, falling back to
    /// <paramref name="dflt"/> (C# members are private by default, except in an interface).</summary>
    private static OutlineVisibility CsVisibility(Node decl, OutlineVisibility dflt)
    {
        bool pub = false, prot = false, priv = false, intern = false;
        foreach (var c in decl.NamedChildren)
            if (c.Type == "modifier")
                switch (c.Text)
                {
                    case "public":    pub = true;    break;
                    case "protected": prot = true;   break;
                    case "private":   priv = true;   break;
                    case "internal":  intern = true; break;
                }
        if (pub) return OutlineVisibility.Public;
        if (prot) return OutlineVisibility.Protected;     // protected / protected internal
        if (priv) return OutlineVisibility.Private;        // private / private protected
        if (intern) return OutlineVisibility.Internal;
        return dflt;
    }

    private static IReadOnlyList<OutlineMember> CsMembers(Node body, string typePath, OutlineKind typeKind)
    {
        var dflt = typeKind == OutlineKind.Interface ? OutlineVisibility.Public : OutlineVisibility.Private;
        var raw = new List<RawMember>();
        CollectCsMembers(body, dflt, raw);
        return Finalize(raw, typePath);
    }

    /// <summary>Collects C# member declarations from a body into <paramref name="raw"/> (shared by class bodies
    /// and the Razor <c>@code</c> block, whose members are C#-shaped nodes directly under the block).</summary>
    private static void CollectCsMembers(Node body, OutlineVisibility dflt, List<RawMember> raw)
    {
        foreach (var m in body.NamedChildren)
        {
            switch (m.Type)
            {
                case "method_declaration":
                case "local_function_statement":
                    AddMember(raw, NameOf(m), Line(m), OutlineKind.Method, CsVisibility(m, dflt),
                        CallableSig(NameOf(m) ?? "", Field(m, "parameters"), Field(m, "returns") ?? Field(m, "type")));
                    break;
                case "constructor_declaration":
                    AddMember(raw, NameOf(m), Line(m), OutlineKind.Constructor, CsVisibility(m, dflt),
                        CallableSig(NameOf(m) ?? "", Field(m, "parameters"), null));
                    break;
                case "property_declaration":
                    AddMember(raw, NameOf(m), Line(m), OutlineKind.Property, CsVisibility(m, dflt),
                        FieldSig(NameOf(m) ?? "", Field(m, "type")));
                    break;
                case "indexer_declaration":
                case "event_declaration":
                    AddMember(raw, NameOf(m), Line(m), OutlineKind.Property, CsVisibility(m, dflt),
                        FieldSig(NameOf(m) ?? "", Field(m, "type")));
                    break;
                case "field_declaration":
                case "event_field_declaration":
                {
                    var vis = CsVisibility(m, dflt);
                    var type = FirstChild(m, "variable_declaration") is { } vdcl ? Field(vdcl, "type") : null;
                    foreach (var vd in Descendants(m))
                        if (vd.Type == "variable_declarator")
                            AddMember(raw, NameOf(vd), Line(vd), OutlineKind.Field, vis,
                                FieldSig(NameOf(vd) ?? "", type));
                    break;
                }
                case "enum_member_declaration":
                    AddMember(raw, NameOf(m), Line(m), OutlineKind.Field); break;
            }
        }
    }

    // ── Razor (.razor / .cshtml) ───────────────────────────────────────────────
    // The razor grammar parses C# directly: @code/@functions become `razor_block`s whose children are the
    // same C# member nodes a class body has. We surface those as a synthetic "Code" type (the component's
    // members) and pull out any real nested C# types declared in the block.

    private static CodeOutline BuildRazor(Node root, string? baseDir)
    {
        var imports = new List<ImportRef>();
        foreach (var n in Descendants(root))
            if (n.Type == "using_directive")
                imports.Add(new ImportRef(OneLine(n.Text), null));   // @using directives

        var types = new List<OutlineType>();
        var codeRaw = new List<RawMember>();
        int codeLine = 0;
        foreach (var block in Descendants(root))
            if (block.Type == "razor_block")
            {
                if (codeLine == 0) codeLine = Line(block);
                ScanCsTypes(block, "", types);                              // nested C# types in @code
                CollectCsMembers(block, OutlineVisibility.Private, codeRaw); // component members (C# default: private)
            }

        if (codeRaw.Count > 0)
            types.Insert(0, new OutlineType("Code", codeLine == 0 ? 1 : codeLine, OutlineKind.Class, "T:Code",
                Finalize(codeRaw, "T:Code")));

        return new CodeOutline(imports, types, []);
    }

    // ── JavaScript / TypeScript ────────────────────────────────────────────────

    private static readonly HashSet<string> JsTransparent =
        ["program", "export_statement", "statement_block", "module", "internal_module", "ambient_declaration"];

    private static CodeOutline BuildJsTs(Node root, string? baseDir, bool ts)
    {
        var imports = new List<ImportRef>();
        foreach (var n in Descendants(root))
            if (n.Type == "import_statement")
            {
                var spec = StripQuotes(n.GetChildForField("source") is { } s ? s.Text : "");
                imports.Add(new ImportRef(OneLine(n.Text), ResolveJs(spec, baseDir)));
            }

        var types = new List<OutlineType>();
        var topRaw = new List<RawMember>();
        ScanJsTypes(root, "", types, topRaw);
        return new CodeOutline(imports, types, Finalize(topRaw, ""));
    }

    private static void ScanJsTypes(Node container, string parentPath, List<OutlineType> outTypes,
        List<RawMember> topFuncs)
    {
        foreach (var child in container.NamedChildren)
        {
            switch (child.Type)
            {
                case "class_declaration":
                case "abstract_class_declaration":
                case "interface_declaration":
                case "enum_declaration":
                case "type_alias_declaration":
                {
                    var name = Simple(NameOf(child) ?? "");
                    if (name.Length == 0) break;
                    var path = parentPath.Length > 0 ? $"{parentPath}/T:{name}" : $"T:{name}";
                    var body = child.GetChildForField("body");
                    var members = body is { } b ? JsMembers(b, path) : [];
                    var kind = child.Type is "interface_declaration" or "type_alias_declaration"
                        ? OutlineKind.Interface
                        : child.Type == "enum_declaration" ? OutlineKind.Enum : OutlineKind.Class;
                    outTypes.Add(new OutlineType(name, Line(child), kind, path, members) { Bases = JsBases(child) });
                    if (body is { } b2) ScanJsTypes(b2, path, outTypes, topFuncs);
                    break;
                }
                case "function_declaration":
                case "generator_function_declaration":
                    if (parentPath.Length == 0)
                        AddMember(topFuncs, NameOf(child), Line(child), OutlineKind.Method,
                            OutlineVisibility.Public,
                            CallableSig(NameOf(child) ?? "", Field(child, "parameters"), StripAnno(Field(child, "return_type"))));
                    break;
                default:
                    if (JsTransparent.Contains(child.Type))
                        ScanJsTypes(child, parentPath, outTypes, topFuncs);
                    break;
            }
        }
    }

    /// <summary>The <c>extends</c> superclass(es) and <c>implements</c> interfaces from a class's heritage clause.
    /// Plain JS lists the superclass directly under <c>class_heritage</c>; TS nests <c>extends_clause</c> /
    /// <c>implements_clause</c> — both are handled.</summary>
    private static IReadOnlyList<BaseRef> JsBases(Node classNode)
    {
        var heritage = FirstChild(classNode, "class_heritage");
        if (heritage is null) return [];
        var bases = new List<BaseRef>();

        void AddBase(Node t, bool iface)
        {
            var n = Simple(t.Text);
            if (n.Length > 0) bases.Add(new BaseRef(n, iface));
        }

        foreach (var clause in heritage.NamedChildren)
        {
            if (clause.Type is "extends_clause" or "implements_clause")
            {
                bool iface = clause.Type == "implements_clause";
                foreach (var t in clause.NamedChildren) AddBase(t, iface);
            }
            else AddBase(clause, iface: false);   // JS: the superclass expression sits directly under class_heritage
        }
        return bases;
    }

    private static IReadOnlyList<OutlineMember> JsMembers(Node body, string typePath)
    {
        var raw = new List<RawMember>();
        foreach (var m in body.NamedChildren)
        {
            switch (m.Type)
            {
                case "method_definition":
                case "method_signature":
                {
                    var name = NameOf(m);
                    var kind = name == "constructor" ? OutlineKind.Constructor : OutlineKind.Method;
                    var sig = kind == OutlineKind.Constructor
                        ? CallableSig(name ?? "", Field(m, "parameters"), null)
                        : CallableSig(name ?? "", Field(m, "parameters"), StripAnno(Field(m, "return_type")));
                    AddMember(raw, name, Line(m), kind, JsVisibility(m, name), sig);
                    break;
                }
                case "public_field_definition":
                case "field_definition":
                case "property_signature":
                    AddMember(raw, NameOf(m), Line(m), OutlineKind.Property, JsVisibility(m, NameOf(m)),
                        FieldSig(NameOf(m) ?? "", StripAnno(Field(m, "type"))));
                    break;
            }
        }
        return Finalize(raw, typePath);
    }

    /// <summary>TS <c>accessibility_modifier</c> (public/private/protected), else the JS <c>#private</c> naming
    /// convention, else public.</summary>
    private static OutlineVisibility JsVisibility(Node m, string? name)
    {
        foreach (var c in m.NamedChildren)
            if (c.Type == "accessibility_modifier")
                return c.Text switch
                {
                    "private"   => OutlineVisibility.Private,
                    "protected" => OutlineVisibility.Protected,
                    _           => OutlineVisibility.Public,
                };
        return name is not null && name.StartsWith('#') ? OutlineVisibility.Private : OutlineVisibility.Public;
    }

    /// <summary>Strips the leading <c>:</c> of a TS type-annotation node (<c>": Foo"</c> → <c>"Foo"</c>).</summary>
    private static string? StripAnno(string? s)
    {
        if (s is null) return null;
        s = s.Trim();
        return s.StartsWith(':') ? s[1..].Trim() : s;
    }

    private static string? ResolveJs(string spec, string? baseDir)
    {
        if (baseDir is null || !(spec.StartsWith("./") || spec.StartsWith("../"))) return null;
        var combined = Path.GetFullPath(Path.Combine(baseDir, spec));
        string[] exts = [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs"];
        if (Path.HasExtension(combined) && File.Exists(combined)) return combined;
        foreach (var e in exts) if (File.Exists(combined + e)) return combined + e;
        foreach (var e in exts) { var idx = Path.Combine(combined, "index" + e); if (File.Exists(idx)) return idx; }
        return null;
    }

    // ── Python ─────────────────────────────────────────────────────────────────

    private static CodeOutline BuildPython(Node root, string? baseDir)
    {
        var imports = new List<ImportRef>();
        foreach (var n in Descendants(root))
        {
            if (n.Type == "import_statement")
                imports.Add(new ImportRef(OneLine(n.Text), null));
            else if (n.Type == "import_from_statement")
            {
                var module = n.GetChildForField("module_name") is { } mn ? mn.Text : "";
                imports.Add(new ImportRef(OneLine(n.Text), ResolvePy(module, baseDir)));
            }
        }

        var types = new List<OutlineType>();
        var topRaw = new List<RawMember>();
        ScanPyTypes(root, "", types, topRaw);
        return new CodeOutline(imports, types, Finalize(topRaw, ""));
    }

    private static Node Undecorate(Node n) =>
        n.Type == "decorated_definition" && n.GetChildForField("definition") is { } d ? d : n;

    private static void ScanPyTypes(Node container, string parentPath, List<OutlineType> outTypes,
        List<RawMember> topFuncs)
    {
        foreach (var rawChild in container.NamedChildren)
        {
            var child = Undecorate(rawChild);
            if (child.Type == "class_definition")
            {
                var name = Simple(NameOf(child) ?? "");
                if (name.Length == 0) continue;
                var path = parentPath.Length > 0 ? $"{parentPath}/T:{name}" : $"T:{name}";
                var body = child.GetChildForField("body");
                var members = body is { } b ? PyMembers(b, path) : [];
                outTypes.Add(new OutlineType(name, Line(child), OutlineKind.Class, path, members) { Bases = PyBases(child) });
                if (body is { } b2) ScanPyTypes(b2, path, outTypes, topFuncs);
            }
            else if (child.Type == "function_definition" && parentPath.Length == 0)
            {
                AddMember(topFuncs, NameOf(child), Line(child), OutlineKind.Method, OutlineVisibility.Public,
                    CallableSig(NameOf(child) ?? "", Field(child, "parameters"), StripArrow(Field(child, "return_type"))));
            }
            else if (child.Type is "block" or "module")
            {
                ScanPyTypes(child, parentPath, outTypes, topFuncs);
            }
        }
    }

    /// <summary>Base classes from a Python <c>class C(Base, Mixin)</c> list, dropping the implicit <c>object</c>
    /// and any keyword arguments (e.g. <c>metaclass=…</c>).</summary>
    private static IReadOnlyList<BaseRef> PyBases(Node classDef)
    {
        var supers = classDef.GetChildForField("superclasses");
        if (supers is null) return [];
        var bases = new List<BaseRef>();
        foreach (var a in supers.NamedChildren)
        {
            if (a.Type is "keyword_argument" or "comment") continue;
            var n = Simple(a.Text);
            if (n.Length > 0 && n != "object") bases.Add(new BaseRef(n, IsInterface: false));
        }
        return bases;
    }

    private static IReadOnlyList<OutlineMember> PyMembers(Node body, string typePath)
    {
        var raw = new List<RawMember>();
        foreach (var rawChild in body.NamedChildren)
        {
            var m = Undecorate(rawChild);
            if (m.Type == "function_definition")
            {
                var name = NameOf(m) ?? "";
                var kind = name == "__init__" ? OutlineKind.Constructor : OutlineKind.Method;
                AddMember(raw, name, Line(m), kind, PyVisibility(name),
                    CallableSig(name, Field(m, "parameters"), StripArrow(Field(m, "return_type"))));
            }
            else if (m.Type == "expression_statement" && FirstChild(m) is { Type: "assignment" } asg)
            {
                if (asg.GetChildForField("left") is { Type: "identifier" } lhs)
                    AddMember(raw, lhs.Text, Line(m), OutlineKind.Field, PyVisibility(lhs.Text),
                        FieldSig(lhs.Text, Field(asg, "type")));
            }
        }
        return Finalize(raw, typePath);
    }

    /// <summary>Python naming convention: <c>__x</c> (without trailing dunder) is private, a single leading
    /// underscore is protected, everything else (incl. dunder like <c>__init__</c>) public.</summary>
    private static OutlineVisibility PyVisibility(string name) =>
        name.StartsWith("__") && !name.EndsWith("__") ? OutlineVisibility.Private
        : name.StartsWith('_')                         ? OutlineVisibility.Protected
        : OutlineVisibility.Public;

    /// <summary>Strips a leading <c>-&gt;</c> off a Python return-type node, if present.</summary>
    private static string? StripArrow(string? s)
    {
        if (s is null) return null;
        s = s.Trim();
        return s.StartsWith("->") ? s[2..].Trim() : s;
    }

    private static string? ResolvePy(string module, string? baseDir)
    {
        if (baseDir is null || module.Length == 0 || module[0] != '.') return null;
        int dots = 0;
        while (dots < module.Length && module[dots] == '.') dots++;
        var rest = module[dots..];

        var dir = baseDir;
        for (int i = 1; i < dots; i++) dir = Path.Combine(dir, "..");
        var basePath = dir;
        if (rest.Length > 0)
            foreach (var seg in rest.Split('.'))
                basePath = Path.Combine(basePath, seg);

        var asFile = Path.GetFullPath(basePath + ".py");
        if (File.Exists(asFile)) return asFile;
        var asPkg = Path.GetFullPath(Path.Combine(basePath, "__init__.py"));
        return File.Exists(asPkg) ? asPkg : null;
    }

    // ── Ruby ───────────────────────────────────────────────────────────────────

    private static CodeOutline BuildRuby(Node root, string? baseDir)
    {
        var imports = new List<ImportRef>();
        foreach (var n in Descendants(root))
        {
            if (n.Type is not ("call" or "command" or "method_call")) continue;
            // require / require_relative "path"
            string? verb = null;
            foreach (var c in n.NamedChildren)
                if (c.Type == "identifier") { verb = c.Text; break; }
            if (verb is not ("require" or "require_relative")) continue;

            Node? argNode = null;
            foreach (var d in Descendants(n))
                if (d.Type is "string" or "string_content") { argNode = d; break; }
            if (argNode is not { } arg) continue;

            var spec = StripQuotes(arg.Text);
            imports.Add(new ImportRef(OneLine(n.Text), verb == "require_relative" ? ResolveRb(spec, baseDir) : null));
        }

        var types = new List<OutlineType>();
        ScanRbTypes(root, "", types);
        return new CodeOutline(imports, types, []);
    }

    private static void ScanRbTypes(Node container, string parentPath, List<OutlineType> outTypes)
    {
        foreach (var child in container.NamedChildren)
        {
            if (child.Type is "class" or "module")
            {
                var name = Simple(NameOf(child) ?? "");
                if (name.Length == 0) continue;
                var path = parentPath.Length > 0 ? $"{parentPath}/T:{name}" : $"T:{name}";
                var members = RbMembers(child, path);
                outTypes.Add(new OutlineType(name, Line(child), child.Type == "module" ? OutlineKind.Interface : OutlineKind.Class, path, members)
                    { Bases = RbBases(child) });
                ScanRbTypes(child.GetChildForField("body") is { } b ? b : child, path, outTypes);
            }
            else if (child.Type is "body_statement")
            {
                ScanRbTypes(child, parentPath, outTypes);
            }
        }
    }

    /// <summary>The single superclass from a Ruby <c>class C &lt; Base</c> (modules have none).</summary>
    private static IReadOnlyList<BaseRef> RbBases(Node classNode)
    {
        if (classNode.GetChildForField("superclass") is not { } sc) return [];
        var t = sc.Text.Trim();
        if (t.StartsWith('<')) t = t[1..].Trim();
        var n = Simple(t);
        return n.Length > 0 ? [new BaseRef(n, IsInterface: false)] : [];
    }

    private static IReadOnlyList<OutlineMember> RbMembers(Node classNode, string typePath)
    {
        var raw = new List<RawMember>();
        var body = classNode.GetChildForField("body") is { } b ? b : classNode;
        foreach (var m in body.NamedChildren)
        {
            if (m.Type is "method")
            {
                var name = NameOf(m) ?? "";
                var kind = name == "initialize" ? OutlineKind.Constructor : OutlineKind.Method;
                AddMember(raw, name, Line(m), kind, OutlineVisibility.Public,
                    CallableSig(name, Field(m, "parameters"), null));
            }
            else if (m.Type is "singleton_method")
                AddMember(raw, NameOf(m), Line(m), OutlineKind.Method, OutlineVisibility.Public,
                    CallableSig(NameOf(m) ?? "", Field(m, "parameters"), null));
        }
        return Finalize(raw, typePath);
    }

    private static string? ResolveRb(string spec, string? baseDir)
    {
        if (baseDir is null || spec.Length == 0) return null;
        var combined = Path.GetFullPath(Path.Combine(baseDir, spec));
        if (File.Exists(combined)) return combined;
        return File.Exists(combined + ".rb") ? combined + ".rb" : null;
    }

    // ── Java ─────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> JavaTypeDecls =
        ["class_declaration", "interface_declaration", "enum_declaration", "record_declaration", "annotation_type_declaration"];

    private static OutlineKind JavaTypeKind(string t) => t switch
    {
        "interface_declaration" or "annotation_type_declaration" => OutlineKind.Interface,
        "enum_declaration"                                       => OutlineKind.Enum,
        _                                                        => OutlineKind.Class,
    };

    private static CodeOutline BuildJava(Node root, string? baseDir)
    {
        var imports = new List<ImportRef>();
        foreach (var n in Descendants(root))
            if (n.Type == "import_declaration")
                imports.Add(new ImportRef(OneLine(n.Text), null));

        var types = new List<OutlineType>();
        ScanJavaTypes(root, "", types);
        return new CodeOutline(imports, types, []);
    }

    private static void ScanJavaTypes(Node container, string parentPath, List<OutlineType> outTypes)
    {
        foreach (var child in container.NamedChildren)
        {
            if (JavaTypeDecls.Contains(child.Type))
            {
                var name = Simple(NameOf(child) ?? "");
                if (name.Length == 0) continue;
                var kind = JavaTypeKind(child.Type);
                var path = parentPath.Length > 0 ? $"{parentPath}/T:{name}" : $"T:{name}";
                var body = child.GetChildForField("body");
                var members = body is { } b ? JavaMembers(b, path, kind) : [];
                outTypes.Add(new OutlineType(name, Line(child), kind, path, members) { Bases = JavaBases(child) });
                if (body is { } b2) ScanJavaTypes(b2, path, outTypes);   // nested types
            }
        }
    }

    private static IReadOnlyList<BaseRef> JavaBases(Node typeDecl)
    {
        var bases = new List<BaseRef>();
        if (typeDecl.GetChildForField("superclass") is { } sc)
            foreach (var c in sc.NamedChildren)
            {
                var n = Simple(c.Text);
                if (n.Length > 0) bases.Add(new BaseRef(n, IsInterface: false));
            }
        if (typeDecl.GetChildForField("interfaces") is { } ifaces)
            foreach (var d in Descendants(ifaces))
                if (d.Type == "type_identifier")
                {
                    var n = Simple(d.Text);
                    if (n.Length > 0) bases.Add(new BaseRef(n, IsInterface: true));
                }
        return bases;
    }

    private static IReadOnlyList<OutlineMember> JavaMembers(Node body, string typePath, OutlineKind typeKind)
    {
        var dflt = typeKind == OutlineKind.Interface ? OutlineVisibility.Public : OutlineVisibility.Internal;  // package-private
        var raw = new List<RawMember>();
        foreach (var m in body.NamedChildren)
        {
            switch (m.Type)
            {
                case "method_declaration":
                    AddMember(raw, NameOf(m), Line(m), OutlineKind.Method, JavaVisibility(m, dflt),
                        CallableSig(NameOf(m) ?? "", Field(m, "parameters"), Field(m, "type")));
                    break;
                case "constructor_declaration":
                    AddMember(raw, NameOf(m), Line(m), OutlineKind.Constructor, JavaVisibility(m, dflt),
                        CallableSig(NameOf(m) ?? "", Field(m, "parameters"), null));
                    break;
                case "field_declaration":
                {
                    var vis = JavaVisibility(m, dflt);
                    var type = Field(m, "type");
                    foreach (var vd in m.NamedChildren)
                        if (vd.Type == "variable_declarator")
                            AddMember(raw, NameOf(vd), Line(vd), OutlineKind.Field, vis, FieldSig(NameOf(vd) ?? "", type));
                    break;
                }
                case "enum_constant":
                    AddMember(raw, NameOf(m), Line(m), OutlineKind.Field, OutlineVisibility.Public);
                    break;
                case "enum_body_declarations":
                    foreach (var inner in JavaMembers(m, typePath, typeKind)) raw.Add(
                        new RawMember(inner.Name, inner.Line, inner.Kind, inner.Visibility, inner.Signature));
                    break;
            }
        }
        return Finalize(raw, typePath);
    }

    /// <summary>Java visibility from the <c>modifiers</c> node text (no explicit modifier ⇒ package-private,
    /// mapped to internal); interface members default public.</summary>
    private static OutlineVisibility JavaVisibility(Node m, OutlineVisibility dflt)
    {
        var text = FirstChild(m, "modifiers")?.Text ?? "";
        if (text.Contains("public")) return OutlineVisibility.Public;
        if (text.Contains("private")) return OutlineVisibility.Private;
        if (text.Contains("protected")) return OutlineVisibility.Protected;
        return dflt;
    }

    // ── PHP ──────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> PhpTypeDecls =
        ["class_declaration", "interface_declaration", "trait_declaration", "enum_declaration"];

    private static OutlineKind PhpTypeKind(string t) => t switch
    {
        "interface_declaration" or "trait_declaration" => OutlineKind.Interface,
        "enum_declaration"                             => OutlineKind.Enum,
        _                                              => OutlineKind.Class,
    };

    private static CodeOutline BuildPhp(Node root, string? baseDir)
    {
        var imports = new List<ImportRef>();
        foreach (var n in Descendants(root))
            if (n.Type == "namespace_use_declaration")
                imports.Add(new ImportRef(OneLine(n.Text), null));

        var types = new List<OutlineType>();
        var topRaw = new List<RawMember>();
        ScanPhpTypes(root, "", types, topRaw);
        return new CodeOutline(imports, types, Finalize(topRaw, ""));
    }

    private static void ScanPhpTypes(Node container, string parentPath, List<OutlineType> outTypes, List<RawMember> topFuncs)
    {
        foreach (var child in container.NamedChildren)
        {
            if (PhpTypeDecls.Contains(child.Type))
            {
                var name = Simple(NameOf(child) ?? "");
                if (name.Length == 0) continue;
                var path = parentPath.Length > 0 ? $"{parentPath}/T:{name}" : $"T:{name}";
                var body = FirstChild(child, "declaration_list");
                var members = body is { } b ? PhpMembers(b, path) : [];
                outTypes.Add(new OutlineType(name, Line(child), PhpTypeKind(child.Type), path, members) { Bases = PhpBases(child) });
            }
            else if (child.Type == "function_definition" && parentPath.Length == 0)
            {
                AddMember(topFuncs, NameOf(child), Line(child), OutlineKind.Method, OutlineVisibility.Public,
                    CallableSig(NameOf(child) ?? "", Field(child, "parameters"), Field(child, "return_type")));
            }
        }
    }

    private static IReadOnlyList<BaseRef> PhpBases(Node typeDecl)
    {
        var bases = new List<BaseRef>();
        if (FirstChild(typeDecl, "base_clause") is { } bc)
            foreach (var c in bc.NamedChildren)
            {
                var n = ShortName(c.Text);
                if (n.Length > 0) bases.Add(new BaseRef(n, IsInterface: false));
            }
        if (FirstChild(typeDecl, "class_interface_clause") is { } ic)
            foreach (var c in ic.NamedChildren)
            {
                var n = ShortName(c.Text);
                if (n.Length > 0) bases.Add(new BaseRef(n, IsInterface: true));
            }
        return bases;
    }

    private static IReadOnlyList<OutlineMember> PhpMembers(Node body, string typePath)
    {
        var raw = new List<RawMember>();
        foreach (var m in body.NamedChildren)
        {
            switch (m.Type)
            {
                case "method_declaration":
                {
                    var name = NameOf(m) ?? "";
                    var kind = name == "__construct" ? OutlineKind.Constructor : OutlineKind.Method;
                    AddMember(raw, name, Line(m), kind, PhpVisibility(m),
                        CallableSig(name, Field(m, "parameters"), Field(m, "return_type")));
                    break;
                }
                case "property_declaration":
                {
                    var vis = PhpVisibility(m);
                    var type = Field(m, "type");
                    foreach (var pe in m.NamedChildren)
                        if (pe.Type == "property_element" && pe.GetChildForField("name") is { } vn)
                        {
                            var pname = StripDollar(vn.Text);
                            AddMember(raw, pname, Line(pe), OutlineKind.Property, vis, FieldSig(pname, type));
                        }
                    break;
                }
                case "const_declaration":
                {
                    var vis = PhpVisibility(m);
                    foreach (var ce in m.NamedChildren)
                        if (ce.Type == "const_element")
                            AddMember(raw, FirstChild(ce, "name")?.Text, Line(ce), OutlineKind.Field, vis);
                    break;
                }
            }
        }
        return Finalize(raw, typePath);
    }

    private static OutlineVisibility PhpVisibility(Node m) =>
        FirstChild(m, "visibility_modifier")?.Text switch
        {
            "private"   => OutlineVisibility.Private,
            "protected" => OutlineVisibility.Protected,
            _           => OutlineVisibility.Public,   // PHP members are public by default
        };

    private static string StripDollar(string s) => s.StartsWith('$') ? s[1..] : s;

    // ── C++ ──────────────────────────────────────────────────────────────────────

    private static CodeOutline BuildCpp(Node root, string? baseDir)
    {
        var imports = new List<ImportRef>();
        foreach (var n in Descendants(root))
            if (n.Type == "preproc_include")
                imports.Add(new ImportRef(OneLine(n.Text), null));

        var types = new List<OutlineType>();
        var topRaw = new List<RawMember>();
        ScanCppTypes(root, "", types, topRaw);
        return new CodeOutline(imports, types, Finalize(topRaw, ""));
    }

    private static void ScanCppTypes(Node container, string parentPath, List<OutlineType> outTypes, List<RawMember> topFuncs)
    {
        foreach (var child in container.NamedChildren)
        {
            switch (child.Type)
            {
                case "class_specifier":
                case "struct_specifier":
                {
                    var name = Simple(NameOf(child) ?? "");
                    if (name.Length == 0) break;
                    var kind = child.Type == "struct_specifier" ? OutlineKind.Struct : OutlineKind.Class;
                    var path = parentPath.Length > 0 ? $"{parentPath}/T:{name}" : $"T:{name}";
                    var body = FirstChild(child, "field_declaration_list");
                    var dflt = child.Type == "struct_specifier" ? OutlineVisibility.Public : OutlineVisibility.Private;
                    var members = body is { } b ? CppMembers(b, path, name, dflt) : [];
                    outTypes.Add(new OutlineType(name, Line(child), kind, path, members) { Bases = CppBases(child) });
                    if (body is { } b2) ScanCppTypes(b2, path, outTypes, topFuncs);   // nested types
                    break;
                }
                case "enum_specifier":
                {
                    var name = Simple(NameOf(child) ?? "");
                    if (name.Length == 0) break;
                    var path = parentPath.Length > 0 ? $"{parentPath}/T:{name}" : $"T:{name}";
                    var raw = new List<RawMember>();
                    if (FirstChild(child, "enumerator_list") is { } el)
                        foreach (var e in el.NamedChildren)
                            if (e.Type == "enumerator") AddMember(raw, NameOf(e), Line(e), OutlineKind.Field);
                    outTypes.Add(new OutlineType(name, Line(child), OutlineKind.Enum, path, Finalize(raw, path)));
                    break;
                }
                case "namespace_definition":
                    if (FirstChild(child, "declaration_list") is { } nb) ScanCppTypes(nb, parentPath, outTypes, topFuncs);
                    break;
                case "declaration_list":
                    ScanCppTypes(child, parentPath, outTypes, topFuncs);
                    break;
                case "function_definition":
                    if (parentPath.Length == 0 && CppFunctionName(child) is { } fname)
                        AddMember(topFuncs, fname, Line(child), OutlineKind.Method, OutlineVisibility.Public, CppSig(child, fname));
                    break;
            }
        }
    }

    private static IReadOnlyList<BaseRef> CppBases(Node typeDecl)
    {
        var bc = FirstChild(typeDecl, "base_class_clause");
        if (bc is null) return [];
        var bases = new List<BaseRef>();
        foreach (var c in bc.NamedChildren)
            if (c.Type is "type_identifier" or "qualified_identifier" or "template_type")
            {
                var n = Simple(c.Text);
                if (n.Length > 0) bases.Add(new BaseRef(n, IsInterface: false));
            }
        return bases;
    }

    private static IReadOnlyList<OutlineMember> CppMembers(Node body, string typePath, string typeName, OutlineVisibility dflt)
    {
        var raw = new List<RawMember>();
        var vis = dflt;
        foreach (var m in body.NamedChildren)
        {
            switch (m.Type)
            {
                case "access_specifier":
                    vis = m.Text.Contains("public") ? OutlineVisibility.Public
                        : m.Text.Contains("protected") ? OutlineVisibility.Protected
                        : OutlineVisibility.Private;
                    break;
                case "function_definition":
                case "declaration":
                    if (CppFunctionName(m) is { } fn)
                        AddMember(raw, fn, Line(m), fn == typeName ? OutlineKind.Constructor : OutlineKind.Method, vis, CppSig(m, fn));
                    break;
                case "field_declaration":
                    if (CppFunctionName(m) is { } mfn)                                    // a method declaration
                        AddMember(raw, mfn, Line(m), mfn == typeName ? OutlineKind.Constructor : OutlineKind.Method, vis, CppSig(m, mfn));
                    else if (CppFieldName(m) is { } dn)                                   // a data member
                        AddMember(raw, dn, Line(m), OutlineKind.Field, vis, FieldSig(dn, Field(m, "type")));
                    break;
            }
        }
        return Finalize(raw, typePath);
    }

    /// <summary>The declared name of a function from its <c>function_declarator</c> (the method/ctor name), or null
    /// when the node declares no function.</summary>
    private static string? CppFunctionName(Node node)
    {
        var fd = FindDescendantOfType(node, "function_declarator");
        var d = fd?.GetChildForField("declarator");
        return d is null ? null : (d.Type is "identifier" or "field_identifier" ? d.Text : LeafIdentifier(d));
    }

    private static string? CppFieldName(Node fieldDecl)
    {
        var d = fieldDecl.GetChildForField("declarator");
        return d is null ? null : (d.Type is "field_identifier" or "identifier" ? d.Text : LeafIdentifier(d));
    }

    private static string CppSig(Node node, string name)
    {
        var fd = FindDescendantOfType(node, "function_declarator");
        return CallableSig(name, fd?.GetChildForField("parameters")?.Text, Field(node, "type"));
    }

    // ── Rust ─────────────────────────────────────────────────────────────────────

    /// <summary>A type accumulated across its declaration and one-or-more <c>impl</c> blocks.</summary>
    private sealed class RustType
    {
        public string Name = "";
        public int Line;
        public OutlineKind Kind;
        public readonly List<RawMember> Members = [];
        public readonly List<BaseRef> Bases = [];
    }

    private static CodeOutline BuildRust(Node root, string? baseDir)
    {
        var imports = new List<ImportRef>();
        foreach (var n in root.NamedChildren)
            if (n.Type == "use_declaration")
                imports.Add(new ImportRef(OneLine(n.Text), null));

        var byName = new Dictionary<string, RustType>();
        var order = new List<string>();
        var topRaw = new List<RawMember>();

        foreach (var child in root.NamedChildren)
        {
            switch (child.Type)
            {
                case "struct_item": RustDeclare(byName, order, child, OutlineKind.Struct, RustStructFields(child)); break;
                case "enum_item":   RustDeclare(byName, order, child, OutlineKind.Enum, RustEnumVariants(child)); break;
                case "trait_item":  RustDeclare(byName, order, child, OutlineKind.Interface, RustTraitMethods(child)); break;
                case "function_item":
                    AddMember(topRaw, NameOf(child), Line(child), OutlineKind.Method, RustVisibility(child),
                        CallableSig(NameOf(child) ?? "", Field(child, "parameters"), Field(child, "return_type")));
                    break;
                case "impl_item": RustApplyImpl(byName, order, child); break;
            }
        }

        var types = new List<OutlineType>();
        foreach (var name in order)
        {
            var rt = byName[name];
            types.Add(new OutlineType(rt.Name, rt.Line, rt.Kind, $"T:{rt.Name}", Finalize(rt.Members, $"T:{rt.Name}")) { Bases = rt.Bases });
        }
        return new CodeOutline(imports, types, Finalize(topRaw, ""));
    }

    private static RustType RustEntry(Dictionary<string, RustType> byName, List<string> order, string name, int line, OutlineKind kind)
    {
        if (!byName.TryGetValue(name, out var rt))
        {
            rt = new RustType { Name = name, Line = line, Kind = kind };
            byName[name] = rt;
            order.Add(name);
        }
        return rt;
    }

    private static void RustDeclare(Dictionary<string, RustType> byName, List<string> order, Node node, OutlineKind kind, List<RawMember> members)
    {
        var name = Simple(NameOf(node) ?? "");
        if (name.Length == 0) return;
        var rt = RustEntry(byName, order, name, Line(node), kind);
        rt.Kind = kind;                 // a real declaration wins over a placeholder created by an impl block
        rt.Members.AddRange(members);
    }

    private static void RustApplyImpl(Dictionary<string, RustType> byName, List<string> order, Node impl)
    {
        var typeName = Simple(Field(impl, "type") ?? "");
        if (typeName.Length == 0) return;
        var rt = RustEntry(byName, order, typeName, Line(impl), OutlineKind.Struct);

        if (impl.GetChildForField("trait") is { } tr)
        {
            var tn = Simple(tr.Text);
            if (tn.Length > 0) rt.Bases.Add(new BaseRef(tn, IsInterface: true));
        }
        if (FirstChild(impl, "declaration_list") is { } body)
            foreach (var f in body.NamedChildren)
                if (f.Type is "function_item" or "function_signature_item")
                    AddMember(rt.Members, NameOf(f), Line(f), OutlineKind.Method, RustVisibility(f),
                        CallableSig(NameOf(f) ?? "", Field(f, "parameters"), Field(f, "return_type")));
    }

    private static List<RawMember> RustStructFields(Node structItem)
    {
        var raw = new List<RawMember>();
        if (FirstChild(structItem, "field_declaration_list") is { } body)
            foreach (var f in body.NamedChildren)
                if (f.Type == "field_declaration")
                    AddMember(raw, Field(f, "name"), Line(f), OutlineKind.Field, RustVisibility(f),
                        FieldSig(Field(f, "name") ?? "", Field(f, "type")));
        return raw;
    }

    private static List<RawMember> RustEnumVariants(Node enumItem)
    {
        var raw = new List<RawMember>();
        if (FirstChild(enumItem, "enum_variant_list") is { } body)
            foreach (var v in body.NamedChildren)
                if (v.Type == "enum_variant")
                    AddMember(raw, NameOf(v), Line(v), OutlineKind.Field, RustVisibility(v));
        return raw;
    }

    private static List<RawMember> RustTraitMethods(Node traitItem)
    {
        var raw = new List<RawMember>();
        if (FirstChild(traitItem, "declaration_list") is { } body)
            foreach (var f in body.NamedChildren)
                if (f.Type is "function_signature_item" or "function_item")
                    AddMember(raw, NameOf(f), Line(f), OutlineKind.Method, OutlineVisibility.Public,
                        CallableSig(NameOf(f) ?? "", Field(f, "parameters"), Field(f, "return_type")));
        return raw;
    }

    private static OutlineVisibility RustVisibility(Node n) =>
        FirstChild(n, "visibility_modifier") is not null ? OutlineVisibility.Public : OutlineVisibility.Private;

    // ── Misc ───────────────────────────────────────────────────────────────────

    /// <summary>The short (last-segment) name from a possibly-qualified identifier (<c>App\Models\User</c> →
    /// <c>User</c>, <c>std::vector</c> → <c>vector</c>), generics stripped.</summary>
    private static string ShortName(string text)
    {
        var t = text.Trim();
        int slash = t.LastIndexOf('\\');
        if (slash >= 0) t = t[(slash + 1)..];
        int colons = t.LastIndexOf("::", StringComparison.Ordinal);
        if (colons >= 0) t = t[(colons + 2)..];
        return Simple(t);
    }

    /// <summary>The first descendant of <paramref name="n"/> with type <paramref name="type"/> (DFS), or null.</summary>
    private static Node? FindDescendantOfType(Node n, string type)
    {
        foreach (var c in n.NamedChildren)
        {
            if (c.Type == type) return c;
            if (FindDescendantOfType(c, type) is { } d) return d;
        }
        return null;
    }

    /// <summary>The first identifier-like leaf under a (possibly pointer/reference) declarator.</summary>
    private static string? LeafIdentifier(Node n)
    {
        if (n.Type is "identifier" or "field_identifier" or "type_identifier") return n.Text;
        foreach (var c in n.NamedChildren)
            if (LeafIdentifier(c) is { } id) return id;
        return null;
    }

    private static string StripQuotes(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && (s[0] is '"' or '\'' or '`') && s[^1] == s[0]) return s[1..^1];
        return s;
    }
}
