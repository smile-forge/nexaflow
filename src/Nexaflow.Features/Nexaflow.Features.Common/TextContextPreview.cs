namespace Nexaflow.Features.Common;

/// <summary>
/// A preview of a page that is essentially text (text, code, JSON, logs, a table): a one-line summary and a snippet of the
/// content. The panel names the page, so the preview does not.
/// </summary>
/// <param name="Meta">One line, e.g. "1,240 lines · UTF-8 · unsaved edits".</param>
/// <param name="Body">A snippet of the content, capped by the page: the panel stays light.</param>
public sealed record TextContextPreview(string Meta, string Body);
