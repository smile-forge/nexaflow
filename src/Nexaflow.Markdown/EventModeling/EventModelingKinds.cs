namespace Nexaflow.Markdown.EventModeling;

/// <summary>What a piece of an <c>eventmodeling</c> block is.</summary>
public static class EventModelingKinds
{
    /// <summary>The block: what it is read into, and what its stage puts the timeline on.</summary>
    public const string Block = "eventmodeling-block";

    /// <summary>The front matter between its <c>---</c> rows.</summary>
    public const string FrontMatter = "eventmodeling-front-matter";

    /// <summary>A <c>key: value</c> row of the front matter.</summary>
    public const string Setting = "eventmodeling-setting";

    /// <summary>A row of the front matter that is no <c>key: value</c> — a list item, say — held as written.</summary>
    public const string Yaml = "eventmodeling-yaml";

    /// <summary>A <c>%%{ … }%%</c> directive, which Mermaid reads wherever it stands.</summary>
    public const string Directive = "eventmodeling-directive";

    /// <summary>The <c>eventmodeling</c> word the block starts with.</summary>
    public const string Header = "eventmodeling-header";

    /// <summary>A word the grammar reserves: a statement's own word, an entity type, a data format, <c>given</c>.</summary>
    public const string Keyword = "eventmodeling-keyword";

    /// <summary>A <c>title</c> statement.</summary>
    public const string Title = "eventmodeling-title";

    /// <summary>An <c>accTitle:</c> statement.</summary>
    public const string AccessibleTitle = "eventmodeling-acc-title";

    /// <summary>An <c>accDescr</c> statement, after a colon or between braces.</summary>
    public const string AccessibleDescription = "eventmodeling-acc-descr";

    /// <summary>A time frame or a reset frame: <c>tf 02 cmd AddItem ->> 01 [[AddItem01]] { … }</c>.</summary>
    public const string Frame = "eventmodeling-frame";

    /// <summary>An <c>entity</c> statement, naming what a specification's lines refer to.</summary>
    public const string Entity = "eventmodeling-entity";

    /// <summary>A <c>data</c> statement: a named block of data a frame refers to.</summary>
    public const string Data = "eventmodeling-data";

    /// <summary>A <c>note</c> statement, about the frame it names.</summary>
    public const string Note = "eventmodeling-note";

    /// <summary>A <c>gwt</c> statement: a given / when / then specification of the frame it names.</summary>
    public const string Specification = "eventmodeling-specification";

    /// <summary>A <c>given</c>, <c>when</c> or <c>then</c> clause of a specification, with what it lists.</summary>
    public const string Clause = "eventmodeling-clause";

    /// <summary>A line of a clause: an entity type and the entity it names.</summary>
    public const string Mention = "eventmodeling-mention";

    /// <summary>What a frame or an entity is called: its words, dots between them.</summary>
    public const string Name = "eventmodeling-name";

    /// <summary>A <c>->></c> and the number of the frame feeding the one it is written on.</summary>
    public const string Feed = "eventmodeling-feed";

    /// <summary>A <c>[[name]]</c> naming the data block a frame shows.</summary>
    public const string Reference = "eventmodeling-reference";

    /// <summary>A data format in backticks: <c>`json`</c>.</summary>
    public const string Format = "eventmodeling-format";

    /// <summary>Data, with what it is written between: on the row in braces or quotes, or a block over several rows.</summary>
    public const string Value = "eventmodeling-value";
}

/// <summary>What a piece of an <c>eventmodeling</c> block is <em>to</em> the piece holding it.</summary>
public static class EventModelingRoles
{
    /// <summary>A frame's own number.</summary>
    public const string Frame = "eventmodeling-frame";

    /// <summary>The number of a frame named from elsewhere — what feeds a frame, what a note or a specification is about.</summary>
    public const string Source = "eventmodeling-source";

    /// <summary>An entity type: <c>ui</c>, <c>cmd</c>, <c>evt</c>, <c>rmo</c>, <c>pcr</c> and their long forms.</summary>
    public const string Type = "eventmodeling-type";

    /// <summary>A word of a name.</summary>
    public const string Name = "eventmodeling-name";

    /// <summary>What a data block is called, where it is written and where a frame refers to it.</summary>
    public const string DataName = "eventmodeling-data-name";

    /// <summary>The entity a specification's line names.</summary>
    public const string Entity = "eventmodeling-entity";

    /// <summary>A data format's word.</summary>
    public const string Format = "eventmodeling-format";

    /// <summary>What data says: the inside of its braces or quotes, a row of a block.</summary>
    public const string Said = "eventmodeling-said";

    /// <summary>What a title or an accessibility line says.</summary>
    public const string Text = "eventmodeling-text";

    /// <summary>What a front-matter key is set to.</summary>
    public const string Setting = "eventmodeling-setting";
}
