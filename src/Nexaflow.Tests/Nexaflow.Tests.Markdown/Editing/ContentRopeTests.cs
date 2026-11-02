using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// A long document held as a rope: every question asked of it — its characters, a stretch of them, its lines, where a line starts,
/// its line ending, what an edit makes of it — answered as the same characters held as one string answer it, through every edit
/// and across the lengths where it switches between the two; and an edit costing the nodes it writes through, not the document.
/// </summary>
[TestClass]
[CoversNode("markdown-long-writing")]
public class ContentRopeTests
{
    /// <summary>What the random documents are written in: line breaks of every kind, so plenty of them fall where one leaf meets the next.</summary>
    private const string Letters = "ab c\r\n\r\n#\t\n";

    [TestMethod]
    public void EveryQuestionIsAnsweredAsTheSameCharactersInOneStringAnswerIt_ThroughEveryEdit()
    {
        var random = new Random(4417);
        var expected = Written(random, 300 * 1024);
        var document = ContentDocument.Of(expected);
        var roped = true;

        Assert.IsTrue(document.Roped, "a document past where it switches is held as a rope");

        for (var step = 0; step < 600; step++)
        {
            var (start, length, text) = Edit(random, expected, step);
            var before = document;

            document = document.Apply(ContentChange.Write(start, length, text));
            expected = string.Concat(expected.AsSpan(0, start), text, expected.AsSpan(start + length));

            roped = roped ? expected.Length >= ContentDocument.FlatBelow : expected.Length >= ContentDocument.RopeFrom;
            Assert.AreEqual(roped, document.Roped, $"step {step}: held the way its length says, {expected.Length} characters");

            Same(expected, document, random, $"step {step}");

            if (step % 25 == 0)
                Assert.AreEqual(before.ToString(), document.Apply(document.Inverse!).ToString(), $"step {step}: the inverse takes it back");
        }
    }

    [TestMethod]
    public void WritingACharacterAtATimeLeavesLeavesOfThousands_InAShallowTree()
    {
        var random = new Random(91);
        var document = ContentDocument.Of(Written(random, 1024 * 1024));

        for (var step = 0; step < 5000; step++)
            document = document.Apply(ContentChange.Write(random.Next(document.Length + 1), 0, "x"));

        var leaves = document.Chunks().Count();

        Assert.IsTrue(leaves <= 2 * document.Length / 1024, $"{leaves} leaves hold {document.Length} characters: typing left fragments behind");
        Assert.IsTrue(document.Rope!.Height <= 4, $"{document.Rope.Height} levels deep for {leaves} leaves");
    }

    [TestMethod]
    public void AKeystrokeMakesOnlyTheLeavesItWritesIn_EveryOtherIsTheVersionBeforesOwn()
    {
        var document = ContentDocument.Of(Written(new Random(7), 1024 * 1024));
        var typed = document.Apply(ContentChange.Write(document.Length / 2, 0, "x"));

        var was = new HashSet<string>(Strings(document), ReferenceEqualityComparer.Instance);
        var made = Strings(typed).Count(text => !was.Contains(text));

        Assert.IsTrue(made <= 3, $"{made} leaves made for one keystroke");
    }

    [TestMethod]
    public void WhatIsSearchedForIsFoundWhereOneLeafMeetsTheNext_AsWellAsInsideOne()
    {
        var text = string.Concat(Enumerable.Range(0, 40_000).Select(at => (char)('a' + (at * 7 % 26))));
        var document = ContentDocument.Of(text);

        foreach (var at in Enumerable.Range(0, 200).Select(at => at * 199 % (text.Length - 16)))
        {
            var wanted = text.Substring(at, 13);

            Assert.AreEqual(text.IndexOf(wanted, StringComparison.Ordinal), document.IndexOf(wanted), $"'{wanted}' from {at}");
            Assert.AreEqual(text.IndexOf(wanted, at, StringComparison.Ordinal), document.IndexOf(wanted, at), $"'{wanted}' searched from {at}");
        }

        Assert.AreEqual(-1, document.IndexOf("zz"));
        Assert.AreEqual(text.IndexOf('q', 31_000), document.IndexOf('q', 31_000));
    }

