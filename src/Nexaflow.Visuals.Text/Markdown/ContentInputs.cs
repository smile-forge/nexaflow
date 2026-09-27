using System;
using System.Windows.Media;

using Nexaflow.Markdown.Binding;
using Nexaflow.Visuals.Text.Markdown.Stages;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What the host showing some content says about it that the source does not, and that what is laid out depends on — what the
/// engine is told, and what a language's stages read.
/// </summary>
/// <param name="Pictures">
/// Where a picture the content names is found — an <c>![](…)</c>, a word cloud's <c>mask:</c>, a diagram's <c>img:</c>. The host's
/// own resolver first, then the document's folder, so a document that does not live on disk brings its pictures the way it
/// brings the rest of them, and a block can reach nowhere an image could not. Null where there is nothing to resolve against, and
/// a picture named is then one that could not be found.
/// </param>
/// <param name="Links">
/// The host's say in how a link looks, asked for every link with where it points and the words it was written as — the help pane
/// marks a <c>locate:</c> link this way without disturbing those words. Null where the host has nothing to say, which is most.
/// </param>
/// <param name="Data">
/// What a <c>{{…}}</c> written in a diagram is read against — the host's own object, asked by path. Null leaves a binding drawn as
/// the characters it was written with, so a document nobody has bound to still reads.
/// </param>
public sealed record ContentInputs(
    Func<string, ImageSource?>? Pictures = null,
    Func<string, string, LinkLook?>? Links = null,
    IDataContext? Data = null)
{
    /// <summary>A host with nothing to say.</summary>
    public static ContentInputs None { get; } = new();
}
