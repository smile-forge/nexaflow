using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Expressions;

/// <summary>
/// Resolves the path roots an expression can read — <c>inputs</c>, <c>consts</c>, <c>capture</c>,
/// <c>item</c>, <c>device</c>, <c>adapter</c>, and the rest.
///
/// <para>
/// A scope is a plain bag of named values rather than a set of interfaces onto the shell, and
/// deliberately so: the engine must be exercisable end to end with nothing but literals, which is what
/// makes every protocol in the corpus testable without a socket, a device, or a workspace.
/// </para>
/// </summary>
public sealed class EvalScope
{
    private readonly Dictionary<string, ProtoValue> _roots = new(StringComparer.Ordinal);
    private readonly EvalScope? _parent;

    public EvalScope(EvalScope? parent = null) => _parent = parent;

    /// <summary>Binds a whole root, e.g. <c>inputs</c> to a record of the declared inputs.</summary>
    public EvalScope Set(string root, ProtoValue value)
    {
        _roots[root] = value;
        return this;
    }

    /// <summary>Convenience for building a record root a member at a time.</summary>
    public EvalScope SetMember(string root, string member, ProtoValue value)
    {
        var members = _roots.TryGetValue(root, out var existing) && existing is ProtoValue.Rec r
            ? new Dictionary<string, ProtoValue>(r.Members, StringComparer.Ordinal)
            : new Dictionary<string, ProtoValue>(StringComparer.Ordinal);

        members[member] = value;
        _roots[root] = new ProtoValue.Rec(members);
        return this;
    }

    /// <summary>
    /// The value bound to <paramref name="root"/>, or <see cref="ProtoValue.Null"/>.
    /// Null rather than an exception: an unbound optional root must suppress its enclosing
    /// <c>when</c> the same way an out-of-range index does, not abort the parse.
    /// </summary>
    public ProtoValue Root(string root)
        => _roots.TryGetValue(root, out var v) ? v
         : _parent?.Root(root) ?? ProtoValue.Nothing;

    /// <summary>True if the root is bound anywhere in the chain — used by the validator to tell an
    /// undeclared root (an authoring error) from one that is legitimately absent at run time.</summary>
    public bool HasRoot(string root)
        => _roots.ContainsKey(root) || (_parent?.HasRoot(root) ?? false);

    /// <summary>A child scope, for an <c>each</c> body's <c>item.*</c> or a <c>ref</c>'s <c>params.*</c>.</summary>
    public EvalScope Child() => new(this);

    public static ProtoValue Record(params (string Name, ProtoValue Value)[] members)
        => new ProtoValue.Rec(members.ToDictionary(m => m.Name, m => m.Value, StringComparer.Ordinal));
}
