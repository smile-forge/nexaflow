using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// What follows what, as edges.
///
/// <para>
/// A message's field list is one arrangement of it, and until now it was the <i>only</i> thing it could
/// be — order was list position and nothing could point at it. As a path through nodes it is a fact the
/// graph holds: a message may offer several, which one applies can be decided, and sequence, choice and
/// repetition stop being three shapes with three sets of rules.
/// </para>
///
/// <para>
/// Built beside the containment the codec still walks, and checked against it here. That is the whole
/// value of this test: the new structure has to say exactly what the old one says before anything is moved
/// over to it, for every document in the corpus rather than for one written to suit.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol arrangement model — engine structure, no single product node")]
public class ArrangementTests
{
    /// <summary>
    /// Every document the corpus has a handle on. Deliberately the whole set rather than one written to
    /// suit: the point of this file is that the new structure agrees with the old one everywhere.
    /// </summary>
    private static MessageDef[] Corpus =>
    [
        EndToEndCaptureTests.Definition(),
        FramedChoiceCaptureTests.Request(),
        FramedChoiceCaptureTests.Response(),
        VariableWidthCaptureTests.Connect(),
        VariableWidthCaptureTests.FilterList(),
        NestedLengthCaptureTests.Message(),
        NestedVectorCaptureTests.Definition(),
        TaggedUnionCaptureTests.Definition(),
    ];

    /// <summary>The leaves of a declaration, in the order the field list puts them.</summary>
    private static IEnumerable<Field> Declared(IEnumerable<Field> fields)
    {
        foreach (var field in fields)
            if (field.Pattern is Pattern.Group group)
                foreach (var member in Declared(group.Fields)) yield return member;
            else
                yield return field;
    }

    [TestMethod]
    public void Every_document_in_the_corpus_lays_out_the_way_its_field_list_says()
    {
        List<string> wrong = [];

        foreach (var message in Corpus)
        {
            var arrangement = message.Arrangements.SingleOrDefault();

            if (arrangement is null)
            {
                wrong.Add($"{message.Id}: no arrangement at all");
                continue;
            }

            var laid = message.Walk(arrangement).ToList();
            var listed = Declared(message.Fields).Cast<Node>().ToList();

            if (!laid.SequenceEqual(listed))
                wrong.Add($"{message.Id}:\n      path: {string.Join(", ", laid.Select(n => n.Name))}"
                        + $"\n      list: {string.Join(", ", listed.Select(n => n.Name))}");
        }

        Assert.AreEqual(0, wrong.Count,
            "the arrangement has to say exactly what the declaration says before anything reads it "
          + "instead.\n\n  • " + string.Join("\n  • ", wrong));
    }

    [TestMethod]
    public void An_arrangement_starts_somewhere_and_each_node_says_what_follows_it()
    {
        var message = Corpus.First(m => m.Fields.Count > 1);
        var arrangement = message.Arrangements.Single();

        var starts = message.Graph.From<Starts>(arrangement).Single();
        Assert.AreSame(message.Fields[0], starts.To, "and it starts at the first thing declared");

        // One way on, unkeyed, because a field list has nothing to decide. Several keyed ways on are what
        // an alternation will be, and a way on that reaches backwards is what a repetition will be — the
        // same edge, which is the reason for it being one.
        var next = message.Graph.From<Then>(message.Fields[0]).Single();
        Assert.IsNull(next.Key);
        Assert.AreSame(message.Fields[1], next.To);
    }

    [TestMethod]
    public void A_group_is_a_place_on_the_path_that_holds_its_members_in_order()
    {
        var message = Corpus
            .First(m => m.Fields.Any(f => f.Pattern is Pattern.Group { Fields.Count: > 1 }));

        var group = message.Fields.First(f => f.Pattern is Pattern.Group { Fields.Count: > 1 });
        var members = ((Pattern.Group)group.Pattern).Fields;

        var held = message.Graph.From<Holds>(group).OrderBy(h => h.Order).Select(h => h.To).ToList();

        CollectionAssert.AreEqual(members.ToArray(), held.ToArray(),
            "its members, ordered on the edge — genuinely ordinal, unlike a way on");

        // And the group is not itself on the wire: the walk goes through it, not over it.
        Assert.IsFalse(message.Walk(message.Arrangements.Single()).Contains(group));
    }

    [TestMethod]
    public void Nothing_on_the_path_is_reached_twice_while_a_field_list_is_all_there_is()
    {
        // A repetition will be a way on that reaches back, and when that arrives this stops being true —
        // deliberately, and the test that replaces it should say which node the walk returns to. Until
        // then, a path that revisited anything would be a building error rather than a protocol.
        foreach (var message in Corpus)
        {
            var laid = message.Walk(message.Arrangements.Single()).ToList();

            Assert.AreEqual(laid.Count, laid.Distinct().Count(), $"{message.Id} lays something out twice");
        }
    }
}
