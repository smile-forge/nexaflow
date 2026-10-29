using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Packet;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Packet;

/// <summary>
/// What a change to a packet's fields is written as: whatever is put in, taken out, made longer or carried elsewhere, every field
/// is numbered again from bit 0 and keeps its length and the form it is written in — and nothing but its numbers is written over.
/// </summary>
[TestClass]
[CoversNode("packet-writing")]
public class PacketWritingTests
{
    [TestMethod, TestCategory("Unit")]
    public void AFieldMadeLongerPushesTheFieldsAfterItOn_EachKeepingItsLength()
    {
        const string source = "packet\n0-7: \"A\"\n8-15: \"B\"\n16: \"C\"";

        Assert.AreEqual("packet\n0-15: \"A\"\n16-23: \"B\"\n24: \"C\"", Changed(source, root => PacketWriting.Resized(root, Field(root, 0), 16, 0)));
        Assert.AreEqual("packet\n0-3: \"A\"\n4-11: \"B\"\n12: \"C\"", Changed(source, root => PacketWriting.Resized(root, Field(root, 0), 4, 0)), "and shorter pulls them back");
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldKeepsTheFormItIsWrittenIn()
    {
        Assert.AreEqual("packet\n0-7: \"A\"\n8-11: \"B\"", Changed("packet\n0-7: \"A\"\n8: \"B\"", root => PacketWriting.Resized(root, Field(root, 1), 4, 0)),
                        "one bit made longer becomes a range");
        Assert.AreEqual("packet\n+4: \"A\"\n4-11: \"B\"", Changed("packet\n+8: \"A\"\n8-15: \"B\"", root => PacketWriting.Resized(root, Field(root, 0), 4, 0)),
                        "a count is given the new one");
        Assert.AreEqual("packet\n0 - 3 : \"A\" %% why\n+8: \"B\"\n12  -  19: \"C\"",
                        Changed("packet\n0 - 7 : \"A\" %% why\n+8: \"B\"\n16  -  23: \"C\"", root => PacketWriting.Resized(root, Field(root, 0), 4, 0)),
                        "and only its numbers are written over: its spacing, its comment, and a count no change of place touches");
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldTakenOutTakesItsLine_AndTheFieldsLeftAreNumberedOn()
    {
        const string source = "packet\n0-7: \"A\"\n8-15: \"B\"\n16-31: \"C\"";

        Assert.AreEqual("packet\n0-7: \"A\"\n8-23: \"C\"", Changed(source, root => PacketWriting.Removed(root, [Field(root, 1)])));
        Assert.AreEqual("packet\n0-7: \"A\"\n8-15: \"B\"", Changed(source, root => PacketWriting.Removed(root, [Field(root, 2)])),
                        "the last leaves no empty line behind");
        Assert.AreEqual("packet\n0-15: \"C\"", Changed(source, root => PacketWriting.Removed(root, [Field(root, 0), Field(root, 1)])));
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldPutInIsABitLong_WrittenAsTheOneBesideItIs_AndTheCaretIsInItsLabel()
    {
        const string source = "packet\n  0-7: \"A\"\n  8-15: \"B\"";

        var after = Change(source, root => PacketWriting.Inserted(root, Field(root, 0), after: true, "\n"));
        Assert.AreEqual("packet\n  0-7: \"A\"\n  8: \"\"\n  9-16: \"B\"", after.Source);
        Assert.AreEqual(after.Source.IndexOf("8: \"", StringComparison.Ordinal) + 4, after.Caret);

        Assert.AreEqual("packet\n  0: \"\"\n  1-8: \"A\"\n  9-16: \"B\"", Changed(source, root => PacketWriting.Inserted(root, Field(root, 0), after: false, "\n")));
        Assert.AreEqual("packet\n+8: \"A\"\n+1: \"\"\n+8: \"B\"", Changed("packet\n+8: \"A\"\n+8: \"B\"", root => PacketWriting.Inserted(root, Field(root, 0), after: true, "\n")),
                        "beside a field counting its bits, one counting its own");
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldAddedGoesAfterEverythingWritten_OrIsTheFirst()
    {
        Assert.AreEqual("packet\n0-7: \"A\"\n8: \"\"", Changed("packet\n0-7: \"A\"", root => PacketWriting.Appended(root, "\n")));
        Assert.AreEqual("packet\ntitle T\n0: \"\"", Changed("packet\ntitle T", root => PacketWriting.Appended(root, "\n")));
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldCarriedToAnotherTakesItsPlace_EveryFieldNumberedOnWithItsOwnLength()
    {
        const string source = "packet\n0-3: \"A\"\n4-11: \"B\" %% bee\n12-15: \"C\"";

        Assert.AreEqual("packet\n0-7: \"B\" %% bee\n8-11: \"C\"\n12-15: \"A\"", Changed(source, root => PacketWriting.Moved(root, [Field(root, 0)], Field(root, 2))!),
                        "after the one it is let go on, carrying its comment with it");
        Assert.AreEqual("packet\n0-3: \"C\"\n4-7: \"A\"\n8-15: \"B\" %% bee", Changed(source, root => PacketWriting.Moved(root, [Field(root, 2)], Field(root, 0))!),
                        "and before it, going the other way");
        var root = Root(source);
        Assert.IsNull(PacketWriting.Moved(root, [Field(root, 1)], Field(root, 1)), "let go on itself, nothing moves");
    }

    [TestMethod, TestCategory("Unit")]
    public void ANumberStillToWriteStaysStillToWrite()
    {
        Assert.AreEqual("packet\n0-3: \"A\"\n+: \"B\"\n5-12: \"C\"",
                        Changed("packet\n0-7: \"A\"\n+: \"B\"\n9-16: \"C\"", root => PacketWriting.Resized(root, Field(root, 0), 4, 0)),
                        "a count still to write is a bit wide, and stays to write");
    }

    /// <summary>A packet read and worked over, as an edit is handed it.</summary>
    private static ContentPart Root(string source) => ContentPart.Of(PacketParserTests.Staged(source));

    private static ContentPart Field(ContentPart root, int at) => PacketWriting.Fields(root)[at];

    /// <summary>What <paramref name="source"/> becomes once the change made of it is written, and where the caret is left.</summary>
    private static (string Source, int Caret) Change(string source, Func<ContentPart, ContentChange> change)
    {
        var made = change(Root(source));
        var written = source;

        foreach (var write in made.Writes.OrderByDescending(write => write.Start))
            written = written[..write.Start] + write.Text + written[write.End..];

        return (written, made.Caret);
    }

    private static string Changed(string source, Func<ContentPart, ContentChange> change) => Change(source, change).Source;
}
