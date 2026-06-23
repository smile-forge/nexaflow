namespace Nexaflow.Syntax;

/// <summary>What an outline element is. Type-like kinds (Class…Enum) describe a box in a class diagram;
/// member kinds (Method…Variable) describe a row inside one.</summary>
public enum OutlineKind { Class, Struct, Interface, Enum, Method, Constructor, Property, Field, Variable }

/// <summary>An import / include / require statement, with the local file it resolves to when the specifier
/// is relative (e.g. <c>./util</c>, <c>from .x import</c>). <see cref="ResolvedPath"/> is null for library /
/// namespace imports that don't map to a file (e.g. C# <c>using System.Text;</c>).</summary>
public sealed record ImportRef(string Text, string? ResolvedPath);

/// <summary>One member of a type — a method, property, field, etc. <see cref="AstPath"/> is a structure-keyed
/// identifier (see <see cref="CodeStructureExtractor"/>) used to re-find the member by name after edits, so a
/// link to it stays valid until the structure itself changes. <see cref="Line"/> is 1-based.</summary>
public sealed record OutlineMember(string Name, int Line, OutlineKind Kind, string Signature, string AstPath);

/// <summary>A declared type and its members. <see cref="AstPath"/> encodes nesting (e.g. <c>T:Outer/T:Inner</c>).
/// <see cref="Line"/> is 1-based.</summary>
public sealed record OutlineType(string Name, int Line, OutlineKind Kind, string AstPath, IReadOnlyList<OutlineMember> Members);

/// <summary>The structural outline of one source file: its imports, declared types (with members), and any
/// top-level members (free functions). Produced by <see cref="CodeStructureExtractor"/>.</summary>
public sealed record CodeOutline(
    IReadOnlyList<ImportRef> Imports,
    IReadOnlyList<OutlineType> Types,
    IReadOnlyList<OutlineMember> TopLevel)
{
    public static readonly CodeOutline Empty = new([], [], []);

    public bool HasContent => Imports.Count > 0 || Types.Count > 0 || TopLevel.Count > 0;
}
