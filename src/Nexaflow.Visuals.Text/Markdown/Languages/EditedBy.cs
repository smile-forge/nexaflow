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

/// <summary>A language whose edits are told to <paramref name="onEdit"/> — or, where that is null, one with nothing of its own to say about an edit.</summary>
internal sealed class EditedBy(IOnEdit? onEdit) : IContentLanguage
{
    public IOnEdit? OnEdit => onEdit;

    /// <summary>And its moves too, where the same handler says anything about one.</summary>
    public IOnMove? OnMove => onEdit as IOnMove;
}
