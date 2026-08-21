namespace Nexaflow.Syntax;

/// <summary>What an outline element is. Type-like kinds (Class…Enum) describe a box in a class diagram;
/// member kinds (Method…Variable) describe a row inside one.</summary>
public enum OutlineKind { Class, Struct, Interface, Enum, Method, Constructor, Property, Field, Variable }

/// <summary>A member's access level, mapped to the UML class-diagram visibility markers
/// (<c>+</c> public, <c>-</c> private, <c>#</c> protected, <c>~</c> internal/package).</summary>
public enum OutlineVisibility { Public, Private, Protected, Internal }

/// <summary>An import / include / require statement, with the local file it resolves to when the specifier
/// is relative (e.g. <c>./util</c>, <c>from .x import</c>). <see cref="ResolvedPath"/> is null for library /
/// namespace imports that don't map to a file (e.g. C# <c>using System.Text;</c>).</summary>
public sealed record ImportRef(string Text, string? ResolvedPath);

/// <summary>A base type a class extends or implements. <see cref="IsInterface"/> distinguishes realization
/// (interface, drawn dashed) from inheritance (class, drawn solid) so the diagram uses the right UML arrow.
/// <see cref="Name"/> is the bare type name (generics stripped), used both as the diagram box id and label.</summary>
public sealed record BaseRef(string Name, bool IsInterface);

/// <summary>One member of a type — a method, property, field, etc. <see cref="AstPath"/> is a structure-keyed
/// identifier (see <see cref="CodeStructureExtractor"/>) used to re-find the member by name after edits, so a
/// link to it stays valid until the structure itself changes. <see cref="Signature"/> is the display form
/// (name + parameters + return/field type, no visibility marker). <see cref="Line"/> is 1-based.</summary>
public sealed record OutlineMember(string Name, int Line, OutlineKind Kind, string Signature, string AstPath)
{
    /// <summary>The member's access level (defaults to public so callers that don't extract it still render a <c>+</c>).</summary>
    public OutlineVisibility Visibility { get; init; } = OutlineVisibility.Public;

    /// <summary>1-based line the declaration ends on, or 0 when the extractor didn't record one.
    /// <para>
    /// The parser knows this exactly — it is <c>EndPosition</c> on the very tree-sitter node <see cref="Line"/>
    /// comes from. Carrying it means a consumer never has to re-derive where a member stops by counting
    /// braces, which is a small C# parser sitting next to a real one and wrong in all the ways you would
    /// expect (raw string literals, property initialisers after an accessor list).
    /// </para></summary>
    public int EndLine { get; init; }
}

/// <summary>A declared type and its members. <see cref="AstPath"/> encodes nesting (e.g. <c>T:Outer/T:Inner</c>).
/// <see cref="Line"/> is 1-based.</summary>
public sealed record OutlineType(string Name, int Line, OutlineKind Kind, string AstPath, IReadOnlyList<OutlineMember> Members)
{
    /// <summary>The type's parent class(es) and implemented interfaces, for drawing inheritance edges.</summary>
    public IReadOnlyList<BaseRef> Bases { get; init; } = [];

    /// <summary>The type's declared access level. Defaults to public because most languages here have no
    /// meaningful type-level modifier; the C# extractor sets it from the real modifiers, defaulting the way
    /// the language does (a top-level type is internal, a nested one private).</summary>
    public OutlineVisibility Visibility { get; init; } = OutlineVisibility.Public;

    /// <summary>1-based line the type ends on, or 0 when the extractor didn't record one. See
    /// <see cref="OutlineMember.EndLine"/>.</summary>
    public int EndLine { get; init; }
}

/// <summary>A region of the file written in another language (the JavaScript inside an HTML
/// <c>&lt;script&gt;</c>, the Ruby inside an ERB block, …), with its own structural outline. Rendered as a
/// linked sub-graph in the class diagram. <see cref="Language"/> is the embedded grammar id, <see cref="HostLine"/>
/// the 1-based line where the region begins (so the sub-graph heading can link back to it), and
/// <see cref="Outline"/> the embedded structure — its line numbers already mapped to absolute document lines and
/// its AST paths namespaced so they stay unique within the host.</summary>
public sealed record EmbeddedOutline(string Language, string HostLabel, int HostLine, CodeOutline Outline);

/// <summary>The structural outline of one source file: its imports, declared types (with members), any
/// top-level members (free functions), and any embedded-language regions. Produced by
/// <see cref="CodeStructureExtractor"/>.</summary>
public sealed record CodeOutline(
    IReadOnlyList<ImportRef> Imports,
    IReadOnlyList<OutlineType> Types,
    IReadOnlyList<OutlineMember> TopLevel)
{
    public static readonly CodeOutline Empty = new([], [], []);

    /// <summary>Embedded-language regions (HTML's script/style, ERB's Ruby, …), each a linked sub-graph.</summary>
    public IReadOnlyList<EmbeddedOutline> Embedded { get; init; } = [];

    /// <summary>
    /// The parser could not make a tree of this file at all — its <b>root</b> came back as an error node, so
    /// the declarations below are whatever survived recovery rather than what the file contains.
    /// <para>
    /// This exists because the failure is otherwise invisible and indistinguishable from an empty file. A
    /// grammar that predated one C# feature turned a single slice pattern into a root-level error, and a
    /// 1,900-line file with ninety methods silently contributed <i>one</i> type to the knowledge graph for
    /// months. Nothing was wrong-looking anywhere: no exception, no warning, just absence. A caller that
    /// cares about completeness — an indexer, a guard test — must be able to tell "nothing here" from
    /// "nothing readable here", which is the whole point of surfacing it.
    /// </para>
    /// </summary>
    public bool ParseFailed { get; init; }

    public bool HasContent =>
        Imports.Count > 0 || Types.Count > 0 || TopLevel.Count > 0 || Embedded.Count > 0;
}
