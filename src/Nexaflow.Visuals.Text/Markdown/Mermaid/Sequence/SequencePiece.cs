using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

/// <summary>The pieces a sequence diagram's layout is made of — its layers, and what is in them.</summary>
public static class SequencePiece
{
    /// <summary>The boxes grouping the participants, drawn behind everything.</summary>
    public const string Boxes = "Boxes";

    /// <inheritdoc cref="Boxes"/>
    public const string Box = "Box";

    /// <summary>The lifelines: one piece for each participant, holding everything drawn down its column.</summary>
    public const string Lifelines = "Lifelines";

    /// <inheritdoc cref="Lifelines"/>
    public const string Lifeline = "Lifeline";

    /// <summary>A participant's own box or figure, at the top of its lifeline and again at the bottom.</summary>
    public const string Head = "Head";

    /// <summary>The bar down a lifeline saying the participant is working.</summary>
    public const string Bar = "Bar";

    /// <summary>Somewhere a participant leads, written under it.</summary>
    public const string Menu = "Menu";

    /// <summary>Everything written on the timeline, frames holding what is drawn inside them.</summary>
    public const string Timeline = "Timeline";

    /// <summary>One frame, standing for the whole of what was written from the word that opened it to its <c>end</c>.</summary>
    public const string Frame = "Frame";

    /// <summary>The line across a frame where it is divided, and what is written for the part under it.</summary>
    public const string Divider = "Divider";

    /// <summary>One message, its line and what is written over it.</summary>
    public const string Message = "Message";

    /// <inheritdoc cref="Message"/>
    public const string Line = "Line";

    /// <summary>The number <c>autonumber</c> gives a message, drawn where it sets out.</summary>
    public const string Number = "Number";

    /// <summary>One note, beside a lifeline or spanning several.</summary>
    public const string Note = "Note";
}
