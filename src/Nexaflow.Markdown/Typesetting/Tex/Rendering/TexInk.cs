namespace Nexaflow.Markdown.Typesetting.Tex.Rendering;

/// <summary>
/// A colour a formula names — <c>\color{…}</c>, <c>\colorbox{…}</c> — as the typesetter carries it: what it is, and nothing of how
/// it is drawn, which is the layer's to say for whichever renderer it lays for.
/// </summary>
public sealed record TexInk(byte A, byte R, byte G, byte B) : IBrush;
