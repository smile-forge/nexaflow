using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Email.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The email viewer as a searchable page. Its scope is what the message currently shows: the body pane, the
/// raw headers only while open, and every attachment — by name, by the text inside it, and into any message
/// it carries.
/// </summary>
[TestClass]
[CoversNode("email-find")]
public sealed class EmailSearchableTests : SearchableContentConformanceTests
{
    // "alpha42" appears only in lower case, in the Subject and the body; "alpha\d+" matches but never
    // appears verbatim.
    private const string Message =
        "From: Ada Lovelace <ada@example.com>\r\n" +
        "To: Grace Hopper <grace@example.com>\r\n" +
        "Subject: Notes on the alpha42 build\r\n" +
        "Date: Mon, 3 Aug 2026 10:00:00 +0000\r\n" +
        "Content-Type: text/plain; charset=utf-8\r\n" +
        "\r\n" +
        "The importer was fixed in alpha42.\r\n" +
        "Nothing else to report.\r\n";

    // A richer message: a body word, a text attachment with its own word, and a forwarded message whose
    // subject carries a third — each must be found and attributed to the right place.
    private const string Rich =
        "From: a@example.com\r\n" +
        "To: b@example.com\r\n" +
        "Subject: outer\r\n" +
        "MIME-Version: 1.0\r\n" +
        "Content-Type: multipart/mixed; boundary=\"BOUND\"\r\n" +
        "\r\n" +
        "--BOUND\r\n" +
        "Content-Type: text/plain; charset=utf-8\r\n\r\n" +
        "the body mentions bodyword here.\r\n" +
        "--BOUND\r\n" +
        "Content-Type: text/plain; charset=utf-8\r\n" +
        "Content-Disposition: attachment; filename=\"notes.txt\"\r\n\r\n" +
        "inside the attachment is attachword.\r\n" +
        "--BOUND\r\n" +
        "Content-Type: message/rfc822\r\n" +
        "Content-Disposition: attachment; filename=\"forwarded.eml\"\r\n\r\n" +
        "From: c@example.com\r\nSubject: nestedword here\r\n\r\nnested body.\r\n" +
        "--BOUND--\r\n";

