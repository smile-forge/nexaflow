using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Prose;

/// <summary>What a piece of a laid-out markdown document is. Kinds, so a test can say which piece it means.</summary>
public static class MarkdownPieces
{
    public const string Document = "MarkdownDocument";

    /// <summary>One block of the document, whole: laid, painted and kept as one.</summary>
    public const string Whole = "MarkdownWhole";
    public const string Block = "MarkdownBlock";
    public const string Words = "MarkdownWords";
    public const string Marker = "MarkdownMarker";
    public const string Tick = "MarkdownTick";
    public const string Rule = "MarkdownRule";
    public const string Row = "MarkdownRow";
    public const string Cell = "MarkdownCell";
    public const string Verbatim = "MarkdownVerbatim";
    public const string Picture = "MarkdownPicture";
}
