using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Email.Model;
using Nexaflow.Features.Email.Reading;
using Nexaflow.Features.Email.Services;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Features.Email.ViewModels;

/// <summary>Which body representation the reading pane shows.</summary>
internal enum EmailBodyView { Rendered, PlainText, HtmlSource }

/// <summary>
/// The Email viewer page: an envelope header (essential fields always visible, the full raw header list
/// behind an expander), the body (HTML rendered via <see cref="HtmlToMarkdown"/> into a markdown view, or
/// plain text, or the raw HTML source), and the attachment strip. Reads the file through the VFS so a
/// <c>.eml</c> inside an archive opens too; attachments open in their own viewer via
/// <see cref="IShellServices.HandleObject"/>.
/// </summary>
internal sealed partial class EmailViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly IShellServices _shell;
    private readonly string _path;
    private HtmlBodyExporter? _exporter;
    private EmailDocument? _doc;

    /// <summary>Largest attachment (bytes) <c>read_attachment</c> will return inline before refusing.</summary>
    private const long MaxReadableAttachmentBytes = 256 * 1024;

    [ObservableProperty] private string? _from;
    [ObservableProperty] private string? _to;
    [ObservableProperty] private string? _cc;
    [ObservableProperty] private string? _subject;
    [ObservableProperty] private string? _dateText;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _renderedMarkdown = string.Empty;
    [ObservableProperty] private string _plainText = string.Empty;
    [ObservableProperty] private string _htmlSource = string.Empty;
    [ObservableProperty] private int _inlineImageCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenderedView))]
    [NotifyPropertyChangedFor(nameof(IsPlainTextView))]
    [NotifyPropertyChangedFor(nameof(IsHtmlSourceView))]
    private EmailBodyView _bodyView = EmailBodyView.Rendered;

    [ObservableProperty] private bool _headersExpanded;

    public ObservableCollection<EmailHeaderItem> AllHeaders { get; } = [];
    public ObservableCollection<EmailAttachmentItem> Attachments { get; } = [];

    /// <summary>Folder the rendered view resolves inline (<c>cid:</c>) images against; null when there are none.</summary>
    public string? MarkdownBaseDirectory { get; private set; }

    public bool IsRenderedView   => BodyView == EmailBodyView.Rendered;
    public bool IsPlainTextView  => BodyView == EmailBodyView.PlainText;
    public bool IsHtmlSourceView => BodyView == EmailBodyView.HtmlSource;

    public bool HasHtmlBody   => _exporter?.HasHtmlBody ?? false;
    public bool HasPlainText  => PlainText.Length > 0;
    public bool HasAttachments => Attachments.Count > 0;
    public bool HasCc         => !string.IsNullOrEmpty(Cc);
    public bool HasError      => ErrorMessage is not null;
    public bool HasInlineImages => InlineImageCount > 0;

    public EmailViewModel(string path, IShellServices shell)
    {
        _path = path;
        _shell = shell;
        Load();
    }

    private void Load()
    {
        try
        {
            using var stream = VirtualFileSystem.Instance.OpenRead(_path);
            var doc = EmailDocumentReader.Instance.Read(stream, Path.GetFileName(_path));
            _doc = doc;
            _exporter = new HtmlBodyExporter(doc);

            From = doc.From;
            To = doc.To;
            Cc = doc.Cc;
            Subject = doc.Subject;
            DateText = doc.Date?.LocalDateTime.ToString("f");
            PlainText = doc.TextBody ?? string.Empty;
            HtmlSource = doc.HtmlBody ?? string.Empty;

            foreach (var h in doc.Headers) AllHeaders.Add(new EmailHeaderItem(h.Field, h.Value));

            if (!string.IsNullOrEmpty(doc.HtmlBody))
            {
                RenderedMarkdown = HtmlToMarkdown.Convert(_exporter.RewriteCidToLocal(doc.HtmlBody));
                MarkdownBaseDirectory = _exporter.InlineBaseDirectory;
                BodyView = EmailBodyView.Rendered;
            }
            else if (!string.IsNullOrEmpty(doc.TextBody))
            {
                // Plain text renders acceptably as markdown text; keep the same rendered pane.
                RenderedMarkdown = doc.TextBody!;
                BodyView = EmailBodyView.Rendered;
            }

            var inline = 0;
            foreach (var a in doc.Attachments)
            {
                if (a.IsInline) { inline++; continue; }
                Attachments.Add(new EmailAttachmentItem(a));
            }
            InlineImageCount = inline;

            OnPropertyChanged(nameof(HasHtmlBody));
            OnPropertyChanged(nameof(HasPlainText));
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(HasCc));
            OnPropertyChanged(nameof(HasInlineImages));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't open this email: {ex.Message}";
            OnPropertyChanged(nameof(HasError));
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Opens an attachment in its own default viewer via the shell — the same path a double-click
    /// takes anywhere else. The attachment resolves as a VFS entry inside this email.</summary>
    [RelayCommand]
    private void OpenAttachment(EmailAttachmentItem? item)
    {
        if (item is null) return;
        var virtualPath = Path.Combine(_path, item.EntryName);
        if (!_shell.HandleObject(virtualPath))
            _shell.ShowError($"Couldn't open attachment '{item.DisplayName}'.");
    }

    /// <summary>Full-fidelity view: writes the HTML body (with inline images) to a temp file and opens it in
    /// the shell's WebView2 browser tab.</summary>
    [RelayCommand]
    private void OpenInBrowser()
    {
        if (_exporter is null) return;
        try { _shell.HandleObject(_exporter.ExportBrowserHtml()); }
        catch (Exception ex) { _shell.ShowError($"Couldn't open the browser view: {ex.Message}"); }
    }

    [RelayCommand] private void ShowRendered()   => BodyView = EmailBodyView.Rendered;
    [RelayCommand] private void ShowPlainText()  => BodyView = EmailBodyView.PlainText;
    [RelayCommand] private void ShowHtmlSource() => BodyView = EmailBodyView.HtmlSource;
    [RelayCommand] private void ToggleHeaders()  => HeadersExpanded = !HeadersExpanded;

    // ── IPageViewModel ─────────────────────────────────────────────────────────

    public string GetContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Viewing an email message.");
        if (Subject is not null) sb.AppendLine($"Subject: {Subject}");
        if (From is not null) sb.AppendLine($"From: {From}");
        if (To is not null) sb.AppendLine($"To: {To}");
        if (Cc is not null) sb.AppendLine($"Cc: {Cc}");
        if (DateText is not null) sb.AppendLine($"Date: {DateText}");
        if (Attachments.Count > 0)
            sb.AppendLine($"Attachments ({Attachments.Count}): {string.Join(", ", Attachments.Select(a => a.DisplayName))}");

        var body = HasPlainText ? PlainText : RenderedMarkdown;
        if (body.Length > 0)
        {
            sb.AppendLine("Body:");
            sb.AppendLine(body.Length > 4000 ? body[..4000] + "…" : body);
        }
        return sb.ToString();
    }

    public string? GetAiSystemPromptGuidance() =>
        "The user is reading an email. You can summarise it, draft a reply, or explain its attachments. " +
        "You cannot send mail from here.";

    /// <summary>The email file's path — the scope these read-only tools act within, and what
    /// distinguishes two email tabs pinned together as AI context.</summary>
    public string? GetSecurityContext() => _path;

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        // The Email viewer is deliberately read-only: no send / reply / forward. Every tool is a pure
        // read the context text couldn't carry (the full uncapped body, the attachment list, a text
        // attachment's contents) — all auto-run as SafeOperation.
        new DelegateClientTool(
            "read_email",
            "Return the full email — every header plus the complete body text, without the length cap the "
          + "ambient context applies. Use this to read the whole message.",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                if (_doc is null) return Task.FromResult(ToolResult.Error("No email is loaded."));
                var sb = new StringBuilder();
                foreach (var h in AllHeaders) sb.AppendLine($"{h.Field}: {h.Value}");
                sb.AppendLine();
                var body = HasPlainText ? PlainText : RenderedMarkdown;
                sb.Append(body.Length > 0 ? body : "(this message has no text body)");
                return Task.FromResult(ToolResult.Ok($"read '{Subject ?? Path.GetFileName(_path)}'", sb.ToString()));
            }),

        new DelegateClientTool(
            "list_attachments",
            "List the message's attachments — each file's name, content type and size — plus how many inline "
          + "images are embedded in the body.",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                if (_doc is null) return Task.FromResult(ToolResult.Error("No email is loaded."));
                if (Attachments.Count == 0 && InlineImageCount == 0)
                    return Task.FromResult(ToolResult.Ok("no attachments", "This email has no attachments."));
                var sb = new StringBuilder();
                foreach (var a in Attachments)
                    sb.AppendLine($"- {a.DisplayName} ({a.ContentType}, {a.SizeText})");
                if (InlineImageCount > 0)
                    sb.AppendLine($"({InlineImageCount} inline image(s) embedded in the body.)");
                return Task.FromResult(ToolResult.Ok($"{Attachments.Count} attachment(s)", sb.ToString()));
            }),

        new DelegateClientTool(
            "read_attachment",
            "Return a text attachment's contents by name (as listed by list_attachments). Text parts only — "
          + "binary attachments (images, PDFs, Office files) and very large files are refused; open those in "
          + "their own viewer instead.",
            [new ClientToolParameter("name", "The attachment's file name, e.g. \"notes.txt\".")],
            ToolSafety.SafeOperation,
            (args, _) =>
            {
                if (_doc is null) return Task.FromResult(ToolResult.Error("No email is loaded."));
                var name = ToolArgs.Str(args, "name", "file", "filename", "attachment");
                if (string.IsNullOrEmpty(name))
                    return Task.FromResult(ToolResult.Error("No attachment 'name' provided."));

                var att = _doc.Attachments.FirstOrDefault(a => !a.IsInline &&
                    (string.Equals(a.DisplayName, name, StringComparison.OrdinalIgnoreCase)
                  || string.Equals(a.EntryName,   name, StringComparison.OrdinalIgnoreCase)));
                if (att is null)
                    return Task.FromResult(ToolResult.Error($"No attachment named '{name}'."));

                if (!IsTextContentType(att.ContentType))
                    return Task.FromResult(ToolResult.Error(
                        $"'{att.DisplayName}' is {att.ContentType} — not text. Open it in its own viewer instead."));
                if (att.Size > MaxReadableAttachmentBytes)
                    return Task.FromResult(ToolResult.Error(
                        $"'{att.DisplayName}' is {SizeFormatter.FormatBytes(att.Size)} — too large to read inline "
                      + $"(limit {SizeFormatter.FormatBytes(MaxReadableAttachmentBytes)})."));

                var text = Encoding.UTF8.GetString(att.Content);
                return Task.FromResult(ToolResult.Ok($"read '{att.DisplayName}'", text));
            })
    ];

    /// <summary>True for a content type whose bytes are readable as text (so <c>read_attachment</c> may
    /// decode them). Covers <c>text/*</c>, structured <c>+xml</c>/<c>+json</c> types, a short list of known
    /// textual application types, and embedded messages (<c>message/rfc822</c>).</summary>
    private static bool IsTextContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var ct = contentType.Split(';')[0].Trim().ToLowerInvariant();
        if (ct.StartsWith("text/", StringComparison.Ordinal)) return true;
        if (ct.EndsWith("+xml", StringComparison.Ordinal) || ct.EndsWith("+json", StringComparison.Ordinal)) return true;
        return ct is "application/json" or "application/xml" or "application/xhtml+xml"
                  or "application/javascript" or "application/ecmascript"
                  or "application/x-yaml" or "application/yaml" or "application/x-sh"
                  or "message/rfc822";
    }

    public void Dispose() => _exporter?.Dispose();
}
