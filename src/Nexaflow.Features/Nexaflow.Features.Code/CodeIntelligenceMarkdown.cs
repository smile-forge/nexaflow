using System;
using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.Syntax;

namespace Nexaflow.Features.Code;

/// <summary>
/// Builds the "As Code" side-panel markdown from a <see cref="CodeOutline"/>:
///   • a <b>Dependencies</b> list — imports that resolve to a local file become <c>file:</c> links (clicking
///     opens that file As Code), others stay plain text;
///   • a <b>Structure</b> mermaid <c>classDiagram</c> — one box per type. Each member row carries a hidden
///     <c>… @@&lt;url&gt;</c> token so the renderer makes it a clickable link to the member's definition;
///   • a <b>Members</b> list for any top-level (free) functions.
///
/// Member/link URLs are <c>file:///&lt;path&gt;#ast=&lt;url-encoded ast-path&gt;</c>. The host intercepts them:
/// same-file + ast ⇒ scroll to the resolved line; other file ⇒ open it As Code. The AST-path fragment (not a
/// line number) keeps a link valid across edits — see <see cref="CodeStructureExtractor"/>.
/// </summary>
public static class CodeIntelligenceMarkdown
{
    public static string Build(string filePath, CodeOutline outline)
    {
        var fileUri = SafeFileUri(filePath);
        var sb = new StringBuilder();

        // Host imports plus those from any embedded language (a notebook cell's imports, a <script>'s imports …).
        var imports = new List<ImportRef>();
        CollectImports(outline, imports, new HashSet<string>());
        if (imports.Count > 0)
        {
            sb.AppendLine("## Dependencies").AppendLine();
            foreach (var imp in imports)
            {
                if (imp.ResolvedPath is { } rp)
                    sb.AppendLine($"- [{LinkLabel(imp.Text)}]({SafeFileUri(rp)})");
                else
                    // Code span so angle-bracketed text (e.g. C++ `#include <string>`) isn't eaten as an HTML tag.
                    sb.AppendLine($"- `{imp.Text.Replace("`", "'")}`");
            }
            sb.AppendLine();
        }

        if ((outline.Types.Count > 0 || outline.Embedded.Count > 0) && fileUri is not null)
        {
            sb.AppendLine("## Structure").AppendLine();
            sb.AppendLine("```mermaid");
            sb.AppendLine("classDiagram");
            // LR ranks the diagram left-to-right, which stacks the (usually unrelated) classes of a file into a
            // vertical column so they fit the narrow side panel instead of spreading across one wide row.
            sb.AppendLine("  direction LR");
            foreach (var t in outline.Types)
            {
                var cls = SafeId(t.Name);
                AppendTypeBox(sb, "  ", cls, label: null, t.Members, fileUri);
                AppendBases(sb, "  ", cls, t.Bases);
            }

            // Embedded languages render as their own namespace cluster — a linked sub-graph whose member rows
            // still jump to their (absolute) line in this file.
            foreach (var e in outline.Embedded)
                AppendEmbedded(sb, e, fileUri);

            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (outline.TopLevel.Count > 0 && fileUri is not null)
        {
            sb.AppendLine("## Members").AppendLine();
            foreach (var m in outline.TopLevel)
                sb.AppendLine($"- [{LinkLabel(MemberLabel(m))}]({fileUri}#ast={Uri.EscapeDataString(m.AstPath)})");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Gathers imports from the outline and, recursively, its embedded-language regions, de-duped by
    /// text so an import shared by host and embedded code is listed once.</summary>
    private static void CollectImports(CodeOutline o, List<ImportRef> into, HashSet<string> seen)
    {
        foreach (var imp in o.Imports)
            if (seen.Add(imp.Text)) into.Add(imp);
        foreach (var e in o.Embedded)
            CollectImports(e.Outline, into, seen);
    }

    /// <summary>Emits one Mermaid class box. <paramref name="label"/> null ⇒ the id is shown verbatim (host
    /// types, whose name is already clean); otherwise a <c>["label"]</c> override shows a friendly name over a
    /// collision-proof id (embedded types are id-namespaced). Each member row carries the hidden <c>@@&lt;url&gt;</c>
    /// link token.</summary>
    private static void AppendTypeBox(StringBuilder sb, string indent, string id, string? label,
        IReadOnlyList<OutlineMember> members, string fileUri)
    {
        var head = label is null ? $"class {id}" : $"class {id}[\"{MermaidLabel(label)}\"]";
        if (members.Count == 0)
        {
            sb.AppendLine($"{indent}{head}");
            return;
        }
        sb.AppendLine($"{indent}{head} {{");
        foreach (var m in members)
            sb.AppendLine($"{indent}  {BoxMemberLabel(m)} @@{fileUri}#ast={Uri.EscapeDataString(m.AstPath)}");
        sb.AppendLine($"{indent}}}");
    }

    /// <summary>Inheritance / realization edges: a base class draws a solid hollow-triangle arrow
    /// (<c>Base &lt;|-- Derived</c>), an interface a dashed one (<c>Iface &lt;|.. Impl</c>). The base box is
    /// created implicitly, so parents/interfaces declared outside the file still appear.</summary>
    private static void AppendBases(StringBuilder sb, string indent, string id, IReadOnlyList<BaseRef> bases)
    {
        foreach (var b in bases)
            sb.AppendLine($"{indent}{SafeId(b.Name)} <|{(b.IsInterface ? ".." : "--")} {id}");
    }

    /// <summary>Renders an embedded-language region as a Mermaid <c>namespace</c> cluster (a linked sub-graph):
    /// its types become id-namespaced class boxes, its free functions gather into one box labelled by the
    /// region, and edges + any deeper embeds follow after the cluster closes (Mermaid ids are global, so the
    /// edges still attach to the boxed nodes).</summary>
    private static void AppendEmbedded(StringBuilder sb, EmbeddedOutline e, string fileUri)
    {
        var ns = SafeId($"{e.Language}_L{e.HostLine}");
        sb.AppendLine($"  namespace {ns} {{");

        foreach (var t in e.Outline.Types)
            AppendTypeBox(sb, "    ", SafeId($"{ns}__{t.Name}"), t.Name, t.Members, fileUri);

        if (e.Outline.TopLevel.Count > 0)
            AppendTypeBox(sb, "    ", SafeId($"{ns}__free"), $"{e.Language} · line {e.HostLine}",
                e.Outline.TopLevel, fileUri);

        sb.AppendLine("  }");

        foreach (var t in e.Outline.Types)
            AppendBases(sb, "  ", SafeId($"{ns}__{t.Name}"), t.Bases);
        foreach (var nested in e.Outline.Embedded)
            AppendEmbedded(sb, nested, fileUri);
    }

    /// <summary>Escapes a class-box label so quotes/brackets can't break the <c>["…"]</c> override.</summary>
    private static string MermaidLabel(string s) => s.Replace("\"", "'").Replace("[", "(").Replace("]", ")");

    /// <summary>The list label for a member: a UML visibility marker then the full signature (name +
    /// parameters + return/field type). The Mermaid class parser reads the leading marker as visibility and
    /// the <c>()</c> as the method/attribute discriminator, reflowing a trailing return type after a colon.</summary>
    private static string MemberLabel(OutlineMember m) => VisibilitySymbol(m.Visibility) + m.Signature;

    /// <summary>As <see cref="MemberLabel"/> but for a diagram box, where a long signature would make the box
    /// very wide: the parameter list is abbreviated so boxes stay readable (the full text is still in the file).</summary>
    private static string BoxMemberLabel(OutlineMember m) => VisibilitySymbol(m.Visibility) + Abbreviate(m.Signature);

    /// <summary>Keeps a member's box label compact: once the signature passes <see cref="MaxBoxSignature"/>
    /// chars, the parameter list collapses to a short stub + ellipsis, preserving the name and return type
    /// (which carry the most meaning); anything still over-long is hard-truncated.</summary>
    private static string Abbreviate(string signature)
    {
        if (signature.Length <= MaxBoxSignature) return signature;

        int open = signature.IndexOf('(');
        int close = open >= 0 ? MatchingParen(signature, open) : -1;
        if (close > open + 1)   // a non-empty parameter list to collapse
        {
            var prms = signature[(open + 1)..close];
            var stub = prms.Length > 4 ? prms[..4].TrimEnd() + "…" : prms;
            signature = signature[..(open + 1)] + stub + signature[close..];
        }
        return signature.Length <= MaxBoxSignature ? signature : signature[..(MaxBoxSignature - 1)] + "…";
    }

    private const int MaxBoxSignature = 35;

    /// <summary>The index of the close paren matching the open paren at <paramref name="open"/>, or -1.</summary>
    private static int MatchingParen(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    private static string VisibilitySymbol(OutlineVisibility v) => v switch
    {
        OutlineVisibility.Private   => "-",
        OutlineVisibility.Protected => "#",
        OutlineVisibility.Internal  => "~",
        _                           => "+",
    };

    /// <summary>A Mermaid-safe class id (identifier characters only).</summary>
    private static string SafeId(string name) => Regex.Replace(name, "[^A-Za-z0-9_]", "_");

    /// <summary>Escapes a markdown link label so brackets in import text can't break the link.</summary>
    private static string LinkLabel(string s) => s.Replace("[", "(").Replace("]", ")");

    private static string? SafeFileUri(string path)
    {
        try { return new Uri(path).AbsoluteUri; }
        catch { return null; }
    }
}
