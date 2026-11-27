using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a unit read says to the rest of its document, worked over by its language's stages on its own — with nothing a host said, which
/// no heading or definition turns on — whether or not it is laid: what its blocks say to the outline, and what it defines.
/// </summary>
/// <param name="Outline">What its blocks say to the outline.</param>
/// <param name="Reports">Whether a stage said what it defines — never, for content in a language none of whose stages look.</param>
/// <param name="Defined">What it defines, in the order written, where in the unit each stands.</param>
internal sealed record UnitFacts(OutlineUnit Outline, bool Reports, IReadOnlyList<LinkTarget> Defined)
{
    /// <summary>Whether <paramref name="other"/> says the same of the same unit.</summary>
    public bool Same(UnitFacts other) =>
        Reports == other.Reports && Outline.Blocks == other.Outline.Blocks && Outline.Entries.SequenceEqual(other.Outline.Entries)
        && Defined.SequenceEqual(other.Defined);

    /// <summary>What the units <paramref name="said"/> define means, compared by value: what each target means and is called, not where it stands.</summary>
    public static string Meant(IEnumerable<UnitFacts?> said) =>
        string.Join('\n', said.SelectMany(facts => facts?.Defined ?? []).Select(target => $"{target.Kind}\t{target.Name}\t{target.Meaning}\t{target.Title}"));
}
