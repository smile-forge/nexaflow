using XamlMath.Boxes;

namespace XamlMath.Atoms;

/// <summary>
/// Something that has still to be written: what an empty argument stands for.
///
/// <para>
/// A symbol like any other, laid out exactly where the argument's contents would have been — so
/// everything downstream that knows how to find, hit-test, select, carry or replace a symbol handles a
/// hole without knowing it is one. That is the whole of the design: not a special case threaded through
/// the editor, but an ordinary atom that happens to draw a box.
/// </para>
/// <para>
/// It exists in the parse and never in the source, which is what makes it a hole rather than content.
/// Its span is the characters it was read from, so typing over it replaces exactly those; nothing the
/// reader saves, copies or solves ever carries a placeholder command.
/// </para>
/// </summary>
internal sealed record PlaceholderAtom : Atom
{
    protected override Box CreateBoxCore(TexEnvironment environment) => new PlaceholderBox(environment);
}
