using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Plot;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Languages;
using Nexaflow.Visuals.Text.Markdown.Music;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Every language a fence can be written in — the one table, and the only one.
///
/// <para>
/// A language is a name, a parser, a pipeline, a builder and the rules for writing into the result
/// (<see cref="IContentLanguage"/>), and whatever holds one only has to know it found one. That is true
/// wherever a fence appears: a block of a document, a label on a flowchart node, a caption under a picture.
/// One table answers all of them, so a language added once is drawn everywhere.
/// </para>
/// <para>
/// The ones that ship are registered here. A feature brings its own by implementing
/// <see cref="IContentLanguage"/>, which is found at assembly load like every other contributed contract and
/// registered through <see cref="Register"/> — so a feature that wants its own notation drawn in markdown
/// writes a parser and a builder and nothing else.
/// </para>
/// </summary>
public static class ContentLanguages
{
    private static readonly List<IContentLanguage> Known =
    [
        new MermaidLanguage(),
        new NomnomlLanguage(),
        new QrLanguage(),
        new BarcodeLanguage(),
        new MusicLanguage(MusicDialect.Abc),
        new MusicLanguage(MusicDialect.LilyPond),
        new DataMatrixLanguage(),
        new Pdf417Language(),
        new AztecLanguage(),
        new SmilesLanguage(),
        new LatexLanguage(),
        new WordCloudLanguage(),
        new PlotLanguage(PlotFence.Scatter),
        new PlotLanguage(PlotFence.Bubble),
        new PlotLanguage(PlotFence.Heatmap),
        new PlotLanguage(PlotFence.Density2d),
    ];

    private static readonly Lock Adding = new();

    /// <summary>
    /// Adds a language, or replaces one already registered under a name it answers to.
    ///
    /// <para>
    /// Last in wins, which is what lets a host override a language that ships — and means a feature
    /// registering one nobody else claims is simply added.
    /// </para>
    /// </summary>
    public static void Register(IContentLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);

        lock (Adding)
        {
            Known.Insert(0, language);
        }
    }

    /// <summary>Whether anything here draws a fence calling itself <paramref name="language"/>.</summary>
    public static bool Reads(string? language) => For(language) is not null;

    /// <summary>What draws it, or null where nothing does.</summary>
    public static IContentLanguage? For(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;

        lock (Adding)
        {
            return Known.FirstOrDefault(known => known.Reads(language));
        }
    }
}
