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
}
