using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using System.Collections.Generic;

namespace Nexaflow.Tests.Markdown.Binding;

/// <summary>
/// What a <c>{{…}}</c> comes to, end to end: the run read into parts that say which of it is a binding, the stage
/// that works out what each one stands for, and what the whole run then says — with the words it was written among
/// kept exactly as they were.
/// </summary>
[TestClass]
[CoversNode("mermaid")]
public class BoundTextTests
{
    private sealed class Team
    {
        public string Name { get; init; } = "Platform";
        public int Size = 4;
        public Team? Parent { get; init; }
        public List<string> People { get; init; } = ["Ada", "Grace"];
        public Dictionary<string, object> Facts { get; init; } = new() { ["region"] = "EU", ["budget"] = 12.5 };

        public string Never() => "called anyway";

        [Bindable]
        public string Headcount() => $"{People.Count} of {Size}";

        [Bindable]
        public string Boom() => throw new System.InvalidOperationException("no");
    }

    private static IDataContext Data(object? root = null) => new ReflectionDataContext(root ?? new Team());

    private static string Bound(string text, object? root = null) => Said(text, Data(root));

    /// <summary>
    /// The whole path a binding takes: read into a run whose parts say which of it stands for a value, worked over
    /// by the stage that settles what each one is, then asked what it says.
    /// </summary>
    private static string Said(string text, IDataContext? data)
    {
        // "words" stands for whatever the language calls a run of them; nothing here depends on which.
        var run = ContentWords.Of(text, "words", Roles.Element);

        Assert.AreEqual(text, run.Print(), "however it was split up, the run still prints as what was written");

        return ContentWords.Says(ContentPart.Of(data is null ? run : new WithBindings(data).Run(run)));
    }

    // ── Reading a path ──────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_property_a_field_and_a_path_through_both_are_read()
    {
        var team = new Team { Parent = new Team { Name = "Engineering" } };

        Assert.AreEqual("Platform", Bound("{{Name}}", team));
        Assert.AreEqual("4", Bound("{{Size}}", team), "a field reads like a property");
        Assert.AreEqual("Engineering", Bound("{{Parent.Name}}", team));
    }

    [TestMethod, TestCategory("Unit")]
    public void A_list_is_indexed_by_place_and_a_dictionary_by_key()
    {
        Assert.AreEqual("Grace", Bound("{{People[1]}}"));
        Assert.AreEqual("EU", Bound("{{Facts[region]}}"));
        Assert.AreEqual("12.5", Bound("{{Facts[\"budget\"]}}"), "quotes round a key are the writer's, not part of it");
        Assert.AreEqual("2", Bound("{{People.Count}}"), "and a list is still an object with properties");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_path_is_read_however_it_is_cased()
    {
        Assert.AreEqual("Platform", Bound("{{name}}"), "a path is written by hand, not generated");
    }

    // ── Calling ─────────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_method_the_host_marked_is_called_and_one_it_did_not_is_never_called()
    {
        Assert.AreEqual("2 of 4", Bound("{{Headcount()}}"));
        Assert.AreEqual("", Bound("{{Never()}}"),
                        "a call is code driven by a string in a document, so the host says which of its methods it meant");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_call_that_throws_leaves_a_gap_rather_than_taking_the_diagram_with_it()
    {
        Assert.AreEqual("", Bound("{{Boom()}}"));
    }

    // ── Nothing there ───────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_path_naming_nothing_leaves_a_gap()
    {
        Assert.AreEqual("", Bound("{{Nope}}"));
        Assert.AreEqual("", Bound("{{Parent.Name}}"), "and so does a step through a null");
        Assert.AreEqual("", Bound("{{People[9]}}"));
    }

    [TestMethod, TestCategory("Unit")]
    public void With_nothing_to_read_against_a_binding_is_the_characters_it_was_written_with()
    {
        Assert.AreEqual("{{Name}}", Said("{{Name}}", null),
                        "a document nobody has bound to still reads");
    }

    // ── Among other words ───────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void What_is_written_round_a_binding_is_kept_exactly()
    {
        Assert.AreEqual("Team Platform (4)", Bound("Team {{Name}} ({{Size}})"));
        Assert.AreEqual("nothing to bind", Bound("nothing to bind"));
        Assert.AreEqual("{{ unfinished", Bound("{{ unfinished"), "an opening nobody shut is not a binding");
        Assert.AreEqual("a } b", Bound("a } b"));
    }

    [TestMethod, TestCategory("Unit")]
    public void Space_round_a_path_is_the_writers_and_not_part_of_it()
    {
        Assert.AreEqual("Platform", Bound("{{  Name  }}"));
    }

    [TestMethod, TestCategory("Unit")]
    public void Whether_anything_is_bound_at_all_is_the_cheap_question()
    {
        Assert.IsFalse(BoundText.Binds(null));
        Assert.IsFalse(BoundText.Binds("Team"));
        Assert.IsFalse(BoundText.Binds("{{"), "too short to be one");
        Assert.IsTrue(BoundText.Binds("{{x}}"));
    }

    // ── What the tree says about it ─────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_binding_is_a_part_of_its_own_with_the_path_named_inside_it()
    {
        var run = ContentWords.Of("Team {{ Name }}!", "words", Roles.Element);
        var bound = run.Children.Single(child => child.Kind == Kinds.Bound);

        Assert.AreEqual("Name", bound.Part(Roles.Name)?.Text,
                        "the path is a part, so nothing downstream has to look at a brace to find it");
        Assert.AreEqual("{{ Name }}", bound.Print(), "and the braces and the writer's space are still there");
    }

    [TestMethod, TestCategory("Unit")]
    public void What_a_binding_stands_for_is_hung_underneath_and_takes_up_no_source()
    {
        var run = new WithBindings(Data()).Run(ContentWords.Of("{{Name}}", "words", Roles.Element));
        var bound = run.Children.Single(child => child.Kind == Kinds.Bound);

        Assert.AreEqual("Platform", bound.Said(ContentWords.Value));
        Assert.AreEqual("{{Name}}", run.Print(), "which is a reading of the source and no part of it");
    }

    [TestMethod, TestCategory("Unit")]
    public void Braces_naming_nothing_are_the_characters_they_are()
    {
        Assert.AreEqual("{{}}", Bound("{{}}"), "there is no path there to stand for anything");
        Assert.IsTrue(ContentWords.Of("{{}}", "words", Roles.Element).IsLeaf, "so the run was never split up");
    }
}