    private readonly List<string> _paths = [];

    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Func<Task<SearchOutcome>>>())
             .Returns(ci => ci.Arg<Func<Task<SearchOutcome>>>()());
        return shell;
    }

    private EmailViewModel Open(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "nexasearch_" + Guid.NewGuid().ToString("N") + ".eml");
        File.WriteAllText(path, content);
        _paths.Add(path);
        return new EmailViewModel(path, Shell());
    }

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Open(Message));

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var p in _paths) if (File.Exists(p)) File.Delete(p);
    }

    protected override string Snapshot(ISearchable page)
    {
        var vm = (EmailViewModel)page;
        return $"{vm.SearchMatchCount}|{vm.IsSearchActive}|{vm.CurrentSearchTerm}|{vm.BodyView}|{vm.HeadersExpanded}";
    }

    // ── Headers are searched only while the raw list is open ──────────────────

    [TestMethod]
    public void Headers_AreSearchedOnlyWhenExpanded() => WithPage(async page =>
    {
        var vm = (EmailViewModel)page;
        Assert.IsFalse(vm.HeadersExpanded, "the raw header list starts collapsed");

        // "Notes" is only in the Subject header, not the body.
        var collapsed = await vm.SearchAsync(new SearchRequest("Notes"), display: false, default);
        Assert.AreEqual(0, collapsed.MatchCount, "a collapsed header list is out of scope");

        vm.HeadersExpanded = true;
        var expanded = await vm.SearchAsync(new SearchRequest("Notes"), display: false, default);
        Assert.AreEqual(1, expanded.MatchCount, "with the list open, the Subject header is searched");
        Assert.IsTrue(expanded.Hits.Single().Id.StartsWith("header:"));
    });

    [TestMethod]
    public void DisplayingSearch_DoesNotOpenTheHeaders() => WithPage(async page =>
    {
        var vm = (EmailViewModel)page;
        Assert.IsFalse(vm.HeadersExpanded);

        await vm.SearchAsync(new SearchRequest("alpha42"), display: true, default);

        Assert.IsFalse(vm.HeadersExpanded, "the search reads what's shown; it never forces the headers open");
    });

    [TestMethod]
    public void DisplayingSearch_HighlightsTheMatchingHeaderRow_WhenOpen() => WithPage(async page =>
    {
        var vm = (EmailViewModel)page;
        vm.HeadersExpanded = true;

        await vm.SearchAsync(new SearchRequest("alpha42"), display: true, default);

        var subject = vm.AllHeaders.Single(h => h.Field.Equals("Subject", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(subject.Highlighted, "the header holding the match glows");
        Assert.IsTrue(vm.AllHeaders.Where(h => h != subject).All(h => !h.Highlighted), "only the match glows");
    });

    // ── Body is searched, and stays on the shown pane ─────────────────────────

    [TestMethod]
    public void Body_IsSearchedAndHitsAreLabelled() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(new SearchRequest("alpha42"), display: false, default);

        Assert.AreEqual(1, outcome.MatchCount, "one body line mentions alpha42 (headers are collapsed)");
        Assert.IsTrue(outcome.Hits.Single().Id.StartsWith("body:"));
    });

    // ── Attachments: name, text contents, and nested messages ─────────────────

    [TestMethod]
    public void Attachment_IsMatchedByName() => WithPage(async _ =>
    {
        var vm = Open(Rich);
        var outcome = await vm.SearchAsync(new SearchRequest("notes"), display: false, default);

        Assert.IsTrue(outcome.Hits.Any(h => h.Id.StartsWith("attachment:")), "notes.txt matches by name");
    });

    [TestMethod]
    public void Attachment_IsMatchedByItsTextContents() => WithPage(async _ =>
    {
        var vm = Open(Rich);
        var outcome = await vm.SearchAsync(new SearchRequest("attachword"), display: false, default);

        Assert.AreEqual(1, outcome.MatchCount, "the word is only inside the attachment's text");
        Assert.IsTrue(outcome.Hits.Single().Id.StartsWith("attachment:"),
            "a text attachment is searched by its contents, not just its name");
    });

    [TestMethod]
    public void Attachment_IsMatchedInsideACarriedMessage() => WithPage(async _ =>
    {
        var vm = Open(Rich);
        var outcome = await vm.SearchAsync(new SearchRequest("nestedword"), display: false, default);

        Assert.AreEqual(1, outcome.MatchCount, "the word is only in the forwarded message's subject");
        Assert.IsTrue(outcome.Hits.Single().Id.StartsWith("attachment:"),
            "the search descends into a message/rfc822 attachment");
    });

    [TestMethod]
    public void DisplayingSearch_HighlightsTheMatchingAttachmentTile() => WithPage(async _ =>
    {
        var vm = Open(Rich);
        await vm.SearchAsync(new SearchRequest("attachword"), display: true, default);

        var notes = vm.Attachments.Single(a => a.DisplayName == "notes.txt");
        Assert.IsTrue(notes.Highlighted, "the tile whose contents matched glows");
        Assert.IsTrue(vm.Attachments.Where(a => a != notes).All(a => !a.Highlighted));
    });

    // ── Nothing-found and clearing ────────────────────────────────────────────

    [TestMethod]
    public void SearchThatMatchesNothing_IsAResultNotAFailure() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(new SearchRequest("nowherenearthismessage"), display: false, default);

        Assert.IsFalse(outcome.Failed, "'no matches' is an answer; only an unrunnable search is a failure");
        Assert.AreEqual(0, outcome.MatchCount);
    });

    [TestMethod]
    public void ClearingSearch_DropsEveryHighlight() => WithPage(async _ =>
    {
        var vm = Open(Rich);
        vm.HeadersExpanded = true;
        await vm.SearchAsync(new SearchRequest("outer"), display: true, default);   // subject header
        Assert.IsTrue(vm.IsSearchActive);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.IsTrue(vm.AllHeaders.All(h => !h.Highlighted));
        Assert.IsTrue(vm.Attachments.All(a => !a.Highlighted));
    });
}
