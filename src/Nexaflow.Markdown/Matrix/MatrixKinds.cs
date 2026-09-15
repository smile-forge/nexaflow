namespace Nexaflow.Markdown.Matrix;

/// <summary>What a piece of a 2D-code block is.</summary>
public static class MatrixKinds
{
    /// <summary>The whole body of the fence.</summary>
    public const string Block = "block";

    /// <summary>One line and the characters that ended it — a field, a comment, nothing, or what could not be read.</summary>
    public const string Line = "line";

    /// <summary>A <c>key: value</c> pair: its key, its colon, and its value when one was written.</summary>
    public const string Field = "field";

    public const string Key = "key";

    /// <summary>Everything after the colon, less the space either side of it.</summary>
    public const string Value = "value";
}

/// <summary>What a piece of a 2D-code block is <em>to</em> the piece holding it.</summary>
public static class MatrixRoles
{
    /// <summary>A field's value. Its key is <see cref="Ast.Roles.Name"/> and its colon <see cref="Ast.Roles.Separator"/>.</summary>
    public const string Value = "value";
}
