using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.Features.Images.EditingFixtures;
using System.IO;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The recipe as it is kept: its JSON, the hidden sidecar beside the photo and the rules about when it is written,
/// deleted or left alone, the fingerprint that notices a photo changed under it, and the undo history above it.
/// </summary>
[TestClass]
public class ImageRecipeTests
{
    private static readonly ImageRecipe Edited = new()
    {
        Geometry = new RecipeGeometry { QuarterTurns = 1, StraightenDegrees = 3.5, Crop = new CropRect(10, 20, 300, 200), Aspect = AspectPreset.FourThree },
        Adjustments = new RecipeAdjustments { Exposure = 0.5, Saturation = -20 },
        Markup =
        [
            new MarkupArrow { From = new MarkupPoint(1, 2), To = new MarkupPoint(3, 4) },
            new MarkupText { Anchor = new MarkupPoint(5, 6), Text = "Hi", RotationDegrees = -90, Backing = 0x80000000 },
            new MarkupStroke { Points = [new(1, 1), new(2, 2)], Style = StrokeStyle.Highlighter },
            new MarkupBox { Corner = new MarkupPoint(0, 0), OppositeCorner = new MarkupPoint(9, 9), Fill = 0x40FF0000 },
        ],
        Resize = new RecipeResize { Mode = ResizeMode.Pixels, Width = 800, LockAspect = true },
        Export = new RecipeExport { StripMetadata = true, JpegQuality = 80 },
    };

    // ── JSON ────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-recipe-sidecar")]
    public void ARecipe_RoundTripsThroughItsJson_MarkupKindsAndAll()
    {
        var json = ImageRecipeJson.Serialize(Edited);
        StringAssert.Contains(json, "\"kind\": \"arrow\"");
        StringAssert.Contains(json, "\"aspect\": \"FourThree\"", "enums are written by name, so the file reads");

        var read = ImageRecipeJson.Deserialize(json);
        Assert.AreEqual(RecipeReadStatus.Read, read.Status);
        Assert.AreEqual(Edited, read.Recipe);
    }

    [TestMethod]
    [CoversNode("images-recipe-sidecar")]
    public void ANewerOrBrokenRecipe_IsRecognised()
    {
        Assert.AreEqual(RecipeReadStatus.Newer, ImageRecipeJson.Deserialize($"{{\"version\": {ImageRecipe.CurrentVersion + 1}}}").Status);
        Assert.AreEqual(RecipeReadStatus.Unreadable, ImageRecipeJson.Deserialize("{ not json").Status);
        Assert.AreEqual(RecipeReadStatus.Unreadable, ImageRecipeJson.Deserialize("[]").Status);
    }

    // ── The sidecar ─────────────────────────────────────────────────────────

    private static string PhotoIn(string dir) => CopyOfSample(dir, PlainSample);

