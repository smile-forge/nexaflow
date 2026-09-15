using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Chemistry.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Chemistry;

/// <summary>
/// The actors a <c>smiles</c> block runs between the parser and the builder, and the order they run in.
///
/// <para>
/// Each needs the answer of the one before it. Nothing can count an atom's hydrogens until its ring closures are
/// bonds, because a ring closure is a bond like any other to the atom it closes on; and nothing can decide where a
/// ring's double bonds go until every atom's hydrogens are known, because an aromatic atom that carries one has no
/// bond left to give.
/// </para>
/// </summary>
public static class SmilesPipeline
{
    /// <summary>
    /// The tree to draw a block from: what was written, with which atom each ring closure reaches, how many
    /// hydrogens each atom carries and where each aromatic ring's double bonds go hung underneath it.
    /// </summary>
    public static ContentNode Read(string block) => Of().Run(SmilesParser.Parse(block));

    /// <summary>The pipeline itself, for anything that wants to run the stages over a tree it already has.</summary>
    public static AstPipeline Of() =>
        new(
            new ConnectAtoms(),    // which atom each ring closure reaches
            new CountHydrogens(),  // the hydrogens nobody wrote, and atoms with more bonds than they can make
            new Kekulize());       // where each aromatic ring's double bonds go
}
