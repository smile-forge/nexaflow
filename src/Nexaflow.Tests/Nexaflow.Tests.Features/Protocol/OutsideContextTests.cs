using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The values a message needs that are not in it.
///
/// <para>
/// These were a bag of names resolved at evaluation time and declared nowhere, which is the shape field
/// references and rules both had before they became edges. It fails the same way and for the same reason:
/// nothing could say what a document required in order to run, because nothing had been told.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol outside context — tree nodes land with the engine")]
public class OutsideContextTests
{
    [TestMethod]
    public void What_a_person_must_be_asked_is_computed_from_the_graph()
    {
        // The point of this being edges. A prompt list maintained beside a document drifts from it the
        // first time either changes; this one cannot, because it IS what the document reads.
        var asked = VariableWidthCaptureTests.Connect().Asked.ToList();

        CollectionAssert.AreEquivalent(new[] { "clientId", "willTopic", "userName", "password" },
            asked.Select(c => c.Key).ToArray());

        foreach (var source in asked)
            Assert.IsFalse(string.IsNullOrWhiteSpace(source.Purpose),
                $"'{source.Key}' is shown to a person and must say what it is for");

        // And one of them is a credential, which is a different thing from a question even though both
        // are asked — it must never turn up in an explanation or a dry run.
        var secret = asked.Single(c => c is Context.Secret);
        Assert.AreEqual("password", secret.Key);
        Assert.IsFalse(secret.Showable);
        Assert.IsTrue(asked.Where(c => c is not Context.Secret).All(c => c.Showable));
    }

    [TestMethod]
    public void A_value_taken_from_the_machine_or_from_a_counter_is_never_asked_for()
    {
        // Where a value comes from decides who can be asked for it. An adapter's own address is
        // discovered, and a transaction id advances on its own; putting either in front of a person
        // would be asking them to look something up that is already known.
        var asked = PaddedFrameCaptureTests.Definition().Asked.Select(c => c.Key).ToList();

        CollectionAssert.AreEqual(new[] { "serverName" }, asked);
        CollectionAssert.DoesNotContain(asked, "hardwareAddress");
        CollectionAssert.DoesNotContain(asked, "transactionId");
    }

    [TestMethod]
    public void Reading_a_value_the_document_never_says_it_needs_is_an_authoring_error()
    {
        // Before this it resolved to nothing and surfaced much later as a converter complaining about a
        // type, usually in a field that had nothing to do with the mistake.
        var mistyped = new MessageDef
        {
            Id = "mistyped",
            Context = Context.Given.These("clientIdentifier"),
            Fields =
            [
                new Field
                {
                    Id = "name",
                    Pattern = new Pattern.Opaque(4),
                    Value = Expr.Parse("inputs.clientIdentifer"),
                },
            ],
        };

        StringAssert.Contains(string.Join("\n", mistyped.Validate()),
            "reads 'inputs.clientIdentifer', which the document never says it needs");
    }

    [TestMethod]
    public void Declaring_a_need_nothing_reads_is_an_authoring_error_too()
    {
        // The other direction, and the reason a prompt list rots: a document keeps asking for something
        // it stopped using two revisions ago and nobody notices, because nothing relates the two.
        var stale = new MessageDef
        {
            Id = "stale",
            Context = Context.Given.These("used", "leftBehind"),
            Fields = [new Field { Id = "a", Pattern = new Pattern.Opaque(1), Value = Expr.Parse("inputs.used") }],
        };

        StringAssert.Contains(string.Join("\n", stale.Validate()), "'leftBehind' is declared and never read");
    }

    [TestMethod]
    public void Something_asked_of_a_person_has_to_say_what_it_is_for()
    {
        // That sentence is the question they get shown. A document that cannot say why it wants a value
        // has no business asking anyone for one.
        var mute = new MessageDef
        {
            Id = "mute",
            Context = [new Context.Prompted { Key = "secretHandshake" }],
            Fields = [new Field { Id = "a", Pattern = new Pattern.Opaque(1), Value = Expr.Parse("inputs.secretHandshake") }],
        };

        StringAssert.Contains(string.Join("\n", mute.Validate()), "does not say what for");
    }

    [TestMethod]
    public void Every_document_says_what_it_needs()
    {
        // The check that made this worth doing to seven documents rather than one.
        foreach (var message in (MessageDef[])
        [
            EndToEndCaptureTests.Definition(),
            FramedChoiceCaptureTests.Request(),
            FramedChoiceCaptureTests.Response(),
            VariableWidthCaptureTests.Connect(),
            VariableWidthCaptureTests.FilterList(),
            NestedLengthCaptureTests.Message(),
            PaddedFrameCaptureTests.Definition(),
        ])
        {
            Assert.AreEqual(0, message.Validate().Count, $"{message.Id}:\n" + string.Join("\n", message.Validate()));
            Assert.IsTrue(message.Context.Count > 0, $"{message.Id} reads nothing from outside?");
        }
    }
}
