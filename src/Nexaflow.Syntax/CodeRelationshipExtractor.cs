using System.Text.RegularExpressions;
using TreeSitter;

namespace Nexaflow.Syntax;

/// <summary>What a raw (unresolved) reference site is: a method <see cref="Call"/> or an object <see cref="New"/>
/// (instantiation). The referenced <see cref="RawRef.Name"/> is a bare identifier — cross-file resolution to a
/// specific declaration (with a confidence score) happens later, against the whole graph's symbol set.</summary>
public enum RawRefKind { Call, New }

/// <summary>
/// One reference found in a source file: <see cref="FromAst"/> is the enclosing member's AST path (the same
/// name-based path <see cref="CodeStructureExtractor"/> produces, so it lines up with the graph's code nodes),
/// <see cref="Name"/> is the referenced simple name, unresolved.
/// </summary>
public sealed record RawRef(string FromAst, RawRefKind Kind, string Name, int Line);

/// <summary>A member's signature: its return type and parameter types (bare names, unresolved) — the basis for an
/// n-ary <c>signature</c> hyperedge (member → return, params[]).</summary>
public sealed record RawSignature(string MemberAst, string? ReturnType, IReadOnlyList<string> ParamTypes, int Line);

/// <summary>An attribute on a type/member: the attribute's bare name + how many arguments it carries — the basis
/// for an <c>annotated</c> hyperedge (target → attr).</summary>
public sealed record RawAttribute(string TargetAst, string AttrName, int ArgCount, int Line);

/// <summary>A call whose arguments include freshly-created objects (<c>Foo(new Bar())</c>) — the basis for an
/// n-ary <c>calls</c> hyperedge (caller → callee, arg types[]). Plain-identifier args are omitted (they'd resolve
/// to locals as often as to graph nodes); only <c>new T</c> args, which name a type unambiguously, are captured.</summary>
public sealed record RawCall(string FromAst, string Callee, IReadOnlyList<string> NewArgTypes, int Line);

/// <summary>A file-like token found inside a <b>string literal</b> in a member (e.g. <c>"default-filemap.json"</c>,
/// <c>"docs/features.md"</c>) — the basis for a low-confidence <c>mentions</c> edge once resolved (uniquely) to a
/// repo file. Literals only (not comments/prose), and resolution drops ambiguous names — so it's a hint, never a claim.</summary>
public sealed record RawFileRef(string FromAst, string Token, int Line);

/// <summary>Everything one file's member bodies + declarations contribute to the inferred graph: binary
/// <see cref="Refs"/> (calls/instantiations) plus the n-ary hyperedge sites (<see cref="Signatures"/>,
/// <see cref="Attributes"/>, <see cref="Calls"/>). All names are unresolved — resolution happens globally later.</summary>
public sealed record CodeRelations(
    IReadOnlyList<RawRef> Refs,
    IReadOnlyList<RawSignature> Signatures,
    IReadOnlyList<RawAttribute> Attributes,
    IReadOnlyList<RawCall> Calls,
    IReadOnlyList<RawFileRef> FileRefs)
{
    public static readonly CodeRelations Empty = new([], [], [], [], []);
}

/// <summary>
/// Walks a parse tree for the <b>relationships inside member bodies + declarations</b> — method calls, object
/// creations, member signatures, and attributes — that the structural <see cref="CodeOutline"/> deliberately
/// skips. Each site is attributed to its enclosing type/member so an edge can hang off the right code node. C#
/// only for now (every other grammar returns nothing); a manual walk (not a tree-sitter query) so it can thread
/// the enclosing AST path and fail soft on node kinds a grammar build may not expose. Single-thread use, like
/// <see cref="CodeHighlighter"/>.
/// </summary>
public sealed class CodeRelationshipExtractor
{
    /// <summary>Accumulates the four site kinds through the walk (a struct so the walk threads one ref).</summary>
    private sealed class Acc
    {
        public readonly List<RawRef> Refs = [];
        public readonly List<RawSignature> Signatures = [];
        public readonly List<RawAttribute> Attributes = [];
        public readonly List<RawCall> Calls = [];
        public readonly List<RawFileRef> FileRefs = [];
        public readonly HashSet<(string, string)> SeenFileRefs = [];   // dedup (member, token) within a file
    }

    public CodeRelations Extract(string grammarId, string text, string? baseDir = null)
    {
        if (grammarId != "c-sharp" || string.IsNullOrEmpty(text)) return CodeRelations.Empty;
        using var hl = CodeHighlighter.TryCreate(grammarId);
        if (hl is null) return CodeRelations.Empty;
        try
        {
            return hl.WithParseTree(text, root =>
            {
                var acc = new Acc();
                ScanTypes(root, "", acc);
                return new CodeRelations(acc.Refs, acc.Signatures, acc.Attributes, acc.Calls, acc.FileRefs);
            }) ?? CodeRelations.Empty;
        }
        catch { return CodeRelations.Empty; }
    }

