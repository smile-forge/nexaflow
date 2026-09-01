using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// The table, held to what it claims.
///
/// <para>
/// It is the only part of the parser that is a matter of fact rather than of syntax, and so the only
/// part that can be wrong while every formula still reads back exactly as written — give a command an
/// argument it does not take and the source is intact, the tree is just not the truth.
/// </para>
/// <para>
/// This is the half that can be checked without leaving the building: whatever a row says a command
/// takes, a formula written that way must come back with all of it. The other half — whether the row is
/// right about how people actually write the command — only the corpus can answer, and it is what
/// caught <c>\brace</c> and <c>\brack</c> being listed as taking two arguments when they are infix and
/// take none.
/// </para>
/// </summary>
[TestClass]
[CoversNode("maths-latex-roles")]
public class TexCommandTableTests
{
    [TestMethod]
    public void EveryCommandGetsWhatTheTableSaysItTakes()
    {
        foreach (var command in TexCommands.All)
        {
            var latex = Written(command);

            // Anywhere in the tree, not just at the top: \left and \begin are the openers of a fence and
            // an environment, so the command itself is a part of the construct it starts rather than a
            // thing standing on its own.
            var node = TexParser.Parse(latex).SelfAndDescendants()
                .FirstOrDefault(n => n.Kind == TexKind.Command && n.Part(TexRole.Name)?.Text == command.Name);

            Assert.IsNotNull(node, $"{latex} did not read as a command at all");

            if (command.Option is { } option)
                Assert.IsNotNull(node.Part(option), $"{latex}: no {option}");

            foreach (var role in command.Arguments)
                Assert.IsNotNull(node.Part(role), $"{latex}: no {role}");
        }
    }

    [TestMethod]
    public void AndNamesEachOfThemSomethingDifferent()
    {
        // Two arguments with the same role would make one of them unreachable: asking a fraction for its
        // numerator would answer with whichever came first, silently and forever.
        foreach (var command in TexCommands.All)
        {
            var roles = command.Arguments.ToList();
            if (command.Option is { } option) roles.Add(option);

            CollectionAssert.AreEquivalent(roles.Distinct().ToList(), roles,
                $"{command.Name} names two of its parts the same thing");
        }
    }

    [TestMethod]
    public void EveryCommandReadsBackAsItWasWritten()
    {
        foreach (var command in TexCommands.All)
        {
            var latex = Written(command);
            Assert.AreEqual(latex, TexParser.Parse(latex).Print());
        }
    }

    /// <summary>The command, written with everything the table says it takes.</summary>
    private static string Written(TexCommand command)
    {
        var latex = command.Name;
        if (command.Option is not null) latex += "[0]";

        for (var i = 0; i < command.Arguments.Count; i++) latex += "{" + (char)('a' + i) + "}";

        // \begin and \end name an environment rather than take content, and \begin{a} on its own is the
        // start of one — which is a different shape entirely, and has its own tests.
        return command.Name is @"\begin" or @"\end" ? latex + @"\end{a}" : latex;
    }
}
