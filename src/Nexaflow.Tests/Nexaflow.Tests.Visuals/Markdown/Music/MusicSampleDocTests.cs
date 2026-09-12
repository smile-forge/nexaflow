using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.LilyPond;

namespace Nexaflow.Tests.Visuals.Markdown.Music;

/// <summary>
/// Holds the two sample documents to what they claim. Each is a showcase, so a block that quietly fails to
/// engrave is worse than a bug — it is a lie in the documentation, and nothing else in the suite would catch it.
///
/// The <em>features</em> section of each doc is held to the stricter bar: nothing it shows may be marked as
/// something the reader could not read or the engraver could not draw. The <em>songs</em> section is allowed
/// that, because one of the songs is there precisely to show what happens when a real-world file contains
/// things the engraver has to skip.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-notation")]
public class MusicSampleDocTests
{
    [TestMethod]
    [CoversNode("ly-core")]
    public void EveryLilyPondSampleBlock_Engraves() => UiThread.Run(() =>
        AssertDoc("music-lilypond.md", ly => LilyPondBuilder.Build(ly, 900, Brushes.Black, 1.0)));

    [TestMethod]
    public void EveryAbcSampleBlock_Engraves() => UiThread.Run(() =>
        AssertDoc("music-abc.md", abc => AbcBuilder.Build(abc, 900, Brushes.Black, 1.0)));

    private static void AssertDoc(string file, Func<string, Laid> build)
    {
        var blocks = Blocks(File.ReadAllText(Path.Combine(TestSampleData.Root, "markdown", file)));
        Assert.IsTrue(blocks.Count > 10, $"{file} should showcase the notation, not sample it ({blocks.Count} blocks)");

        var broken = new List<string>();

        foreach (var (source, features) in blocks)
        {
            var title = First(source);
            var layout = build(source);

            if (!layout.Root.SelfAndDescendants().Any(p => p.Kind == "system"))
                broken.Add($"{title}: engraved no staff");

            if (features && layout.Trouble.Count > 0)
                broken.Add($"{title}: the features section may not be marked — {string.Join("; ", layout.Trouble.Select(t => t.Message))}");
        }

        if (broken.Count > 0)
            Assert.Fail($"{file}:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", broken));
    }

    /// <summary>Every <c>#% … #%</c> block and every fenced block in a sample doc, flagged with whether it sits
    /// above the "## songs" heading (and so is part of the coverage showcase rather than a real-world tune).</summary>
    private static List<(string Source, bool Features)> Blocks(string markdown)
    {
        var blocks = new List<(string, bool)>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        bool features = true;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## songs", StringComparison.OrdinalIgnoreCase)) features = false;

            // Either fence: the older `#%lilypond … #%`, or a ```abc code fence. A line that is only the
            // closing mark is a closer with no opener above it, and is skipped.
            var opener = lines[i].Trim();
            var close = opener.StartsWith("#%", StringComparison.Ordinal) && opener != "#%" ? "#%"
                      : opener.StartsWith("```", StringComparison.Ordinal) && opener != "```" ? "```"
                      : null;
            if (close is null) continue;

            int end = i + 1;
            while (end < lines.Length && lines[end].Trim() != close) end++;
            blocks.Add((string.Join("\n", lines[(i + 1)..end]), features));
            i = end;
        }
        return blocks;
    }

    /// <summary>A label for the failure message: the block's title line, or its first line of source.</summary>
    private static string First(string source)
    {
        foreach (string line in source.Split('\n'))
        {
            string t = line.Trim();
            if (t.StartsWith("T:", StringComparison.Ordinal)) return t[2..].Trim();
            if (t.StartsWith("title =", StringComparison.Ordinal)) return t[7..].Trim().Trim('"');
        }
        return source.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "(empty)";
    }
}
