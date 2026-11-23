using System;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Tests.Visuals.Viewing;

/// <summary>
/// Which view draws a model: the one named for its type, else for the nearest type it derives from, made through its one
/// constructor taking the model. A gap is named rather than drawn blank.
/// <para>Needs an STA thread to construct WPF elements; opens no window.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("vcommon-view-registry")]
public class ViewRegistryTests
{
    private class BaseModel;
    private sealed class DerivedModel : BaseModel;
    private sealed class OtherModel;

    private sealed class BaseView(BaseModel model) : UserControl
    {
        public BaseModel Model { get; } = model;
    }

    private sealed class DerivedView : UserControl
    {
        internal DerivedView(DerivedModel model) => Model = model;
        public DerivedModel Model { get; }
    }

    private sealed class TwoWaysView : UserControl
    {
        public TwoWaysView(BaseModel model) { }
        public TwoWaysView(DerivedModel model) { }
    }

    private sealed class NotAView(BaseModel model)
    {
        public BaseModel Model { get; } = model;
    }

    private static ViewRegistry Naming(params (Type Model, Type View)[] views)
        => new(model => Array.Find(views, entry => entry.Model == model).View);

    [TestMethod]
    public void AModelIsDrawnByTheViewNamedForItsType_ThroughAConstructorOfAnyAccess() => UiThread.Run(() =>
    {
        var registry = Naming((typeof(BaseModel), typeof(BaseView)), (typeof(DerivedModel), typeof(DerivedView)));
        var model = new DerivedModel();

        var view = registry.Create(model);

        Assert.IsInstanceOfType<DerivedView>(view, "the view named for the type itself wins over its base's");
        Assert.AreSame(model, ((DerivedView)view).Model);
        Assert.AreSame(model, ViewRegistry.ModelOf(view));
    });

    [TestMethod]
    public void AModelNoViewNames_IsDrawnByTheViewOfWhatItDerivesFrom() => UiThread.Run(() =>
    {
        var registry = Naming((typeof(BaseModel), typeof(BaseView)));

        var view = registry.Create(new DerivedModel());

        Assert.IsInstanceOfType<BaseView>(view);
        Assert.IsTrue(registry.Draws(typeof(DerivedModel)));
    });

    [TestMethod]
    public void AModelNothingDraws_IsNamedInTheFailure() => UiThread.Run(() =>
    {
        var registry = Naming((typeof(BaseModel), typeof(BaseView)));

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => registry.Create(new OtherModel()));

        StringAssert.Contains(failure.Message, nameof(OtherModel));
        Assert.IsFalse(registry.Draws(typeof(OtherModel)));
    });

    [TestMethod]
    public void AViewThatCouldBeMadeTwoWays_OrIsNoElement_IsRefused() => UiThread.Run(() =>
    {
        StringAssert.Contains(
            Assert.ThrowsExactly<InvalidOperationException>(() => Naming((typeof(DerivedModel), typeof(TwoWaysView))).Create(new DerivedModel())).Message,
            "exactly one constructor");
        StringAssert.Contains(
            Assert.ThrowsExactly<InvalidOperationException>(() => Naming((typeof(BaseModel), typeof(NotAView))).Create(new BaseModel())).Message,
            "not an element");
    });

    [TestMethod]
    public void AHost_DrawsItsModel_WithTheRegistryItsWindowHandsDown_OnceItJoinsIt() => UiThread.Run(() =>
    {
        var window = new StackPanel();
        var host = new ModelHost { Model = new BaseModel() };
        Assert.IsNull(host.Content, "outside a window that hands a registry down, nothing can draw it yet");

        ViewRegistry.SetRegistry(window, Naming((typeof(BaseModel), typeof(BaseView))));
        window.Children.Add(host);

        Assert.IsInstanceOfType<BaseView>(host.Content);
        var drawn = host.Content;
        ViewRegistry.SetRegistry(window, Naming((typeof(BaseModel), typeof(BaseView))));
        Assert.AreSame(drawn, host.Content, "a new registry for the same model keeps the view it has");

        host.Model = null;
        Assert.IsNull(host.Content);
    });

    [TestMethod]
    public void AHost_WhoseModelNothingDraws_SaysSo() => UiThread.Run(() =>
    {
        var host = new ModelHost();
        ViewRegistry.SetRegistry(host, Naming());

        host.Model = new OtherModel();

        StringAssert.Contains(((TextBlock)host.Content).Text, nameof(OtherModel));
    });

    [TestMethod]
    public void AHost_TakenOutOfItsWindowForAMoment_KeepsTheViewItHas() => UiThread.Run(() =>
    {
        var window = new StackPanel();
        ViewRegistry.SetRegistry(window, Naming((typeof(BaseModel), typeof(BaseView))));
        var host = new ModelHost { Model = new BaseModel() };
        window.Children.Add(host);
        var drawn = host.Content;

        window.Children.Remove(host);
        Assert.AreSame(drawn, host.Content, "re-laid out or moved, the view keeps what it shows: a result already reported stays shown");

        window.Children.Add(host);
        Assert.AreSame(drawn, host.Content);
    });
}
