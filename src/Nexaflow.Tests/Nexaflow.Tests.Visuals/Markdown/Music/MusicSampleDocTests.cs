using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Parsers;

namespace Nexaflow.Tests.Visuals.Markdown.Music;

/// <summary>
/// Holds the two sample documents to what they claim. Each is a showcase, so a block that quietly fails to engrave
/// is worse than a bug — it is a lie in the documentation, and nothing else in the suite would catch it.
///
/// The <em>features</em> section of each doc is held to the stricter bar: every construct it shows must engrave with
/// <em>no warnings at all</em>. A warning means the sample is advertising something the parser cannot actually do.
/// The <em>songs</em> section is allowed them, because one of the songs is there precisely to show what happens when
/// a real-world file contains things the engraver has to skip.
/// </summary>
[TestClass]
[CoversNode("abc-notation")]
public class MusicSampleDocTests
{
    [TestMethod]
    [CoversNode("ly-core")]
    public void EveryLilyPondSampleBlock_Engraves()
    {
        AssertDoc("music-lilypond.md", src => new LilyPondParser().Parse(src));
    }

    [TestMethod]
    public void EveryAbcSampleBlock_Engraves()
    {
        AssertDoc("music-abc.md", src => new AbcParser().Parse(src));
    }

    private static void AssertDoc(string file, Func<string, Score> parse)
    {
        var blocks = Blocks(File.ReadAllText(Path.Combine(TestSampleData.Root, "markdown", file)));
        Assert.IsTrue(blocks.Count > 10, $"{file} should showcase the notation, not sample it ({blocks.Count} blocks)");

        var broken = new List<string>();

        foreach (var (source, features) in blocks)
        {
            string title = First(source);
            var score = parse(source);

            if (score.IsEmpty)
            {
                broken.Add($"{title}: engraved nothing");
                continue;
            }
            foreach (var staff in score.Staves)
                if (staff.Measures.Count == 0)
                    broken.Add($"{title}: a staff with no measures");

            if (features && score.Warnings.Count > 0)
                broken.Add($"{title}: the features section may not warn — {string.Join("; ", score.Warnings)}");
        }

        if (broken.Count > 0)
            Assert.Fail($"{file}:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", broken));
    }

    /// <summary>Every <c>#% … #%</c> block in a sample doc, flagged with whether it sits above the "## songs"
    /// heading (and so is part of the coverage showcase rather than a real-world tune).</summary>
    private static List<(string Source, bool Features)> Blocks(string markdown)
    {
        var blocks = new List<(string, bool)>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        bool features = true;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## songs", StringComparison.OrdinalIgnoreCase)) features = false;
            if (!lines[i].StartsWith("#%", StringComparison.Ordinal)) continue;
            if (lines[i].Trim() == "#%" ) continue;                       // a closing fence with no opener above it

            int end = i + 1;
            while (end < lines.Length && lines[end].Trim() != "#%") end++;
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
