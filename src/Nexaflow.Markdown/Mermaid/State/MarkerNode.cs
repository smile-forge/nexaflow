using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.State;

/// <summary>
/// A <c>[*]</c> as the stages leave it (<see cref="Stages.ResolveStates"/>): the dot its scope starts at, where a transition leaves
/// it, or the one it stops at, where one reaches it — every <c>[*]</c> in one scope, and one region of it, the same dot. It prints
/// as the <c>[*]</c> written.
/// </summary>
internal sealed class MarkerNode : ContentNode
{
    internal MarkerNode(ContentNode written, string id, bool stop) : base(written)
    {
        this.Id = id;
        this.Stop = stop;
    }

    /// <summary>
    /// What the dot is called: <c>start</c> or <c>end</c>, which a <c>class</c> or a <c>style</c> line names it by — and, inside a
    /// composite state, that and the composite's place among them, and the region past the first it is in.
    /// </summary>
    public string Id { get; }

    /// <summary>Whether it is the dot its scope stops at rather than the one it starts at.</summary>
    public bool Stop { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new MarkerNode(shape, this.Id, this.Stop);
}
