using System;
using System.Collections.Generic;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// Which blocks of the document read exactly as they did the last time it was read — the reading made aware of the one
/// before it, so that what was laid for a block last time can be laid again without anybody working out what changed.
///
/// <para>
/// A block is the same when everything the stages before this one hung on it is the same as well as its characters: a
/// link whose definition was edited three paragraphs away, or a heading whose name moved because another heading was
/// added above it, reads as the same characters and is not the same block. So this is the last stage, and it asks of the
/// whole node.
/// </para>
/// <para>
/// What it hangs on the document is a <see cref="LaidBlocks"/>: each block of this reading with the place its layout is
/// kept — the place the same block had last time, or a new one. Where a block stands in the source is not part of the
/// question, because nothing in the tree says where anything stands: a paragraph after the edit is the same paragraph,
/// and what the builder kept for it only has to be moved along.
/// </para>
/// <para>
/// One of these reads one document, time after time; the host that keeps the document keeps it. Anything the characters
/// do not say — which nodes of a diagram are opened — is the host's to change, and the host says so with
/// <see cref="Forget"/>, after which nothing is the same as it was.
/// </para>
/// </summary>
public sealed class WithUnchanged : IAstStage
{
    /// <summary>The kind of the part the document's blocks are hung on under.</summary>
    public const string Kind = "markdown-unchanged";

    /// <summary>Every block the last reading had, by the shape of what it says, with what its layout was kept in.</summary>
    private Dictionary<int, List<(ContentNode Block, LaidBlock Laid)>> _before = [];

    /// <inheritdoc/>
    public string Name => "markdown:unchanged";

    /// <inheritdoc/>
    public ContentNode Run(ContentNode tree)
    {
        var now = new Dictionary<int, List<(ContentNode Block, LaidBlock Laid)>>(_before.Count);
        var laid = new LaidBlocks();

        foreach (var block in tree.Children)
        {
            if (block.IsDerived || block.Role == Roles.Trivia) continue;

            var written = Shape(block);
            var kept = Taken(written, block) ?? new LaidBlock();

            laid.Add(block, kept);

            if (!now.TryGetValue(written, out var alike)) now[written] = alike = [];
            alike.Add((block, kept));
        }

        _before = now;
        return tree.Holding(Kind, Roles.Derived, laid);
    }

    /// <summary>Says that something the characters do not say has changed, so no block is the one it was.</summary>
    public void Forget() => _before = [];

    /// <summary>What the last reading kept for a block written as this one is and reading as it does — each given out once.</summary>
    private LaidBlock? Taken(int written, ContentNode block)
    {
        if (!_before.TryGetValue(written, out var alike)) return null;

        for (var at = 0; at < alike.Count; at++)
        {
            if (!Same(alike[at].Block, block)) continue;

            var kept = alike[at].Laid;
            alike.RemoveAt(at);
            return kept;
        }

        return null;
    }

    /// <summary>
    /// A number for what a node says: its kinds, roles and characters, all the way down. Two nodes that say the same have the
    /// same one, so a block is only ever compared whole against the few it could be — worked out without printing it, since
    /// a document is every block of it on every keystroke.
    /// </summary>
    private static int Shape(ContentNode node)
    {
        var shape = HashCode.Combine(node.Kind, node.Role, node.Text);

        for (var at = 0; at < node.Children.Count; at++) shape = HashCode.Combine(shape, Shape(node.Children[at]));

        return shape;
    }

    /// <summary>
    /// Whether two nodes say the same: the same characters in the same shape, and the same things worked out about them.
    /// </summary>
    private static bool Same(ContentNode was, ContentNode now)
    {
        if (ReferenceEquals(was, now)) return true;

        if (was.Kind != now.Kind
            || was.Role != now.Role
            || was.Text != now.Text
            || was.Trouble != now.Trouble
            || !Equals(was.Held, now.Held)
            || was.Children.Count != now.Children.Count) return false;

        for (var at = 0; at < was.Children.Count; at++)
            if (!Same(was.Children[at], now.Children[at])) return false;

        return true;
    }
}