    [TestMethod]
    [CoversNode("images-recipe-sidecar")]
    public void SavingWritesAHiddenSidecar_AndAnIdentityRecipeDeletesIt()
    {
        var dir = TempDirectory();
        try
        {
            var photo = PhotoIn(dir);
            using var store = new ImageRecipeStore(TimeSpan.FromMilliseconds(10));
            var sidecar = ImageRecipeStore.SidecarPath(photo);

            store.Save(photo, Edited);
            Assert.IsTrue(File.Exists(sidecar));
            Assert.IsTrue(File.GetAttributes(sidecar).HasFlag(FileAttributes.Hidden));
            Assert.IsFalse(File.Exists(sidecar[..^ImageRecipeStore.Extension.Length] + ImageRecipeStore.PendingExtension), "no swap file left behind");

            var loaded = store.Load(photo);
            Assert.AreEqual(RecipeLoadStatus.Loaded, loaded.Status);
            Assert.AreEqual(Edited with { Source = loaded.Recipe!.Source }, loaded.Recipe);
            Assert.IsNotNull(loaded.Recipe.Source, "a first write anchors the recipe to the photo");

            store.Save(photo, Edited with { Adjustments = new RecipeAdjustments { Exposure = 1 } });   // over a hidden file
            Assert.AreEqual(1, store.Load(photo).Recipe!.Adjustments.Exposure);

            store.Save(photo, ImageRecipe.Identity);
            Assert.IsFalse(File.Exists(sidecar), "a recipe that changes nothing is not worth a file");
            Assert.AreEqual(RecipeLoadStatus.None, store.Load(photo).Status);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-recipe-sidecar")]
    public void DebouncedWrites_LandOnce_WithTheLatestRecipe()
    {
        var dir = TempDirectory();
        try
        {
            var photo = PhotoIn(dir);
            using var store = new ImageRecipeStore(TimeSpan.FromHours(1));   // only a flush writes
            var first = Edited with { Adjustments = new RecipeAdjustments { Contrast = 10 } };
            var last  = Edited with { Adjustments = new RecipeAdjustments { Contrast = 20 } };

            store.SaveDebounced(photo, first);
            store.SaveDebounced(photo, last);
            Assert.IsFalse(File.Exists(ImageRecipeStore.SidecarPath(photo)), "nothing written while edits continue");
            Assert.AreEqual(20, store.Load(photo).Recipe!.Adjustments.Contrast, "a pending write is what loads");

            store.Flush(photo);
            var json = File.ReadAllText(ImageRecipeStore.SidecarPath(photo));
            Assert.AreEqual(20, ImageRecipeJson.Deserialize(json).Recipe!.Adjustments.Contrast);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-recipe-fingerprint")]
    public void APhotoChangedSinceItsRecipe_IsNoticed()
    {
        var dir = TempDirectory();
        try
        {
            var photo = PhotoIn(dir);
            using var store = new ImageRecipeStore();
            store.Save(photo, Edited);

            File.SetLastWriteTimeUtc(photo, DateTime.UtcNow.AddMinutes(5));
            Assert.AreEqual(RecipeLoadStatus.Loaded, store.Load(photo).Status, "touched but the same bytes: still this photo");

            File.WriteAllBytes(photo, EncodePng(Flat(16, 16, SkiaSharp.SKColors.Black)));
            var load = store.Load(photo);
            Assert.AreEqual(RecipeLoadStatus.SourceChanged, load.Status);
            Assert.IsNotNull(load.Recipe, "the edits are still offered, for the user to keep or discard");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-recipe-sidecar")]
    [CoversNode("images-edit-banner")]
    public void ASidecarFromANewerBuild_IsNeverOverwritten()
    {
        var dir = TempDirectory();
        try
        {
            var photo = PhotoIn(dir);
            var sidecar = ImageRecipeStore.SidecarPath(photo);
            var newer = $"{{\"version\": {ImageRecipe.CurrentVersion + 1}, \"somethingNew\": true}}";
            File.WriteAllText(sidecar, newer);

            using var services = new ImageEditingServices(new ImageRecipeStore(TimeSpan.FromMilliseconds(1)));
            using (var session = new ImageEditSession(photo, services))
            {
                Assert.AreEqual(RecipeLoadStatus.Newer, session.LoadStatus);
                Assert.IsFalse(session.CanPersist);
                session.History.Commit(Edited);   // edited for the session only
            }
            services.Recipes.Flush();
            Assert.AreEqual(newer, File.ReadAllText(sidecar));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-recipe-sidecar")]
    public void SidecarNames_PairWithTheirPhotos()
    {
        Assert.AreEqual(@"C:\a\b.jpg.nfedit", ImageRecipeStore.SidecarPath(@"C:\a\b.jpg"));
        Assert.AreEqual(@"C:\a\b.jpg", ImageRecipeStore.ImagePathOf(@"C:\a\b.jpg.nfedit"));
        Assert.AreEqual(@"C:\a\b.jpg", ImageRecipeStore.ImagePathOf(@"C:\a\b.jpg.nfedit.tmp"));
        Assert.IsTrue(ImageRecipeStore.IsSidecar(@"C:\a\B.JPG.NFEDIT"));
        Assert.IsFalse(ImageRecipeStore.IsSidecar(@"C:\a\b.jpg"));
        Assert.IsNull(ImageRecipeStore.ImagePathOf(@"C:\a\b.jpg"));
    }

    // ── A session over a photo ──────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-recipe")]
    [CoversNode("images-edit-revert")]
    public void ASessionKeepsItsEditsBesideThePhoto_AndRevertRemovesThem()
    {
        var dir = TempDirectory();
        try
        {
            var photo = PhotoIn(dir);
            using var services = new ImageEditingServices(new ImageRecipeStore(TimeSpan.FromHours(1)));

            using (var session = new ImageEditSession(photo, services))
            {
                Assert.IsTrue(session.CanPersist);
                session.History.Commit(Edited);
            }   // closing flushes
            Assert.IsTrue(File.Exists(ImageRecipeStore.SidecarPath(photo)));

            using (var reopened = new ImageEditSession(photo, services))
            {
                Assert.AreEqual(Edited.Geometry, reopened.Recipe.Geometry, "edits survive a reopen");
                reopened.Revert();
                Assert.IsTrue(reopened.History.CanUndo, "revert is undoable");
            }
            Assert.IsFalse(File.Exists(ImageRecipeStore.SidecarPath(photo)), "reverted to nothing, so no sidecar");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-recipe-fingerprint")]
    [CoversNode("images-edit-banner")]
    public void KeepingEditsForAChangedPhoto_ReanchorsThem()
    {
        var dir = TempDirectory();
        try
        {
            var photo = PhotoIn(dir);
            using var services = new ImageEditingServices(new ImageRecipeStore(TimeSpan.FromHours(1)));
            services.Recipes.Save(photo, Edited);
            File.WriteAllBytes(photo, EncodePng(Flat(16, 16, SkiaSharp.SKColors.Black)));

            using (var session = new ImageEditSession(photo, services))
            {
                Assert.AreEqual(RecipeLoadStatus.SourceChanged, session.LoadStatus);
                session.KeepEditsForChangedSource();
            }
            Assert.AreEqual(RecipeLoadStatus.Loaded, services.Recipes.Load(photo).Status, "anchored to the photo as it is now");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-recipe")]
    public void AnArchivedPhoto_IsEditedForTheSessionOnly()
    {
        using var services = new ImageEditingServices();
        using var session = new ImageEditSession(@"C:\no\such\archive.zip\inner\photo.jpg", services);
        Assert.IsFalse(session.IsFileBacked);
        Assert.IsFalse(session.CanPersist);
        session.History.Commit(Edited);
        Assert.AreEqual(Edited, session.Recipe);
    }

    // ── History ─────────────────────────────────────────────────────────────

    private static ImageRecipe Exposed(double ev) => new() { Adjustments = new RecipeAdjustments { Exposure = ev } };

    [TestMethod]
    [CoversNode("images-recipe-history")]
    [CoversNode("images-edit-history")]
    public void UndoAndRedo_WalkTheRecipes_AndANewEditDropsTheRedos()
    {
        var history = new RecipeHistory(ImageRecipe.Identity);
        history.Commit(Exposed(1));
        history.Commit(Exposed(2));

        Assert.IsTrue(history.Undo());
        Assert.AreEqual(Exposed(1), history.Current);
        Assert.IsTrue(history.CanRedo);
        Assert.IsTrue(history.Redo());
        Assert.AreEqual(Exposed(2), history.Current);

        history.Undo();
        history.Commit(Exposed(5));
        Assert.IsFalse(history.CanRedo, "a new edit forks the history");
        history.Undo();
        history.Undo();
        Assert.AreEqual(ImageRecipe.Identity, history.Current);
        Assert.IsFalse(history.Undo());

        history.Commit(ImageRecipe.Identity);
        Assert.IsTrue(history.CanRedo, "committing what is already current is no edit");
    }

    [TestMethod]
    [CoversNode("images-recipe-history")]
    public void AGesture_IsOneStep_OrNoneIfItEndsWhereItBegan()
    {
        var history = new RecipeHistory(ImageRecipe.Identity);
        history.BeginGesture();
        for (int i = 1; i <= 10; i++) history.Commit(Exposed(i / 10.0));
        history.EndGesture();

        Assert.AreEqual(Exposed(1), history.Current);
        Assert.IsTrue(history.Undo());
        Assert.AreEqual(ImageRecipe.Identity, history.Current, "ten slider ticks, one undo");

        history.BeginGesture();
        history.Commit(Exposed(0.3));
        history.Commit(ImageRecipe.Identity);
        history.EndGesture();
        Assert.IsFalse(history.CanUndo, "a drag that ended where it began adds nothing");

        history.BeginGesture();
        history.Commit(Exposed(0.7));
        history.CancelGesture();
        Assert.AreEqual(ImageRecipe.Identity, history.Current, "cancel puts it back");
    }

    [TestMethod]
    [CoversNode("images-recipe-history")]
    public void TheHistory_IsBounded_AndAnArrivingRecipeIsUndoable()
    {
        var history = new RecipeHistory(ImageRecipe.Identity, limit: 3);
        for (int i = 1; i <= 5; i++) history.Commit(Exposed(i));
        int steps = 0;
        while (history.Undo()) steps++;
        Assert.AreEqual(3, steps);

        history.Adopt(Exposed(9));
        Assert.IsTrue(history.Undo(), "another window's edit arrives as a step of its own");
    }
}
