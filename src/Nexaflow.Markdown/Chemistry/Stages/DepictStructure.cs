using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Chemistry.Depiction;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Chemistry.Stages;

/// <summary>
/// Says where each atom of a molecule goes on the page, in bond lengths, and how each bond at a stereocentre is wedged
/// (<see cref="StructureLayout"/>), and hangs it on the molecule.
///
/// <para>
/// What a structure looks like is a fact about the molecule — its rings, its chains, the side of a double bond a <c>/</c> puts
/// a substituent on, its handedness — and not about the room it is drawn in: the same string is the same drawing at any size.
/// So it is worked out here, once the bonds, the hydrogens and the double bonds are known, and the builder only scales it to
/// its room and draws it.
/// </para>
/// </summary>
public sealed class DepictStructure : IAstStage
{
    public string Name => "smiles:structure";

    public ContentNode Run(ContentNode tree) =>
        SmilesRewrite.Molecules(tree, node => node is MoleculeNode { Atoms.Count: > 0 } molecule
                                                  ? molecule.Depicted(StructureLayout.Of(molecule))
                                                  : node);
}
