namespace Nexaflow.Interaction;

/// <summary>
/// A payload made rather than received: what the app puts on the clipboard, in the same <see cref="DragKinds"/> a drag or
/// a paste is read in, so writing and reading share one vocabulary. Each kind holds what <see cref="DragKinds"/> says it
/// holds. It never changes once made: <see cref="With"/> makes another.
/// </summary>
public sealed class ComposedPayload : IDragPayload
{
    private readonly IReadOnlyDictionary<string, object> _kinds;

    private ComposedPayload(IReadOnlyDictionary<string, object> kinds) => _kinds = kinds;

    /// <summary>A payload of no kind: what a clipboard that holds nothing, or cannot be opened, reads as.</summary>
    public static ComposedPayload Empty { get; } = new(new Dictionary<string, object>(StringComparer.Ordinal));

    /// <summary>Plain words.</summary>
    public static ComposedPayload OfText(string text) => Empty.With(DragKinds.Text, text);

    /// <summary>A picture as PNG bytes, its transparency kept.</summary>
    public static ComposedPayload OfImage(byte[] png) => Empty.With(DragKinds.Image, png);

    /// <summary>Files and folders by path, copied — or, <paramref name="cut"/>, to be moved where they are pasted.</summary>
    public static ComposedPayload OfFiles(IReadOnlyList<string> paths, bool cut)
    {
        var files = Empty.With(DragKinds.Files, paths.ToArray());
        return cut ? files.With(DragKinds.FilesCut, true) : files;
    }

    /// <summary>This payload with <paramref name="kind"/> holding <paramref name="value"/>, in place of what it held.</summary>
    public ComposedPayload With(string kind, object value) =>
        new(new Dictionary<string, object>(_kinds, StringComparer.Ordinal) { [kind] = value });

    /// <summary>The kinds it holds.</summary>
    public IEnumerable<string> Kinds => _kinds.Keys;

    public bool Contains(string kind) => _kinds.ContainsKey(kind);

    public object? Read(string kind) => _kinds.GetValueOrDefault(kind);
}
