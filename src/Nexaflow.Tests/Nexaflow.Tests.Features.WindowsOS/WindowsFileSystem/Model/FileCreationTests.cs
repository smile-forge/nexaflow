using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem.Model;

/// <summary>
/// Making a new item, with no picker over it: the name prefilled for the kind, a name already taken refused, the kind's
/// extension added unless the name has one, and nothing made without a kind.
/// </summary>
[TestClass]
[CoversNode("winfs-create")]
public class FileCreationTests
{
    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = ModelHarness.Scratch();
        File.WriteAllText(Path.Combine(_scratch, "taken.txt"), "t");
    }

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    private static IFileCreateAction TextKind()
    {
        var kind = Substitute.For<IFileCreateAction>();
        kind.FileExtension.Returns(".txt");
        kind.DisplayName.Returns("Text Document");
        return kind;
    }

    [TestMethod]
    public async Task ChoosingAKind_PrefillsANameWithItsExtension()
    {
        var model = await ModelHarness.AtAsync(_scratch);

        model.Creation.Choose(TextKind());

        StringAssert.EndsWith(model.Creation.FileName, ".txt");
    }

    [TestMethod]
    public async Task ANameTheFolderHolds_CannotBeMade_AndAFreeOneCan()
    {
        var model = await ModelHarness.AtAsync(_scratch);
        model.Creation.Choose(TextKind());

        model.Creation.FileName = "taken";
        await model.Creation.NameChecked;
        Assert.IsTrue(model.Creation.NameExists);
        Assert.IsFalse(model.Creation.CanCreate);

        model.Creation.FileName = "fresh";
        await model.Creation.NameChecked;
        Assert.IsFalse(model.Creation.NameExists);
        Assert.IsTrue(model.Creation.CanCreate);
    }

    [TestMethod]
    public async Task TheItemIsMadeInTheOpenFolder_WithTheKindsExtension_UnlessTheNameHasOne()
    {
        var model = await ModelHarness.AtAsync(_scratch);
        var kind  = TextKind();
        model.Creation.Choose(kind);

        model.Creation.FileName = "fresh";
        Assert.IsTrue(await model.Creation.CreateAsync());
        model.Creation.FileName = "notes.md";
        Assert.IsTrue(await model.Creation.CreateAsync());

        await kind.Received().CreateItemAsync(Arg.Any<string>(), "fresh.txt");
        await kind.Received().CreateItemAsync(Arg.Any<string>(), "notes.md");
    }

    [TestMethod]
    public async Task WithNoKindChosen_NothingIsMade()
    {
        var model = await ModelHarness.AtAsync(_scratch);

        model.Creation.Choose(null);
        model.Creation.Recheck();

        Assert.IsFalse(model.Creation.CanCreate);
        Assert.IsFalse(await model.Creation.CreateAsync());
    }

    [TestMethod]
    public void OnThisPc_NoItemCanBeMade()
    {
        var model = ModelHarness.ThisPc();

        Assert.IsFalse(model.Location.CanCreateHere);
    }
}
