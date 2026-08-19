using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;                 // IShellServices
using Nexaflow.Features.Images.ViewModels;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// Covers the image viewer's AI-integration surface: honest get_context (format / pixel size / file size /
/// position, not just the mode), the file-scoped security context, and the read + navigate client tools driven
/// exactly as the conversation hub drives them. The vision tool <c>capture_image</c> is implemented but not
/// exercised here — it PNG-encodes a WPF <c>BitmapSource</c>, which isn't reliable off an STA/interactive
/// desktop; the UI journey covers the real pixel path.
/// </summary>
[TestClass]
public class ImagesAiTests
{
    /// <summary>A shell whose RunOnUiAsync runs the action inline — navigation marshals through it, so a
    /// bare substitute (which swallows the delegate) would leave CurrentIndex unchanged.</summary>
    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    [TestMethod]
    [CoversNode("images-ai-act")]
    [CoversNode("images-ai-context")]
    public async Task AiTools_InfoAndNavigation_ThroughToolSurface()
    {
        // Three real, on-disk sample images so format / size / position are all genuine.
        var paths = TestSampleData.Files("images").Take(3).ToArray();
        var vm = new ImageViewModel(paths, RunningShell());
        try
        {
            // Scope: two image tabs on different files stay distinguishable when pinned together.
            Assert.AreEqual(paths[0], vm.GetSecurityContext());

            // Context is honest about the actual image, not merely the sub-view.
            var ctx = vm.GetContext();
            StringAssert.Contains(ctx, vm.CurrentFileName);
            StringAssert.Contains(ctx, "1 of 3");                // index + count

            var tools = vm.GetClientTools();
            CollectionAssert.AreEquivalent(
                new[] { "capture_image", "get_image_info", "next_image", "previous_image" },
                tools.Select(t => t.Name).ToArray(),
                "the Images AI act tool surface changed — update the tree's images-ai-act leaves to match");

            // get_image_info: filename + position always available (dimensions are best-effort).
            var info = tools.Single(t => t.Name == "get_image_info");
            var i = await info.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(i.IsError);
            StringAssert.Contains(i.ModelText, vm.CurrentFileName);
            StringAssert.Contains(i.ModelText, "1 of 3");

            // next_image advances the viewer (data now flows back to the model as the new position).
            var next = tools.Single(t => t.Name == "next_image");
            var n = await next.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(n.IsError);
            Assert.AreEqual(1, vm.CurrentIndex);
            StringAssert.Contains(n.ModelText, "2 of 3");

            // previous_image steps back.
            var prev = tools.Single(t => t.Name == "previous_image");
            await prev.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.AreEqual(0, vm.CurrentIndex);

            // …and clamps at the first image (manual navigation never wraps).
            var atFirst = await prev.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(atFirst.IsError);
            Assert.AreEqual(0, vm.CurrentIndex);
            StringAssert.Contains(atFirst.ModelText, "first");
        }
        finally { vm.Dispose(); }
    }
}
