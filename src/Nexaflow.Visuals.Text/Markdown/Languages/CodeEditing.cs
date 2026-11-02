using System;
using System.Collections.Generic;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Chemistry;
using Nexaflow.Markdown.Latex;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Markdown.Music.LilyPond;
using Nexaflow.Markdown.Nomnoml;
using Nexaflow.Markdown.Packet;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Markdown.Plot;
using Nexaflow.Markdown.Prose;
using Nexaflow.Markdown.WordCloud;
using Nexaflow.Markdown.Barcode;
using Nexaflow.Markdown.Barcode.Stages;
using Nexaflow.Markdown.Matrix.Aztec.Stages;
using Nexaflow.Markdown.Matrix.DataMatrix.Stages;
using Nexaflow.Markdown.Matrix.Pdf417.Stages;
using Nexaflow.Markdown.Matrix.Qr.Stages;

using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Music;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Packet;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Stages;
using Nexaflow.Visuals.Text.Markdown.WordCloud;
using Nexaflow.Markdown.WordCloud.Stages;
using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Icons;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Rtf;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Treemap;
using Nexaflow.Visuals.Text.Markdown.Treemap;
using Nexaflow.Markdown.TreeView;
using Nexaflow.Visuals.Text.Markdown.TreeView;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Layout.Code;

namespace Nexaflow.Visuals.Text.Markdown.Languages;

/// <summary>What code shows and offers: every character a writer typed, and no picture of it.</summary>
internal sealed class CodeEditing : IContentLanguage
{
    /// <summary>Colouring code is still showing it: every character a writer typed is on the page.</summary>
    public bool ShowsWhatWasWritten => true;

    /// <summary>
    /// A picture of code is a worse copy of the code — it cannot be searched, pasted or read by anything — so that is the one
    /// usual button this does not offer.
    /// </summary>
    public BlockCorner Corner(ContentAsk ask) => new(Saves: false);
}
