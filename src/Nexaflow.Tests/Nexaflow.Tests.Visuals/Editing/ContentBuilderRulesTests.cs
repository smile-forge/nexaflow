using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// A builder is one step of a chain — parser, pipeline, builder, layout, editing — and owns nothing. It is handed
/// content that has already been read and turns it into a layout. Nothing else.
///
/// <para>
/// So every builder is the same shape: <strong>one constructor, taking the reading, what is being written, the
/// colours and whether it is read-only, and nothing besides.</strong> Anything else a builder was going to be told
/// is a fact about the content or about the showing of it, and belongs in one of those four where every builder
/// can be given it the same way — not as a fifth parameter one builder has and the others do not. The same goes
/// for handing it in afterwards: a builder with a property somebody sets is a builder with a second constructor
/// written the long way round.
/// </para>
/// <para>
/// And nothing named but what <see cref="ContentBuilder"/> itself declares, so there is one place to say what a
/// builder is. <see cref="ContentBuilder.Build"/> is the one a builder writes for itself, and it is declared on
/// the base and overridden — not invented under a name of its own by each of them.
/// </para>
/// <para>
/// <b>A ratchet, not a pass over the whole repo.</b> The builders that predate the rule are named in
/// <see cref="BaselineFile"/>, and the two tests pull against each other: the first refuses a <i>new</i> builder
/// out of shape, the second refuses a name on the list that has since come into shape or gone away. The list can
/// only shrink, and it cannot rot into a permanent allowlist.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("whole-assembly architecture guard; maps to no single product node")]
public class ContentBuilderRulesTests
{
    /// <summary>The one signature. Read, written, drawn with, and whether anybody may write in it.</summary>
    private static readonly Type[] TheOneShape =
        [typeof(ContentReading), typeof(EditState), typeof(StyleFormat), typeof(bool)];

    /// <summary>The ratchet. One type's full name per line; <c>#</c> starts a comment.</summary>
    private static string BaselineFile => Path.Combine(
        Root(), "src", "Nexaflow.Tests", "Nexaflow.Tests.Visuals", "Editing",
        "content-builders-not-yet-one-shape.txt");

    /// <summary>The checkout this was built from, found by walking up to the solution.</summary>
    private static string Root()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
            if (File.Exists(Path.Combine(at.FullName, "Nexaflow.slnx"))) return at.FullName;

        throw new AssertFailedException("Nothing above the test binaries holds Nexaflow.slnx.");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Every_builder_is_one_shape()
    {
        var baseline = Baseline();

        var broken = Builders()
            .Select(builder => (Builder: builder, Faults: Faults(builder)))
            .Where(found => found.Faults.Count > 0)
            .Where(found => !baseline.Contains(Named(found.Builder)))
            .OrderBy(found => Named(found.Builder), StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(
            0, broken.Count,
            "A builder is handed what was read and gives back a layout. These are not that shape:\n"
            + string.Join("\n", broken.Select(found => $"  {Named(found.Builder)}\n"
                                                    + string.Join("\n", found.Faults.Select(fault => $"      {fault}"))))
            + $"\n\nThe one shape is ({string.Join(", ", TheOneShape.Select(Readable))}).");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void The_list_names_only_builders_that_are_still_out_of_shape()
    {
        var out_of_shape = Builders()
            .Where(builder => Faults(builder).Count > 0)
            .Select(Named)
            .ToHashSet(StringComparer.Ordinal);

        var stale = Baseline()
            .Where(named => !out_of_shape.Contains(named))
            .OrderBy(named => named, StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(
            0, stale.Count,
            "These are listed as out of shape but are not — take them off the list, which is the whole point "
            + $"of it being one:\n  {string.Join("\n  ", stale)}\n\n{BaselineFile}");
    }

    /// <summary>Everything that is a builder, the base itself apart — abstract ones too, since they declare constructors.</summary>
    private static IEnumerable<Type> Builders() =>
        typeof(ContentBuilder).Assembly
            .GetTypes()
            .Where(type => type.IsClass && type != typeof(ContentBuilder) && typeof(ContentBuilder).IsAssignableFrom(type));

    private static string Named(Type type) => type.FullName ?? type.Name;

    private static IReadOnlyList<string> Faults(Type builder)
    {
        var faults = new List<string>();

        var made = builder.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                          .Where(one => !one.IsStatic)
                          .ToList();

        if (made.Count != 1)
            faults.Add($"has {made.Count} constructors; a builder has one");
        else if (!Shaped(made[0]))
            faults.Add($"is made with ({string.Join(", ", made[0].GetParameters().Select(p => Readable(p.ParameterType)))})");

        foreach (var set in builder
                     .GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                     .Where(property => property.SetMethod is not null)
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
            faults.Add($"is told {set.Name} after it is made; there is one way to tell a builder anything");

        foreach (var extra in Public(builder).OrderBy(name => name, StringComparer.Ordinal))
            faults.Add($"offers {extra}, which {nameof(ContentBuilder)} does not");

        return faults;
    }

    private static bool Shaped(ConstructorInfo made) =>
        made.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(TheOneShape);

    /// <summary>
    /// What a builder offers the world under a name the base never declared — a second door into the same room.
    /// Every name <see cref="ContentBuilder"/> has is allowed however it is written here, because the base is
    /// where what a builder is gets decided.
    /// </summary>
    private static IEnumerable<string> Public(Type builder)
    {
        var allowed = typeof(ContentBuilder)
            .GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .Concat(typeof(object).GetMembers().Select(member => member.Name))
            .ToHashSet(StringComparer.Ordinal);

        return builder
            .GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType is MemberTypes.Method or MemberTypes.Property or MemberTypes.Field or MemberTypes.Event)
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Where(member => !allowed.Contains(member.Name))
            .Select(member => member.Name)
            .Distinct(StringComparer.Ordinal);
    }

    private static string Readable(Type type) => type == typeof(bool) ? "bool" : type.Name;

    private static HashSet<string> Baseline()
    {
        if (!File.Exists(BaselineFile)) return new HashSet<string>(StringComparer.Ordinal);

        return File.ReadAllLines(BaselineFile)
            .Select(line => line.Split('#')[0].Trim())
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }
}
