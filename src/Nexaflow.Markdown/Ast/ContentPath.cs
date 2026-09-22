using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexaflow.Markdown.Ast;

/// <summary>
/// Where something is in a document, said in what the document is made of rather than in where it happens
/// to sit — <c>heading:getting-started/list/item#2</c>.
///
/// <para>
/// <strong>A reference that survives editing.</strong> An offset and a line number both say where something
/// is today and neither says anything about what it is, so both go stale the moment a line is added above.
/// What does not go stale is the shape: the second item of the list under the heading called Getting Started
/// is still that, however much has been written around it.
/// </para>
/// <para>
/// <strong>One walk serves every language.</strong> A step is a kind and either a name or a place in the
/// order, and every tree here is made of kinds with names — so <c>table/row#1/cell#2</c> and
/// <c>pie/slice:Chrome</c> are found by the same code that finds a heading, and a language only has to say
/// anything if its idea of a step is different. That is the point of the kinds being open strings.
/// </para>
/// <para>
/// Written the way a snaplink names a declaration — <c>T:Thing/M:Run#1</c> — because it is the same
/// question asked of a different tree, and a reader who can read one should not have to learn another.
/// </para>
/// <para>
/// A path that no longer leads anywhere gives back the furthest it got, which is what a reader wants: a
/// deep link into a section somebody has reorganised should still land in the section.
/// </para>
/// </summary>
public sealed record ContentPath(IReadOnlyList<ContentStep> Steps)
{
    /// <summary>What separates one step from the next.</summary>
    public const char Divider = '/';

    /// <summary>The path that goes nowhere.</summary>
    public static ContentPath Nowhere { get; } = new([]);

    /// <summary>Reads a path back from how it was written down.</summary>
    public static ContentPath Read(string? written)
    {
        if (string.IsNullOrWhiteSpace(written)) return Nowhere;

        var steps = written.Split(Divider, StringSplitOptions.RemoveEmptyEntries)
                           .Select(ContentStep.Read)
                           .OfType<ContentStep>()
                           .ToList();

        return steps.Count == 0 ? Nowhere : new ContentPath(steps);
    }

    /// <summary>
    /// How to find <paramref name="part"/> again: the kinds it sits inside, from the root down, each said
    /// by its name where it has one and by its place in the order where it does not.
    /// </summary>
    public static ContentPath Of(ContentPart? part)
    {
        if (part is null) return Nowhere;

        var steps = new List<ContentStep>();

        for (var at = part; at?.Parent is not null; at = at.Parent)
        {
            // A wrapper holds no name of its own and is not something anybody would point at — the body of
            // a block, the sequence its children sit in — so a path goes straight through it.
            if (at.Role is Roles.Body or Roles.Trivia || at.Kind == Kinds.Sequence || at.Derived) continue;

            steps.Add(new ContentStep(at.Kind, Named(at), Placed(at)));
        }

        steps.Reverse();

        return steps.Count == 0 ? Nowhere : new ContentPath(steps);
    }

    /// <summary>
    /// What this path leads to in <paramref name="root"/> — or, where it no longer leads all the way, the
    /// furthest step that still does.
    /// </summary>
    public ContentPart? In(ContentPart? root)
    {
        var at = root;

        foreach (var step in Steps)
        {
            if (at is null) break;
            if (Took(at, step) is not { } next) return at == root ? null : at;

            at = next;
        }

        return ReferenceEquals(at, root) && Steps.Count > 0 ? null : at;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Join(Divider, Steps.Select(step => step.ToString()));

    /// <summary>The child a step names, or null where nothing answers to it any more.</summary>
    private static ContentPart? Took(ContentPart at, ContentStep step)
    {
        var seen = 0;

        foreach (var child in Inside(at))
        {
            if (child.Kind != step.Kind) continue;

            if (step.Name is { Length: > 0 } wanted)
            {
                if (string.Equals(Named(child), wanted, StringComparison.OrdinalIgnoreCase)) return child;

                continue;
            }

            if (seen++ == step.Index) return child;
        }

        return null;
    }

    /// <summary>
    /// What a step may name inside a part: its own children, and the children of the wrappers a path goes
    /// through — so the path says what the document is made of and not how the reading happens to nest it.
    /// </summary>
    private static IEnumerable<ContentPart> Inside(ContentPart at)
    {
        foreach (var child in at.Children)
        {
            if (child.Derived || child.Role == Roles.Trivia) continue;

            if (child.Role == Roles.Body || child.Kind == Kinds.Sequence)
            {
                foreach (var inner in Inside(child)) yield return inner;

                continue;
            }

            yield return child;
        }
    }

    /// <summary>
    /// What a part is called: what it was written as, where it says so, and otherwise the name a stage
    /// worked out for it — which for a heading is the one a link already points at.
    /// </summary>
    private static string? Named(ContentPart part)
    {
        if (part.Part(Roles.Name) is { Text.Length: > 0 } written) return written.Text.Trim();

        foreach (var child in part.Children)
            foreach (var held in child.Children)
                if (held.Node.Held is string said && said.Length > 0)
                    return said;

        return null;
    }

    /// <summary>Which of its kind a part is, among the things beside it.</summary>
    private static int Placed(ContentPart part)
    {
        if (part.Parent is not { } holder) return 0;

        var seen = 0;

        foreach (var beside in Inside(holder))
        {
            if (ReferenceEquals(beside, part)) return seen;
            if (beside.Kind == part.Kind) seen++;
        }

        return seen;
    }
}

/// <summary>
/// One step of a path: a kind, and either the name of the thing or where it stands among the things of its
/// kind beside it.
/// </summary>
/// <param name="Kind">What sort of thing it is.</param>
/// <param name="Name">What it is called, where it is called anything.</param>
/// <param name="Index">Which of its kind it is, used only where it has no name.</param>
public sealed record ContentStep(string Kind, string? Name = null, int Index = 0)
{
    /// <summary>
    /// Reads one step back from how it was written down — the same shape a snaplink names a declaration
    /// with: what sort of thing it is, then either its name after a colon or its place after a hash.
    /// </summary>
    public static ContentStep? Read(string written)
    {
        var said = written.Trim();
        if (said.Length == 0) return null;

        var named = said.IndexOf(':');
        if (named > 0) return new ContentStep(said[..named], Unescaped(said[(named + 1)..]));

        var placed = said.IndexOf('#');

        if (placed > 0 && int.TryParse(said[(placed + 1)..], out var index))
            return new ContentStep(said[..placed], null, Math.Max(index, 0));

        return new ContentStep(said);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        Name is { Length: > 0 } named ? $"{Kind}:{Escaped(named)}"
        : Index > 0 ? $"{Kind}#{Index}"
        : Kind;

    /// <summary>A name with the characters a path is written with put beyond use in it.</summary>
    private static string Escaped(string name)
    {
        var said = new StringBuilder(name.Length);

        foreach (var letter in name)
        {
            if (letter is ContentPath.Divider or ':' or '#' or '%') said.Append('%').Append(((int)letter).ToString("x2"));
            else said.Append(letter);
        }

        return said.ToString();
    }

    private static string Unescaped(string name)
    {
        if (!name.Contains('%')) return name;

        var said = new StringBuilder(name.Length);

        for (var at = 0; at < name.Length; at++)
        {
            if (name[at] == '%' && at + 2 < name.Length
                && int.TryParse(name.AsSpan(at + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var code))
            {
                said.Append((char)code);
                at += 2;

                continue;
            }

            said.Append(name[at]);
        }

        return said.ToString();
    }
}