    [TestMethod]
    public void TheLineEndingIsSaidByTheLinesNotFoundBySearching()
    {
        var unix = ContentDocument.Of(string.Concat(Enumerable.Repeat("a line\n", 30_000)));
        Assert.IsTrue(unix.Roped);
        Assert.AreEqual("\n", unix.LineEnding);

        var mixed = unix.Apply(ContentChange.Write(100_000, 0, "\r\n"));
        Assert.AreEqual("\r\n", mixed.LineEnding);
        Assert.AreEqual("\n", mixed.Apply(mixed.Inverse!).LineEnding);
        Assert.AreEqual("\r\n", ContentDocument.Of("short\r\nnote").LineEnding);
    }

    [TestMethod]
    public void TheSameCharactersHeldEitherWayAreEqual_AndHashAlike()
    {
        var text = Written(new Random(3), 200 * 1024);
        var roped = ContentDocument.Of(text).Apply(ContentChange.Write(100 * 1024, 100 * 1024, string.Empty));
        var flat = ContentDocument.Of(text[..(100 * 1024)]);

        Assert.IsTrue(roped.Roped, "still a rope above where it goes back to one string");
        Assert.IsFalse(flat.Roped);
        Assert.AreEqual(flat, roped);
        Assert.AreEqual(flat.GetHashCode(), roped.GetHashCode());
    }

    /// <summary>Holds every answer <paramref name="document"/> gives to what <paramref name="expected"/> says.</summary>
    private static void Same(string expected, ContentDocument document, Random random, string when)
    {
        Assert.AreEqual(expected.Length, document.Length, when);
        Assert.AreEqual(expected, document.ToString(), when);
        Assert.AreEqual(expected, string.Concat(document.Chunks().Select(chunk => chunk.ToString())), $"{when}: its chunks");

        var starts = Starts(expected);
        Assert.AreEqual(starts.Count, document.LineCount, $"{when}: lines");
        Assert.AreEqual(expected.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", document.LineEnding, $"{when}: line ending");

        for (var ask = 0; ask < 20; ask++)
        {
            var at = random.Next(expected.Length + 1);
            var found = starts.BinarySearch(at);
            var line = found >= 0 ? found : ~found - 1;
            Assert.AreEqual(line, document.LineOf(at), $"{when}: the line {at} is on");

            var asked = random.Next(starts.Count);
            Assert.AreEqual(starts[asked], document.OffsetOf(asked), $"{when}: where line {asked} starts");

            var length = Math.Min(random.Next(9000), expected.Length - Math.Min(at, expected.Length));
            Assert.AreEqual(expected.Substring(at, length), document.Slice(at, length), $"{when}: {length} from {at}");

            if (at < expected.Length) Assert.AreEqual(expected[at], document[at], $"{when}: the character at {at}");
        }
    }

    /// <summary>A random edit: mostly typing and taking out a little, sometimes a stretch pasted or cut, so the length crosses where it switches.</summary>
    private static (int Start, int Length, string Text) Edit(Random random, string text, int step)
    {
        var start = random.Next(text.Length + 1);
        var room = text.Length - start;

        return (step % 50) switch
        {
            10 when text.Length > 100 * 1024 => (Math.Min(start, text.Length - 90 * 1024), 90 * 1024, string.Empty),
            30 => (start, 0, Written(random, 80 * 1024)),
            _ when step % 3 == 0 => (start, Math.Min(random.Next(40), room), string.Empty),
            _ when step % 7 == 0 => (start, Math.Min(random.Next(9000), room), Written(random, random.Next(9000))),
            _ => (start, 0, Written(random, random.Next(1, 4))),
        };
    }

    /// <summary><paramref name="length"/> characters of <see cref="Letters"/> at random.</summary>
    private static string Written(Random random, int length)
    {
        var text = new StringBuilder(length);
        for (var at = 0; at < length; at++) text.Append(Letters[random.Next(Letters.Length)]);

        return text.ToString();
    }

    /// <summary>Where each line of <paramref name="text"/> starts, a line break being <c>\n</c>, <c>\r\n</c> or <c>\r</c> alone.</summary>
    private static List<int> Starts(string text)
    {
        var starts = new List<int> { 0 };

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n') continue;
            if (text[at] is '\n' or '\r') starts.Add(at + 1);
        }

        return starts;
    }

    /// <summary>The strings <paramref name="document"/>'s leaves hold.</summary>
    private static IEnumerable<string> Strings(ContentDocument document) =>
        document.Chunks().Select(chunk => MemoryMarshal.TryGetString(chunk, out var text, out _, out _) ? text : throw new AssertFailedException("a chunk not cut from a leaf's string"));
}
