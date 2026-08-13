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
        PaddedFrameCaptureTests.Definition(),
    ];

    /// <summary>
    /// What the declaration says the unbranched path is: through a container, and up to a fork, which
    /// stands for itself because past it there is no one path.
    /// </summary>
    private static IEnumerable<Node> Declared(MessageDef message, IEnumerable<Field> fields)
    {
        foreach (var field in fields)
            switch (field.Pattern)
            {
                case Pattern.Group group:
                    foreach (var member in Declared(message, group.Fields)) yield return member;
                    break;

                case Pattern.Choice:
                    yield return Set(message, field);
                    break;

                // A repetition holds the one shape it repeats, so the unbranched path goes through it
                // exactly once — which is all a description can say. How many there are is a run's answer.
                case Pattern.Chain chain:
                    foreach (var member in Declared(message, [chain.Element])) yield return member;
                    break;

                // A run of unlike components leads with its token; which kind follows is a fork, and the
                // unbranched path stops there like any other.
                case Pattern.Assorted assorted:
                    foreach (var member in Declared(message, [assorted.Token])) yield return member;
                    break;

                default:
                    yield return field;
                    break;
            }
    }

    private static FieldSet Set(MessageDef message, Field declared)
        => message.Graph.Nodes.OfType<FieldSet>().Single(s => s.Derived == declared);

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
            var listed = Declared(message, message.Fields).ToList();

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

        var begins = message.Graph.From<Then>(arrangement).Single();
        Assert.AreSame(message.Fields[0], begins.To, "and it goes on to the first thing declared");
        Assert.IsNull(begins.Key, "with nothing to decide, because there is one arrangement");

        // One way on, unkeyed, because a field list has nothing to decide. Several keyed ways on are what
        // an alternation will be, and a way on that reaches backwards is what a repetition will be — the
        // same edge, which is the reason for it being one.
        var next = message.Graph.From<Then>(message.Fields[0]).Single();
        Assert.IsNull(next.Key);
        Assert.AreSame(message.Fields[1], next.To);
    }

    /// <summary>
    /// A container is a set, and a set is not a field.
    /// </summary>
    /// <remarks>
    /// The distinction the engine did not have: a field <i>produces</i> octets and a set only <i>spans</i>
    /// the ones its members produced. Every header, sequence and pseudo-header in the corpus is declared as
    /// a field because there was nowhere else to say it, which gave each of them a value and an emission it
    /// has no business having.
    /// </remarks>
    [TestMethod]
    public void A_container_is_a_set_that_holds_its_members_in_order_and_makes_nothing_itself()
    {
        var message = Corpus
            .First(m => m.Fields.Any(f => f.Pattern is Pattern.Group { Fields.Count: > 1 }));

        var declared = message.Fields.First(f => f.Pattern is Pattern.Group { Fields.Count: > 1 });
        var members = ((Pattern.Group)declared.Pattern).Fields;

        var set = message.Graph.Nodes.OfType<FieldSet>().Single(s => s.Derived == declared);
        var held = message.Graph.From<Holds>(set).OrderBy(h => h.Order).Select(h => h.To).ToList();

        CollectionAssert.AreEqual(members.ToArray(), held.ToArray(),
            "its members, ordered on the edge — genuinely ordinal, unlike a way on");

        // Neither the set nor the field it was declared as is on the wire: the walk goes through, not over.
        var laid = message.Walk(message.Arrangements.Single()).ToList();
        Assert.IsFalse(laid.Contains(set));
        Assert.IsFalse(laid.Contains(declared));
    }

    /// <summary>
    /// Everything the arrangement reaches that stands for part of the message and emits nothing — a
    /// container, or a fork. A chain's element is not laid out yet, so its interior joins when that
    /// converts.
    /// </summary>
    private static IEnumerable<Field> Contained(IEnumerable<Field> fields)
    {
        foreach (var field in fields)
            switch (field.Pattern)
            {
                case Pattern.Group group:
                    yield return field;
                    foreach (var deeper in Contained(group.Fields)) yield return deeper;
                    break;

                case Pattern.Choice choice:
                    yield return field;
                    foreach (var deeper in Contained(choice.Arms.SelectMany(a => a.Fields)))
                        yield return deeper;
                    break;

                case Pattern.Chain chain:
                    yield return field;
                    foreach (var deeper in Contained([chain.Element])) yield return deeper;
                    break;

                case Pattern.Assorted assorted:
                    yield return field;
                    foreach (var deeper in Contained([assorted.Token, .. assorted.Sorts.SelectMany(s => s.Fields)]))
                        yield return deeper;
                    break;
            }
    }

    [TestMethod]
    public void Everything_that_stands_for_part_of_a_message_and_emits_nothing_is_a_set()
    {
        foreach (var message in Corpus)
        {
            var containers = Contained(message.Fields).ToList();
            var sets = message.Graph.Nodes.OfType<FieldSet>().ToList();

            CollectionAssert.AreEquivalent(containers, sets.Where(s => s.Derived is not null).Select(s => s.Derived!).ToList(),
                $"{message.Id}: one set per container, and nothing else pretending to be one");
        }
    }

    /// <summary>
    /// A repetition is a property of a span, and the path never reaches back.
    /// </summary>
    /// <remarks>
    /// This was a back edge for one round of the design and that was wrong. What repeats is <i>inside</i>
    /// the span — a record block holds a record, an option block holds an option — and a cycle in the path
    /// put a containment fact into the arrangement, so the walk revisited nodes and "what follows what"
    /// stopped being answerable by reading it. The span is one place however many turn up in it, and the
    /// unrolling belongs to the run, where the count is finally known.
    /// </remarks>
    [TestMethod]
    public void A_repetition_is_a_span_that_holds_one_shape_and_the_path_never_reaches_back()
    {
        var message = Corpus.First(m => m.AllFields.Any(f => f.Pattern is Pattern.Chain));
        var repeat = message.AllFields.First(f => f.Pattern is Pattern.Chain);
        var span = message.Graph.Nodes.OfType<FieldSet>().Single(s => s.Derived == repeat);

        Assert.AreEqual(1, message.Graph.From<Holds>(span).Count(), "the one shape it repeats");
        Assert.IsTrue(message.Graph.From<Decides>(span).Any(), "and something asks whether another follows");

        // The whole path, everywhere, for every document: no edge leads anywhere the walk has been.
        foreach (var document in Corpus)
        {
            var seen = new HashSet<Node>();
            var reachable = new Queue<Node>(document.Arrangements.Cast<Node>());

            while (reachable.Count > 0)
            {
                var at = reachable.Dequeue();
                if (!seen.Add(at)) continue;

                foreach (var edge in document.Graph.From<Then>(at)) reachable.Enqueue(edge.To);
                foreach (var edge in document.Graph.From<Holds>(at)) reachable.Enqueue(edge.To);
            }

            foreach (var edge in document.Graph.Of<Then>())
                Assert.IsFalse(Reaches(document, edge.To, edge.From),
                    $"{document.Id}: {edge.From.Name} leads to {edge.To.Name}, which leads back");
        }
    }

    /// <summary>Whether one node can be got to from another by ways on and members.</summary>
    private static bool Reaches(MessageDef message, Node from, Node target)
    {
        HashSet<Node> seen = [];
        Queue<Node> next = new([from]);

        while (next.Count > 0)
        {
            var at = next.Dequeue();
            if (ReferenceEquals(at, target)) return true;
            if (!seen.Add(at)) continue;

            foreach (var edge in message.Graph.From<Then>(at)) next.Enqueue(edge.To);
            foreach (var edge in message.Graph.From<Holds>(at)) next.Enqueue(edge.To);
        }

        return false;
    }

    /// <summary>
    /// A run of unlike components is repetition and alternation, composed — and nothing else.
    /// </summary>
    /// <remarks>
    /// The claim the whole arrangement model rests on. An assortment was a pattern of its own, with its
    /// own validation, its own threading parameters and its own carry-per-kind; here it is a token, a fork
    /// on what the token said, and a way on that reaches back. If it had needed one edge the other two did
    /// not, "one guarded edge covers sequence, choice and repeat" would be false.
    /// </remarks>
    [TestMethod]
    public void A_run_of_unlike_components_needs_nothing_the_other_two_did_not()
    {
        var message = Corpus.First(m => m.AllFields.Any(f => f.Pattern is Pattern.Assorted));
        var field = message.AllFields.First(f => f.Pattern is Pattern.Assorted);
        var assorted = (Pattern.Assorted)field.Pattern;

        var run = message.Graph.Nodes.OfType<FieldSet>().Single(s => s.Derived == field);
        var token = message.Graph.From<Holds>(run).Single().To;

        Assert.AreSame(assorted.Token, token, "it leads with its token");

        // The fork: keyed ways on, decided by what the token said.
        var kinds = message.Graph.From<Then>(token).Single().To;
        Assert.AreSame(assorted.Token, message.Graph.From<Decides>(kinds).Single().To);

        var ways = message.Graph.From<Then>(kinds).ToList();
        Assert.AreEqual(assorted.Sorts.Count, ways.Count,
            "one way on per kind — including a kind that holds nothing, which is still a component");
        Assert.IsTrue(ways.All(w => w.Key is not null || w.Otherwise));

        // And the repeat: a property of the span, not a cycle. The token is read again on the next pass
        // because the span says there is one, not because an edge points backwards at it.
        Assert.IsTrue(message.Graph.From<Decides>(run).Any(),
            "something has to ask whether another component follows");
    }

    [TestMethod]
    public void The_unbranched_walk_terminates_even_where_the_description_loops()
    {
        // It follows only a single unkeyed way on, so a back edge — which is keyed — is never taken. The
        // description may loop; reading it does not.
        foreach (var message in Corpus)
        {
            var laid = message.Walk(message.Arrangements.Single()).ToList();

            Assert.AreEqual(laid.Count, laid.Distinct().Count(), $"{message.Id} lays something out twice");
        }
    }
}
