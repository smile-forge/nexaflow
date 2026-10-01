using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Whatever anybody types where something is written in a diagram, the diagram still reads: every diagram in the samples, laid out
/// to be written in, typed into at the end of every run of words and in every hole it draws, through the engine — so what is checked
/// is what the diagram's own edit handler makes of the key (<see cref="DiagramEdits"/>), made as the engine makes it.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("mermaid-diagram-kit")]
public class DiagramEscapingTests
{
    /// <summary>What anybody might type into a place: the characters a diagram's syntax gives a meaning to, and space.</summary>
    private const string Typed = "\"\\ []() {}%,:;#|->";

    [TestMethod]
    public void WhateverIsTypedWhereSomethingIsWrittenTheDiagramStillReads() => UiThread.Run(() =>
    {
        var diagrams = 0;
        var broken = new List<string>();

        foreach (var path in TestSampleData.Files("markdown").Where(path => Path.GetFileName(path).StartsWith("mermaid-", StringComparison.Ordinal)))
        {
            var document = File.ReadAllText(path).Replace("\r\n", "\n");

            foreach (var holder in new ContentEngine().Read(null, document).Root.SelfAndDescendants().Where(part => ContentNested.Language(part) == "mermaid"))
            {
                Typing($"{Path.GetFileName(path)} @{holder.Start}", ContentNested.Own(holder.Part(Roles.Body)!.Node), broken);
                diagrams++;
            }
        }

        Assert.IsTrue(diagrams > 0, "the samples hold diagrams to type into");
        Assert.AreEqual(0, broken.Count, $"{broken.Count} key(s) typed where something is written leave a line that no longer reads — escape them in the diagram's edit handler:\n"
                                         + string.Join("\n", broken.Take(40)));
    });

    /// <summary>Every character typed at every place <paramref name="source"/> draws something written, one at a time; each that leaves a line unread is told to <paramref name="broken"/>.</summary>
    private static void Typing(string what, string source, List<string> broken)
    {
        var laid = new ContentEngine().Lay("mermaid", EditState.For(source), StyleFormat.Dark, 700, readOnly: false);
        var held = Held(source);

        var places = laid.Root.SelfAndDescendants()
            .Select(piece => piece.Part)
            .OfType<ContentPart>()
            .Where(part => part.Kind is Kinds.Words or Kinds.Hole)
            .Distinct();

        foreach (var place in places)
            foreach (var character in Typed)
            {
                var at = new EditState(source, place.End);
                var text = character.ToString();
                var written = ContentEngine.Edited(EditKind.Typing, text, new Landing(at, laid, -1)) ?? at.Write(text);

                if (Held(written.Source) > held)
                    broken.Add($"{what}: {text} typed after '{place.Text}' ({place.Parent?.Kind}/{place.Role}) — the line becomes '{Line(written.Source, place.End)}'");
            }
    }

    /// <summary>The line <paramref name="at"/> is on.</summary>
    private static string Line(string source, int at)
    {
        var start = source.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        var end = source.IndexOf('\n', at);
        return source[start..(end < 0 ? source.Length : end)].Trim();
    }

    /// <summary>How many lines of a block its grammar holds as written, rather than reading them.</summary>
    private static int Held(string source) =>
        MermaidParser.Parse(source).SelfAndDescendants().Count(node => node is { Kind: Kinds.Verbatim, Trouble: not null });
}
