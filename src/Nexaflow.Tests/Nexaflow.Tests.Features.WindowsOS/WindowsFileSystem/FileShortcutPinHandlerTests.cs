using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.RibbonHandlers;
using Nexaflow.Interaction;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>A file dropped on the ribbon, kept there as a shortcut to it.</summary>
[TestClass]
[CoversNode("ribbon-pin-file")]
public class FileShortcutPinHandlerTests
{
    private static FileShortcutPinHandler Handler(IShellServices shell) => new(shell, VirtualFileSystem.Instance);

    [TestMethod]
    public void EachFileDropped_IsAButtonOfItsOwn_NamedAsItIs()
    {
        var pins = Handler(Substitute.For<IShellServices>())
            .Pin(new DragPayload((DragKinds.Files, new[] { @"C:\docs\report.pdf", @"C:\docs\Projects\", @"D:\" })));

        CollectionAssert.AreEqual(new[] { "report.pdf", "Projects", "D:" }, pins.Select(p => p.Label).ToArray());
        Assert.AreEqual(@"C:\docs\report.pdf", pins[0].PageParams![FileShortcutPinHandler.KeyPath]);
        Assert.AreNotEqual(pins[0].Icon, pins[1].Icon, "a folder reads as one");
    }

    [TestMethod]
    public void AFileThatIsOnlyContent_IsNoShortcut()
    {
        IReadOnlyList<CarriedFile> content = [new CarriedFile("attached.txt", false, _ => Assert.Fail("not to be written out"))];
        var handler = Handler(Substitute.For<IShellServices>());
        var payload = new DragPayload((DragKinds.FileContents, content));

        Assert.IsFalse(payload.ContainsAny(handler.AcceptedKinds), "it is never offered one");
        Assert.AreEqual(0, handler.Pin(payload).Count);
    }

    [TestMethod]
    public async Task Clicked_ItOpensAsADoubleClickWould()
    {
        var file = TestTemp.CreateFile(".txt");
        var shell = Substitute.For<IShellServices>();
        var opened = new TaskCompletionSource<object>();
        shell.When(s => s.HandleObject(Arg.Any<object>())).Do(call => opened.TrySetResult(call.Arg<object>()));

        var path = file.ToString()!;
        Handler(shell).Execute(new() { [FileShortcutPinHandler.KeyPath] = path }, Substitute.For<IRibbonExecutionContext>());

        Assert.AreEqual(path, await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [TestMethod]
    public async Task Clicked_AfterTheFileHasGone_ItOffersToGo()
    {
        var context = Substitute.For<IRibbonExecutionContext>();
        var asked = new TaskCompletionSource();
        context.When(c => c.ShowConfirmation(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action>(), Arg.Any<Action?>()))
               .Do(_ => asked.TrySetResult());
        var shell = Substitute.For<IShellServices>();

        var gone = Path.Combine(TestTemp.Root, $"gone-{Guid.NewGuid():N}.txt");
        Handler(shell).Execute(new() { [FileShortcutPinHandler.KeyPath] = gone }, context);

        await asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        shell.DidNotReceiveWithAnyArgs().HandleObject(default!);
    }
}
