using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// Whether ten thousand real tunes <em>engrave</em>, which is a different question from whether they read.
///
/// <para>
/// The parse-level sweep in <c>Nexaflow.Tests.Markdown</c> asks whether the tree holds them, and it needs
/// no fonts and no desktop. This one asks whether anything comes out the other end: it runs the builder,
/// which means glyph outlines, so it needs an STA thread — and it is the only thing that can say a tune
/// nobody here wrote can actually be drawn.
/// </para>
/// <para>
/// <strong>It says nothing about whether the drawing is right.</strong> That is what a comparison against
/// the corpus's own reference pictures would answer, and there is not one yet. What this rules out is the
/// cheaper and more embarrassing failure: a tune that throws, or engraves to nothing, or draws a piece
/// that names a stretch of source it has not got.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
[DoNotParallelize]
public class AbcCorpusRenderTests
{
    private const string Default = @"D:\Datasets\abcmusic\zenoob";

    /// <summary>How many to engrave. The whole corpus takes minutes; a sample answers the same question.</summary>
    private const int Sample = 1500;

    [TestMethod]
    public void RealTunesEngraveToSomethingThatNamesRealSource() => UiThread.Run(() =>
    {
        if (Corpus() is not { } files) { Assert.Inconclusive(Missing); return; }

        var failures = new List<string>();
        var engraved = 0;
        var empty = 0;

        foreach (var file in files.Take(Sample))
        {
            var abc = Text(file);

            Laid layout;
            try
            {
                layout = AbcBuilder.Build(abc, 700, Brushes.Black, 1.0);
            }
            catch (Exception ex)
            {
                if (failures.Count < 10) failures.Add($"{Path.GetFileName(file)}: threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            engraved++;
            if (!layout.Root.Leaves().Any()) { empty++; continue; }

            // Every piece that names source must name source this tune actually has. The parse-level sweep
            // cannot see this: it is the builder's attribution that is being checked, not the reading.
            foreach (var node in layout.Root.SelfAndDescendants())
            {
                if (node.Part is null) continue;

                var at = node.Sits();
                if (at.Start >= 0 && at.End <= abc.Length) continue;

                if (failures.Count < 10)
                    failures.Add($"{Path.GetFileName(file)}: a {node.Kind} claims {at.Start}+{at.Length} of {abc.Length}");
                break;
            }
        }

        Assert.AreEqual(0, failures.Count,
            $"{failures.Count} of {engraved} engraved tunes went wrong:\n  {string.Join("\n  ", failures)}");

        // A tune with no music in it engraves to nothing quite legitimately — the corpus holds a few that
        // are all header — so this is a share rather than a count.
        Assert.IsTrue(empty < engraved / 20,
            $"{empty} of {engraved} engraved to nothing at all, which is too many to be tunes with no music");
    });

    // ── The corpus ──────────────────────────────────────────────────────────

    private const string Missing =
        "Point NEXAFLOW_ABC_CORPUS at a folder of .abc files to run this.";

    private static IReadOnlyList<string>? Corpus()
    {
        var root = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_CORPUS");
        if (string.IsNullOrWhiteSpace(root)) root = Default;
        if (!Directory.Exists(root)) return null;

        var files = Directory.EnumerateFiles(root, "*.abc", SearchOption.AllDirectories).ToList();
        return files.Count == 0 ? null : files;
    }

    private static string Text(string path) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(File.ReadAllBytes(path));
}