    private static readonly HashSet<string> CsTypeDecls =
        ["class_declaration", "struct_declaration", "interface_declaration", "enum_declaration", "record_declaration", "record_struct_declaration"];

    private static void ScanTypes(Node container, string parentPath, Acc acc)
    {
        foreach (var child in container.NamedChildren)
        {
            if (CsTypeDecls.Contains(child.Type))
            {
                var name = Simple(NameOf(child) ?? "");
                if (name.Length == 0) continue;
                var typePath = parentPath.Length > 0 ? $"{parentPath}/T:{name}" : $"T:{name}";
                CollectAttributes(child, typePath, acc);   // attributes on the type itself
                if (child.GetChildForField("body") is { } body)
                {
                    ScanMembers(body, typePath, acc);
                    ScanTypes(body, typePath, acc);   // nested types
                }
            }
            else if (child.Type is "namespace_declaration" or "file_scoped_namespace_declaration" or "declaration_list")
            {
                ScanTypes(child, parentPath, acc);
            }
        }
    }

    // Enumerate members in declaration order (mirroring CodeStructureExtractor's CollectCsMembers + Finalize), so
    // the #index overload suffixes match the graph's member AST paths, then collect the sites in each member.
    private static void ScanMembers(Node body, string typePath, Acc acc)
    {
        // Declares is where the member's attributes live. For a field that is NOT the member node: the
        // declarator names the field, but [ObservableProperty] hangs off the enclosing field_declaration, so
        // reading attributes from the declarator would miss every one of them.
        var members = new List<(Node Node, string Seg, Node Declares)>();
        foreach (var m in body.NamedChildren)
        {
            switch (m.Type)
            {
                case "method_declaration":
                case "local_function_statement":
                case "constructor_declaration":
                    AddMember(members, m, NameOf(m), 'M', m); break;
                case "property_declaration":
                case "indexer_declaration":
                case "event_declaration":
                    AddMember(members, m, NameOf(m), 'P', m); break;
                case "field_declaration":
                case "event_field_declaration":
                    foreach (var d in Descendants(m))
                        if (d.Type == "variable_declarator")
                            AddMember(members, d, NameOf(d), 'F', m);
                    break;
            }
        }

        var total = new Dictionary<string, int>();
        foreach (var (_, seg, _) in members) total[seg] = total.GetValueOrDefault(seg) + 1;

        var seen = new Dictionary<string, int>();
        foreach (var (node, seg, declares) in members)
        {
            var idx = seen.GetValueOrDefault(seg);
            seen[seg] = idx + 1;
            var fseg = total[seg] > 1 ? $"{seg}#{idx}" : seg;
            var memberPath = $"{typePath}/{fseg}";
            CollectSites(node, memberPath, acc);
            CollectSignature(node, memberPath, acc);
            CollectAttributes(declares, memberPath, acc);
        }
    }

