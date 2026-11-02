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
/// <strong>Only the address.</strong> A step is a kind and either a name or a place in the order. A step naming
/// a heading names it as the reading's outline does (<see cref="ContentOutline"/>), and the steps after it are
/// looked for in its section; any other is the part of its kind so named, or at that place, inside the last —
/// so <c>table/row#1/cell#2</c> is found the way a heading's section is. Which of those a path leads to only
/// whatever holds the outline can say, so a path is read and written here and followed there; a place a list
/// already holds — an outline's heading (<see cref="OutlineHeading"/>) — is handed over as itself, never as a path.
/// </para>
/// <para>
/// Written the way a snaplink names a declaration — <c>T:Thing/M:Run#1</c> — because it is the same
/// question asked of a different tree, and a reader who can read one should not have to learn another.
/// </para>
/// <para>
/// A path that no longer leads all the way is followed as far as it does, which is what a reader wants: a
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

    /// <inheritdoc/>
    public override string ToString() =>
        string.Join(Divider, Steps.Select(step => step.ToString()));
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
