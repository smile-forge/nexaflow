namespace Nexaflow.Features.Markdown.ViewModels;

/// <summary>A Markdown page's preview: its text as it stands, and the folder its pictures are found in.</summary>
public sealed record MarkdownContextPreview(string Markdown, string? Folder);
