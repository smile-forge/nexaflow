using System.Text.Json.Nodes;
using Nexaflow.Core.Services.ShellAi;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Core.Unit.ClientTools;

/// <summary>
/// The assistant opening a page: only with the parameters the page declares, and only on what the user has given it — a
/// file open in a tab, or one in a folder open in one — so a tab it opens shows nothing the user had not already shown it.
/// </summary>
[TestClass]
[CoversNode("ai-context-paths")]
public class OpenPageToolTests
{
    private readonly IShellServices _shell = Substitute.For<IShellServices>();

    private static readonly IReadOnlyList<PageParameter> PdfParameters =
    [
        new("path", "Full path to a .pdf file.", OnDisk: true),
        new("page", "1-based page to open at.", Required: false),
    ];

    private OpenPageTool Tool(PathScope scope)
        => new(_shell, kind => kind == "Pdf" ? PdfParameters : kind == "Help" ? [] : null, scope);

    private static JsonObject Opening(string kind, params (string Key, string Value)[] given)
    {
        var parameters = new JsonObject();
        foreach (var (key, value) in given) parameters[key] = value;
        return new JsonObject { ["page_kind"] = kind, ["params"] = parameters };
    }

    [TestMethod]
    public async Task AFileInAFolderOpenInATab_Opens()
    {
        var result = await Tool(new PathScope([], [@"C:\work"])).InvokeAsync(Opening("Pdf", ("path", @"C:\work\deep\a.pdf"), ("page", "3")), default);

        Assert.IsTrue(result.Success, result.ModelText);
        _shell.Received(1).OpenTab("Pdf", Arg.Is<Dictionary<string, string>>(d => d["path"] == @"C:\work\deep\a.pdf"));
    }

    [TestMethod]
    [DataRow(@"C:\a.pdf", DisplayName = "above the folder open")]
    [DataRow(@"C:\work\..\a.pdf", DisplayName = "a '..' climbing out")]
    [DataRow(@"C:\workshop\a.pdf", DisplayName = "a folder beside it")]
    [DataRow(@"a.pdf", DisplayName = "a relative path")]
    public async Task APlaceOutsideWhatTheUserGave_IsRefused_AndNothingOpens(string path)
    {
        var result = await Tool(new PathScope([], [@"C:\work"])).InvokeAsync(Opening("Pdf", ("path", path)), default);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.ModelText, "outside what the user has given you");
        _shell.DidNotReceiveWithAnyArgs().OpenTab(default!, default);
    }

    [TestMethod]
    public async Task AParameterThePageDoesNotDeclare_IsRefused_ForItCouldNameAnywhere()
    {
        var undeclared = await Tool(PathScope.None).InvokeAsync(Opening("Help", ("path", @"C:\Windows")), default);
        var unknown    = await Tool(PathScope.None).InvokeAsync(Opening("Nowhere"), default);

        Assert.IsTrue(undeclared.IsError);
        StringAssert.Contains(undeclared.ModelText, "takes no parameters");
        Assert.IsTrue(unknown.IsError);
        _shell.DidNotReceiveWithAnyArgs().OpenTab(default!, default);
    }

    [TestMethod]
    public async Task APageWithNothingOnDisk_OpensWithNothingGiven()
    {
        var result = await Tool(PathScope.None).InvokeAsync(new JsonObject { ["page_kind"] = "Help" }, default);

        Assert.IsTrue(result.Success, result.ModelText);
        _shell.Received(1).OpenTab("Help", null);
    }

    [TestMethod]
    public async Task AWebAddress_IsNoPlaceOnDisk_ButAFileAddressIs()
    {
        var tool = Tool(PathScope.None);

        var web  = await tool.InvokeAsync(Opening("Pdf", ("path", "https://example.com/a.pdf")), default);
        var file = await tool.InvokeAsync(Opening("Pdf", ("path", "file:///C:/Windows/a.pdf")), default);

        Assert.IsTrue(web.Success, web.ModelText);
        Assert.IsTrue(file.IsError, "a file: address names a place on disk, and is held to the scope");
    }
}
