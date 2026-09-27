using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What is picked out, said to whoever follows the content: the stretches of source, and each thing picked out — a word, a
/// note, a box in a flowchart — with what it stands for and the language it is written in.
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>Raised whenever what is picked out changes — to nothing, too, when the last of it is let go.</summary>
    public event EventHandler<ContentSelectionChange>? SelectionChanged;

    /// <summary>Tells whoever follows the content what is picked out now.</summary>
    private void Chosen()
    {
        if (SelectionChanged is not { } told) return;

        var ranges = _state.Selection.Select(range => (range.Start, range.Length)).ToList();
        var selection = ContentSelection.Over(_laid.Root, ranges);

        told(this, new ContentSelectionChange(selection, Picks(selection)));
    }

    /// <summary>Each thing picked out, in the order written: the whole pieces chosen, and the characters of a stretch chosen inside one.</summary>
    private List<ContentPick> Picks(ContentSelection selection)
    {
        var picks = new List<ContentPick>();

        foreach (var range in _state.Selection.Where(range => range.Length > 0))
        {
            var inside = selection.Pieces
                .Where(piece => piece.Sits() is var at && at.Start >= range.Start && at.End <= range.End)
                .ToList();

            if (inside.Count == 0)
            {
                var words = _laid.Root.WordsAt(range.Start);
                picks.Add(new ContentPick(range.Start, range.Length, _state.Source.Substring(range.Start, range.Length),
                                          words.Exists ? words.Naming() as ContentPart : null, LanguageOf(words), null, null));
                continue;
            }

            foreach (var piece in inside)
            {
                var at = piece.Sits();
                var (id, label) = Selecting(piece);

                picks.Add(new ContentPick(at.Start, at.Length, _state.Source.Substring(at.Start, at.Length),
                                          piece.Naming() as ContentPart, LanguageOf(piece), id, label));
            }
        }

        picks.AddRange(PickedPicks());
        return picks;
    }

    /// <summary>What the drawing says choosing <paramref name="piece"/> means — a diagram node's id and label — or nothing.</summary>
    private static (string? Id, string? Label) Selecting(Piece piece)
    {
        for (var at = piece; at.Exists; at = at.Parent)
            if (at.Acts?.Select is { } select) return (select.Target, select.Tip);

        return (null, null);
    }

    /// <summary>The language <paramref name="piece"/> was drawn in: the nearest piece holding content in another language on the way up, or what the content itself is written in.</summary>
    private string? LanguageOf(Piece piece)
    {
        for (var at = piece; at.Exists; at = at.Parent)
            if (at.Part is ContentPart part && ContentLanguages.Held(part) is not null)
                return ContentNested.Language(part);

        return _named;
    }
}

/// <summary>What is picked out in content now.</summary>
/// <param name="selection">The stretches of source picked out, and the pieces wholly inside them.</param>
/// <param name="picked">Each thing picked out.</param>
public sealed class ContentSelectionChange(ContentSelection selection, IReadOnlyList<ContentPick> picked) : EventArgs
{
    public ContentSelection Selection { get; } = selection;

    public IReadOnlyList<ContentPick> Picked { get; } = picked;

    /// <summary>Whether nothing is picked out.</summary>
    public bool IsEmpty => Picked.Count == 0;
}

/// <summary>One thing picked out: a word or a stretch of words, a note, a box in a flowchart.</summary>
/// <param name="Start">Where its characters start in the source.</param>
/// <param name="Length">How many characters it stands for.</param>
/// <param name="Text">Those characters.</param>
/// <param name="Part">The part of the reading it stands for, where it stands for one.</param>
/// <param name="Language">What it is written in — the word the content holding it was called by — or null for markdown.</param>
/// <param name="Id">What the drawing calls it where choosing it means something there — a diagram node's id — or null.</param>
/// <param name="Label">What the drawing says it is, alongside <paramref name="Id"/>.</param>
public sealed record ContentPick(int Start, int Length, string Text, ContentPart? Part, string? Language, string? Id, string? Label);
