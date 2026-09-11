using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown.Music;

/// <summary>
/// A score inside the <em>selectable</em> surface. The block renderer's path was covered; this one was not,
/// which is where a text-tree crash was able to hide: the score is an element embedded in the FlowDocument, and
/// the RichTextBox walks that tree on every caret move, selection and focus change.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]   // spins an off-screen Window; concurrent WPF layout and focus make it flaky
[CoversNode("music-block")]
public class MusicFlowDocumentTests
{
    /// <summary>
    /// A document of the older <c>#%abc … #%</c> blocks, which draw onto the same page a fenced block does: one tune
    /// with a title, one with notes, one with blank verse lines.
    /// </summary>
    private static string SampleDoc() =>
        """
        # Old-style ABC blocks

        Three tunes in the older fence, with words between them: a reel, a slip jig whose fields carry
        comments, and a lesson in lining words up under notes.

        #%abc
        X:1
        T:Speed the Plough
        M:4/4
        C:Trad.
        K:G
        |:GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|
          GABc dedB|dedB dedB|c2ec B2dB|A2F2 G4:|
        |:g2gf gdBd|g2f2 e2d2|c2ec B2dB|c2A2 A2df|
          g2gf g2Bd|g2f2 e2d2|c2ec B2dB|A2F2 G4:|
        #%

        #%abc
        X:1
        T:Old Sir Simon the King (commented)
        C:Trad.               % composer
        S:Offord MSS          % source
        N:see also Playford   % notes
        M:9/8                 % meter
        R:SJ                  % rhythm
        Q:1/4=160             % tempo
        Z:originally in C     % transcription notes
        K:G                   % key
        D|GFG GAG G2D|GFG GAG F2D|EFE EFE EFG|A2G F2E D2:|
        D|GAG GAB d2D|GAG GAB c2D|[1 EFE EFE EFG|A2G F2E D2:|
        M:12/8                % change meter for a bar
        [2 E2E EFE E2E EFG|\
        M:9/8                 % change back again
        A2G F2E D2|]
        #%

        #%abc
        X:1
        T:Lyrics
        N:see https://www.youtube.com/watch?v=RWNeCjid0zc
        M:4/4
        L:1/4
        K:C
        % use the w: field to add lyrics, with each word lined up on a note
        A A A A | A A A A |
        w:words line up on notes
        %
        % to align syllables on notes, use hyphens and/or spaces to split the words up
        A A A A | A A A A |
        w:syl-la-ble, syl- la- ble
        %
        % to align two (or more) syllables on a single note, don't split them up or use backslash hypen \-
        A2  A2 | A2 A2 | 
        w:syllable, syl\-la\-ble
        %
        % to align two (or more) words on a single note, use a tilde ~ between the words
        A4 | A A A A | 
        w:word~word syl-la-ble
        %
        % to align two (or more) notes on a syllable or word, use an underscore
        A2  A2 | A A A A  | 
        w:word_ syl-la-ble_
        %
        % to skip one (or more) notes, i.e. to include blank syllables, use an asterisk *
        A A A A | A A A A |
        w:word * * * syl-la-ble *
        %
        % to save typing in lots of asterisks, advance to the next barline with a bar symbol |
        A A A A | A A A A |
        w:word | syl-la-ble |
        %
        % to include multipe verses, use multiple w: fields
        A A A A | A A A A |
        w:syl-la-ble | syl- la- ble
        w:word | syl-la-ble 
        %
        % to include more verses underneath use W: fields (upper case)
        W: This is verse two of my song
        W: Syl-la-ble, word
        W: 
        W: This is verse three of my song
        W: Word, word, syl-la-ble!
        W: 
        %%writefields N
        #%

        """;

    /// <summary>Every pointer operation the RichTextBox performs on its own — walking the tree, selecting all
    /// of it, mapping offsets — over a document full of scores. A malformed text tree faults here.</summary>
    [TestMethod]
    public void TheWholeSampleDoc_SurvivesEveryTextPointerWalk() => UiThread.Run(() =>
    {
        var doc = MarkdownFlowDocument.Build(SampleDoc(), MarkdownPalette.Light);
        var rtb = new RichTextBox { Document = doc };
        var host = new Window { Content = rtb, Width = 900, Height = 600, ShowActivated = false };
        try
        {
            host.Show();
            rtb.UpdateLayout();

            // Select the lot, then step a caret through every position in the document.
            rtb.Selection.Select(doc.ContentStart, doc.ContentEnd);
            Assert.IsFalse(rtb.Selection.IsEmpty);

            int steps = 0;
            for (var p = doc.ContentStart; p is not null && p.CompareTo(doc.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward)!)
            {
                rtb.CaretPosition = p;
                p.GetCharacterRect(LogicalDirection.Forward);
                if (++steps > 20_000) break;
            }
            Assert.IsTrue(steps > 10, "the document should have real content to walk");
            Assert.AreEqual(3, doc.Blocks.OfType<BlockUIContainer>().Count(), "and every tune in it is an engraved score");

            // …and back by character offset, which is the path that faulted.
            for (int i = 0; i < 400; i++)
                doc.ContentStart.GetPositionAtOffset(i, LogicalDirection.Forward);
        }
        finally
        {
            host.Close();
        }
    });

    /// <summary>Focus round-trips through the score. Re-activating a window restores keyboard focus to whatever
    /// held it, and if that is an element embedded in the text tree the RichTextBox has to reconcile its caret
    /// with a position that isn't text — which is what faulted deep in the splay tree.</summary>
    [TestMethod]
    public void FocusRoundTrip_ThroughAnEmbeddedScore_DoesNotFaultTheTextTree() => UiThread.Run(() =>
    {
        var doc = MarkdownFlowDocument.Build(SampleDoc(), MarkdownPalette.Light);
        var rtb = new RichTextBox { Document = doc };
        var other = new Button { Content = "elsewhere" };
        var host = new Window
        {
            Content = new StackPanel { Children = { rtb, other } },
            Width = 900,
            Height = 600,
        };
        try
        {
            host.Show();
            rtb.UpdateLayout();

            var score = Descendants(rtb).OfType<Nexaflow.Visuals.Text.Editing.ContentElement>().First();
            Assert.IsFalse(score.Focusable,
                "an engraved score must not take keyboard focus: it lives inside the RichTextBox's text tree, " +
                "and focus landing on it makes the caret reconciliation walk a node that has no text");

            score.Focus();                       // a no-op now, but the point is that it stays a no-op
            other.Focus();
            rtb.Focus();

            _ = rtb.CaretPosition;
            rtb.Selection.Select(doc.ContentStart, doc.ContentEnd);
            _ = rtb.Selection.Text;
        }
        finally
        {
            host.Close();
        }
    });

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
