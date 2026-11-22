using System.Collections.Generic;
using NSubstitute;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Interaction;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>The AI bar's drops, put to the features' handlers.</summary>
[TestClass]
[CoversNode("chrome-drop-routing")]
public class ChatBarDropTargetTests
{
    private static readonly DropRequest File = new(new DragPayload((DragKinds.Files, new[] { @"C:\a.txt" })), default);

    private static IChatDropHandler Handler(string? text, bool handles = true)
    {
        var handler = Substitute.For<IChatDropHandler>();
        handler.AcceptedKinds.Returns([DragKinds.Files]);
        handler.CanHandle(Arg.Any<IPageViewModel?>()).Returns(handles);
        handler.BuildInsertText(Arg.Any<IDragPayload>(), Arg.Any<IPageViewModel?>()).Returns(text);
        return handler;
    }

    [TestMethod]
    public void TheFirstHandlerWithWords_IsTheOneInserted()
    {
        var inserted = new List<string>();
        var target = new ChatBarDropTarget(() => [Handler(null), Handler("first"), Handler("second")], () => null, inserted.Add);

        Assert.IsNotNull(target.OfferDrop(File));
        target.AcceptDrop(File);

        CollectionAssert.AreEqual(new[] { "first" }, inserted);
    }

    [TestMethod]
    public void AHover_NeverBuildsTheText()
    {
        var handler = Handler("text");
        var target = new ChatBarDropTarget(() => [handler], () => null, _ => { });

        target.OfferDrop(File);

        handler.DidNotReceiveWithAnyArgs().BuildInsertText(default!, default);
    }

    [TestMethod]
    public void WithNoHandlerForThePage_ItIsLeftToTheBox()
        => Assert.IsNull(new ChatBarDropTarget(() => [Handler("text", handles: false)], () => null, _ => { }).OfferDrop(File));
}
