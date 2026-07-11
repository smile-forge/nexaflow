using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Email.Model;
using Nexaflow.Features.Email.Reading;
using Nexaflow.Features.Email.Services;
using Nexaflow.IO.Common;
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

    public ObservableCollection<EmailHeader> AllHeaders { get; } = [];
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
            _exporter = new HtmlBodyExporter(doc);

            From = doc.From;
            To = doc.To;
            Cc = doc.Cc;
            Subject = doc.Subject;
            DateText = doc.Date?.LocalDateTime.ToString("f");
            PlainText = doc.TextBody ?? string.Empty;
            HtmlSource = doc.HtmlBody ?? string.Empty;

            foreach (var h in doc.Headers) AllHeaders.Add(h);

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
                Attachments.Add(new EmailAttachmentItem(a.EntryName, a.DisplayName, a.ContentType, a.Size));
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

    public void Dispose() => _exporter?.Dispose();
}
