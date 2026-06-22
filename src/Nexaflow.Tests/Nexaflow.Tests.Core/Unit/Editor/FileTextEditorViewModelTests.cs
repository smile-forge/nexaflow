using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Core.Infrastructure;
using Nexaflow.Visuals.Text.Editor;
using NSubstitute;

namespace Nexaflow.Tests.Core.Unit.Editor;

/// <summary>
/// Covers the read-write Edit Text view-model's save path (the data-loss-sensitive part): encoding/BOM
/// round-trip, EOL normalization, dirty tracking, and the over-size read-only guard. Runs under
/// <see cref="AsyncPump"/> because loading/saving mutate a thread-affine AvalonEdit <c>TextDocument</c>.
/// </summary>
[TestClass]
public class FileTextEditorViewModelTests
{
    private const long Big = 50L * 1024 * 1024;

    private static string Temp() => Path.Combine(Path.GetTempPath(), $"editvm_{Guid.NewGuid():N}.txt");

    private static IShellServices Shell()
    {
        var s = Substitute.For<IShellServices>();
        s.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
         .Returns(Task.FromResult(true));
        return s;
    }

    private static bool HasUtf8Bom(byte[] b) => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;

    [TestMethod]
    public void Save_RoundTripsUtf8NoBom() => AsyncPump.Run(async () =>
    {
        var path = Temp();
        File.WriteAllText(path, "alpha\nbeta\n", new UTF8Encoding(false));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), Big);
            await vm.LoadAsync();
            Assert.IsFalse(vm.IsReadOnlyMode);
            Assert.AreEqual("alpha\nbeta\n", vm.Document.Text);
            Assert.IsFalse(vm.IsDirty);

            vm.Document.Text = "alpha\nbeta\ngamma\n";
            Assert.IsTrue(vm.IsDirty);

            await vm.SaveCommand.ExecuteAsync(null);

            Assert.IsFalse(vm.IsDirty);
            Assert.IsFalse(HasUtf8Bom(File.ReadAllBytes(path)));
            Assert.AreEqual("alpha\nbeta\ngamma\n", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    public void Save_PreservesBom_WhenOriginalHadBom() => AsyncPump.Run(async () =>
    {
        var path = Temp();
        File.WriteAllText(path, "x\n", new UTF8Encoding(true)); // BOM
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), Big);
            await vm.LoadAsync();
            Assert.AreEqual("UTF-8 with BOM", vm.SelectedEncoding.Name);

            vm.Document.Text = "x\ny\n";
            await vm.SaveCommand.ExecuteAsync(null);

            Assert.IsTrue(HasUtf8Bom(File.ReadAllBytes(path)), "BOM should be re-emitted on save");
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    public void OverSizeLimit_OpensReadOnly_AndCannotSave() => AsyncPump.Run(async () =>
    {
        var path = Temp();
        File.WriteAllText(path, new string('a', 5000));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), maxEditableBytes: 1000);
            await vm.LoadAsync();

            Assert.IsTrue(vm.IsReadOnlyMode);
            Assert.AreEqual(string.Empty, vm.Document.Text);
            Assert.IsFalse(vm.SaveCommand.CanExecute(null));
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    public void CommandGroups_HideLineReorderingForCode() => AsyncPump.Run(async () =>
    {
        using var code = new FileTextEditorViewModel("snippet.cs", Shell(), Big);
        using var text = new FileTextEditorViewModel("notes.txt", Shell(), Big);
        await Task.CompletedTask;

        static bool OffersSort(FileTextEditorViewModel vm) =>
            vm.CommandGroups.SelectMany(g => g.Commands).Any(c => c.Name.Contains("Sort"));

        Assert.IsFalse(OffersSort(code), "code files must not offer line sorting");
        Assert.IsTrue(OffersSort(text), "text files should offer line sorting");
    });

    [TestMethod]
    public void Eol_NormalizedToCrlfOnSave() => AsyncPump.Run(async () =>
    {
        var path = Temp();
        File.WriteAllText(path, "a\nb\n", new UTF8Encoding(false));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), Big);
            await vm.LoadAsync();
            vm.SelectedEol = vm.AvailableEols.First(e => e.Eol == LineEnding.CrLf);

            vm.Document.Text = "a\nb\nc\n"; // marks dirty
            await vm.SaveCommand.ExecuteAsync(null);

            Assert.AreEqual("a\r\nb\r\nc\r\n", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    });
}
