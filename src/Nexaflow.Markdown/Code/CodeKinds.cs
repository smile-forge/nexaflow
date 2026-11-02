using Nexaflow.Markdown.Lines;

namespace Nexaflow.Markdown.Code;

/// <summary>What a piece of code is — to the tree it is read into, and to the layout it is set in.</summary>
public static class CodeKinds
{
    /// <summary>A whole stretch of code.</summary>
    public const string Code = "code";

    /// <summary>Where one line of it ends and the next begins, as in everything read line by line (<see cref="LineKinds.LineEnd"/>).</summary>
    public const string LineEnd = LineKinds.LineEnd;

    /// <summary>
    /// One line of a stretch of code, its line end with it where it has one: a block of its own, so a keystroke lays the line it was
    /// typed on and sets down every line that reads as it did as it was laid.
    /// </summary>
    public const string Line = "code-line";

    /// <summary>Hung on a stretch of code the next stretch carries on from, as on everything read line by line (<see cref="LineKinds.CarriedOn"/>).</summary>
    public const string CarriedOn = LineKinds.CarriedOn;
}
