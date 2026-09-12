namespace Nexaflow.Visuals.Text.Markdown.Music.Model;

/// <summary>The clef a staff is drawn with. v1 renders <see cref="Treble"/> and <see cref="Bass"/>;
/// <see cref="Alto"/>/<see cref="Tenor"/> are modelled for a future C-clef renderer.</summary>
public enum ClefKind { Treble, Bass, Alto, Tenor }

/// <summary>Where a text annotation (ABC's <c>"^text"</c> / <c>"_text"</c> / <c>"&lt;text"</c> /
/// <c>"&gt;text"</c>) sits relative to its note. A bare <c>"text"</c> is a chord symbol, not an annotation.</summary>
public enum AnnotationPlacement { Above, Below, Left, Right }