    private static void AddMember(List<(Node, string, Node)> members, Node node, string? name, char kind, Node declares)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length > 0) members.Add((node, $"{kind}:{n}", declares));
    }

    // ── call / instantiation sites (binary refs) + new-arg calls (hyperedge) ──────────────────────
    private static void CollectSites(Node member, string memberPath, Acc acc)
    {
        foreach (var d in Descendants(member))
        {
            if (d.Type == "invocation_expression")
            {
                if (CalleeName(d) is not { Length: > 0 } callee) continue;
                var line = d.StartPosition.Row + 1;
                acc.Refs.Add(new RawRef(memberPath, RawRefKind.Call, callee, line));

                var newArgs = NewArgTypes(d);
                if (newArgs.Count > 0) acc.Calls.Add(new RawCall(memberPath, callee, newArgs, line));
            }
            else if (d.Type == "object_creation_expression")
            {
                if (d.GetChildForField("type") is { } t && Simple(t.Text) is { Length: > 0 } typeName)
                    acc.Refs.Add(new RawRef(memberPath, RawRefKind.New, typeName, d.StartPosition.Row + 1));
            }
            else if (d.Type is "string_literal" or "verbatim_string_literal" or "raw_string_literal")
            {
                foreach (var token in FileLikeTokens(d.Text))
                    if (acc.SeenFileRefs.Add((memberPath, token)))
                        acc.FileRefs.Add(new RawFileRef(memberPath, token, d.StartPosition.Row + 1));
            }
        }
    }

    // A path-ish token ending in a letter-led extension (so "logo.png"/"docs/x.md" match, but "1.0.0"/"127.0.0.1" don't).
    private static readonly Regex FileToken = new(@"[\w\-./\\]+\.[A-Za-z][A-Za-z0-9]{0,7}", RegexOptions.Compiled);

    /// <summary>File-like tokens inside a string literal's text. Over-matching is harmless — resolution only keeps a
    /// token that maps uniquely to a real repo file, so junk tokens (namespaces, versions) simply don't resolve.</summary>
    private static IEnumerable<string> FileLikeTokens(string literalText)
    {
        foreach (Match m in FileToken.Matches(literalText))
        {
            var t = m.Value.Replace('\\', '/').Trim('/');
            var name = t.LastIndexOf('/') is var s && s >= 0 ? t[(s + 1)..] : t;
            if (name.Length >= 3 && name.Contains('.')) yield return t;
        }
    }

    /// <summary>The types of any <c>new T(...)</c> arguments passed to an invocation (direct arguments only).</summary>
    private static IReadOnlyList<string> NewArgTypes(Node invocation)
    {
        if (invocation.GetChildForField("arguments") is not { } args) return [];
        List<string>? types = null;
        foreach (var arg in args.NamedChildren)
            foreach (var e in arg.NamedChildren)
                if (e.Type == "object_creation_expression"
                    && e.GetChildForField("type") is { } t && Simple(t.Text) is { Length: > 0 } name)
                    (types ??= []).Add(name);
        return (IReadOnlyList<string>?)types ?? [];
    }

    // ── signature: return + parameter types ───────────────────────────────────────────────────────
    private static void CollectSignature(Node member, string memberPath, Acc acc)
    {
        if (member.Type is not ("method_declaration" or "constructor_declaration" or "local_function_statement")) return;

        var returns = member.Type == "constructor_declaration"
            ? null
            : Simple((member.GetChildForField("returns") ?? member.GetChildForField("type"))?.Text ?? "");
        if (returns is { Length: 0 }) returns = null;

        var paramTypes = new List<string>();
        if (member.GetChildForField("parameters") is { } plist)
            foreach (var p in plist.NamedChildren)
                if (p.Type == "parameter" && p.GetChildForField("type") is { } pt && Simple(pt.Text) is { Length: > 0 } tn)
                    paramTypes.Add(tn);

        if (returns is null && paramTypes.Count == 0) return;   // nothing to relate
        acc.Signatures.Add(new RawSignature(memberPath, returns, paramTypes, member.StartPosition.Row + 1));
    }

    // ── attributes ────────────────────────────────────────────────────────────────────────────────
    private static void CollectAttributes(Node decl, string targetPath, Acc acc)
    {
        foreach (var list in decl.NamedChildren)
        {
            if (list.Type != "attribute_list") continue;
            foreach (var attr in list.NamedChildren)
            {
                if (attr.Type != "attribute") continue;
                var name = Simple((attr.GetChildForField("name") ?? FirstNameLike(attr))?.Text ?? "");
                if (name.Length == 0) continue;
                var argCount = 0;
                foreach (var d in Descendants(attr))
                    if (d.Type == "attribute_argument") argCount++;
                acc.Attributes.Add(new RawAttribute(targetPath, name, argCount, attr.StartPosition.Row + 1));
            }
        }
    }

    private static Node? FirstNameLike(Node n)
    {
        foreach (var c in n.NamedChildren)
            if (c.Type is "identifier" or "qualified_name" or "generic_name") return c;
        return null;
    }

    private static string? CalleeName(Node invocation)
    {
        var fn = invocation.GetChildForField("function");
        if (fn is null) return null;
        return fn.Type switch
        {
            "member_access_expression" => fn.GetChildForField("name")?.Text,
            "identifier" => fn.Text,
            "generic_name" => NameOf(fn),
            _ => null,   // invocation on an invocation, element access, … — skip
        };
    }

    // ── small helpers (self-contained copies of the outline extractor's) ──────────────────────────
    private static string? NameOf(Node n)
    {
        if (n.GetChildForField("name") is { } f) return f.Text;
        foreach (var c in n.NamedChildren)
            if (c.Type.Contains("identifier") || c.Type is "constant") return c.Text;
        return null;
    }

    private static string Simple(string name)
    {
        var cut = name.Length;
        foreach (var ch in stackalloc[] { '<', '~', '(', ' ' })
        {
            var i = name.IndexOf(ch);
            if (i >= 0 && i < cut) cut = i;
        }
        return name[..cut].Trim();
    }

    private static IEnumerable<Node> Descendants(Node n)
    {
        foreach (var c in n.NamedChildren)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }
}
