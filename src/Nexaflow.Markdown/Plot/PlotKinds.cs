using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// What a piece of a plot block <em>is</em>: the shapes its lines are made of.
///
/// <para>
/// Deliberately only the shapes. Which line is the header, which column a cell stands in, which
/// aesthetic that column feeds and whether a cell reads as a number are all facts about the block as a
/// whole rather than about the characters of one line, so none of them is a kind — they are hung
/// underneath by the pipeline, where they can be worked out from everything that was written.
/// </para>
/// </summary>
public static class PlotKinds
{
    /// <summary>The whole block.</summary>
    public const string Block = "plot-block";

    /// <summary>One line of it: the space either side of what it says, and the characters that ended it.</summary>
    public const string Line = "plot-line";

    /// <summary>A <c>key: value</c> line, which is a setting only while the settings are still open.</summary>
    public const string Setting = "plot-setting";

    /// <summary>The key of a setting.</summary>
    public const string Key = "plot-key";

    /// <summary>
    /// What a setting is set to: every character after its colon, held whole. What those characters
    /// amount to — a number, a column's name, a list of colours — is the reader's, because it depends on
    /// the key.
    /// </summary>
    public const string Value = "plot-value";

    /// <summary>
    /// The bare <c>data</c> keyword, which opens the table where a reader wants to be explicit rather
    /// than leaving the first row to do it.
    /// </summary>
    public const string Data = "plot-data";

    /// <summary>One line of the table.</summary>
    public const string Row = "plot-row";

    /// <summary>One value of a row, with the quotes around it where it was written in any.</summary>
    public const string Cell = "plot-cell";

    /// <summary>What a pipeline stage worked out, hung underneath the piece it is about.</summary>
    public const string Fact = "plot-fact";
}

/// <summary>
/// What a piece of a plot block is <em>to</em> the line holding it.
///
/// <para>
/// A row is <see cref="Roles.Row"/> and a value of one is <see cref="Roles.Cell"/> — both shared, because
/// a grid of values is not something this language invented. Only what a setting is set to needs a role
/// of its own.
/// </para>
/// </summary>
public static class PlotRoles
{
    /// <summary>What a setting is set to.</summary>
    public const string Value = "value";

    // ── What the stages work out ────────────────────────────────────────────
    //
    // None of these is in the characters of one line, which is exactly why none of them is decided by
    // the parser: each is a fact about the table as a whole, hung under the piece it is about.

    /// <summary>On the block: whether the table is a long list of points or a matrix.</summary>
    public const string Form = "form";

    /// <summary>On a row: it names the columns rather than holding a point.</summary>
    public const string Header = "header";

    /// <summary>On a row of a matrix, and on the cell that carries it: the value every cell of the row shares down the side.</summary>
    public const string Names = "names";

    /// <summary>On a cell: what its column is called.</summary>
    public const string Column = "column";

    /// <summary>On a cell: which column it stands in, counted from nought.</summary>
    public const string Index = "index";

    /// <summary>On a cell: the number it reads as, written out plainly — or absent, which is what makes it a category.</summary>
    public const string Number = "number";

    /// <summary>On a cell: the channel its column feeds.</summary>
    public const string Aesthetic = "aesthetic";
}
