namespace Nexaflow.Core.Help;

/// <summary>
/// One help page. <see cref="Topic"/> is the page kind it explains — its file name: <c>help/Text.md</c> is the Text
/// viewer's — <see cref="Title"/> its first heading, and <see cref="LogicalName"/> where it sits in the language pack
/// (<c>Nexaflow.Features.Text/help/Text.md</c>).
/// </summary>
internal sealed record HelpTopic(string Topic, string Title, string LogicalName)
{
    /// <summary>The pack folder the page's own pictures resolve against.</summary>
    public string Folder => LogicalName[..LogicalName.LastIndexOf('/')];
}

/// <summary>A help page ready to show. <see cref="MissingTopic"/> is the page kind that was asked for when it has no
/// help of its own, and the index is shown in its place.</summary>
internal sealed record HelpDocument(HelpTopic Topic, string Markdown, string? MissingTopic = null);
