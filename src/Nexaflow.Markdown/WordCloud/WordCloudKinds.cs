namespace Nexaflow.Markdown.WordCloud;

/// <summary>What a piece of a <c>wordcloud</c> block is.</summary>
public static class WordCloudKinds
{
    /// <summary>The whole body of the fence.</summary>
    public const string Block = "block";

    /// <summary>One line and the characters that ended it — an entry, a setting, a comment, nothing, or what could not be read.</summary>
    public const string Line = "line";

    /// <summary>A <c>word: weight</c> pair — what the cloud is made of.</summary>
    public const string Entry = "entry";

    /// <summary>A <c>key: value</c> pair naming one of the settings — what the cloud is drawn like.</summary>
    public const string Setting = "setting";

    /// <summary>A word, as it is drawn. Its quotes, where it was written in any, are not part of it.</summary>
    public const string Word = "word";

    /// <summary>How much a word counts for — the number after its colon.</summary>
    public const string Weight = "weight";

    /// <summary>The name of a setting.</summary>
    public const string Key = "key";

    /// <summary>Everything after a setting's colon, less the space either side of it.</summary>
    public const string Value = "value";
}

/// <summary>What a piece of a <c>wordcloud</c> block is <em>to</em> the piece holding it.</summary>
public static class WordCloudRoles
{
    /// <summary>The word an entry is for. Its weight is <see cref="Weight"/> and its colon <see cref="Ast.Roles.Separator"/>.</summary>
    public const string Word = "word";

    /// <summary>An entry's weight.</summary>
    public const string Weight = "weight";

    /// <summary>A setting's value. Its key is <see cref="Ast.Roles.Name"/>.</summary>
    public const string Value = "value";
}
