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
    public CodeOutline Extract(string grammarId, string text, string? baseDir = null)
    {
        if (string.IsNullOrEmpty(grammarId) || string.IsNullOrEmpty(text)) return CodeOutline.Empty;
        using var hl = CodeHighlighter.TryCreate(grammarId);
        if (hl is null) return CodeOutline.Empty;
        try { return hl.WithParseTree(text, root => Build(grammarId, root, baseDir)) ?? CodeOutline.Empty; }
        catch { return CodeOutline.Empty; }   // never throw — a malformed parse just yields no outline
    }

    /// <summary>The current 1-based line of the element with <paramref name="astPath"/>, or null if it no
    /// longer exists (renamed / removed). Resolved against the live <paramref name="text"/>.</summary>
    public int? ResolveLine(string grammarId, string text, string astPath, string? baseDir = null)
    {
        if (string.IsNullOrEmpty(astPath)) return null;
        var outline = Extract(grammarId, text, baseDir);
        foreach (var t in outline.Types)
        {
            if (t.AstPath == astPath) return t.Line;
            foreach (var m in t.Members)
                if (m.AstPath == astPath) return m.Line;
        }
        foreach (var m in outline.TopLevel)
            if (m.AstPath == astPath) return m.Line;
        return null;
    }

    private static CodeOutline Build(string grammar, Node root, string? baseDir) => grammar switch
    {
        "c-sharp"    => BuildCSharp(root, baseDir),
        "javascript" => BuildJsTs(root, baseDir, ts: false),
        "typescript" => BuildJsTs(root, baseDir, ts: true),
        "python"     => BuildPython(root, baseDir),
        "ruby"       => BuildRuby(root, baseDir),
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
        return Finalize(raw, typePath);
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

    // ── Misc ───────────────────────────────────────────────────────────────────

    private static string StripQuotes(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && (s[0] is '"' or '\'' or '`') && s[^1] == s[0]) return s[1..^1];
        return s;
    }
}
