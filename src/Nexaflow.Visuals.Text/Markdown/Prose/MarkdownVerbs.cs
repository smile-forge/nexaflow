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

/// <summary>What a press on a piece of a markdown document can mean, beside the shared <see cref="LayoutVerbs"/>.</summary>
public static class MarkdownVerbs
{
    /// <summary>Tick an item off, or take the tick back — <see cref="LayoutIntent.Target"/> says which way.</summary>
    public const string Tick = "tick";
}
