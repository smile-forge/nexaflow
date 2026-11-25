using System;
using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Extensions.Logging;
using Nexaflow.Core.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The binding errors an app under UI automation writes down for its journey: once each, as a signature that is the same
/// from run to run, in the file the harness reads — WPF's own, heard by the app's listener on its binding trace and handed to the
/// logs, where only the binding-error destination takes them.
/// <para>The last test binds a real element, so it needs an STA thread; it opens no window.</para>
/// </summary>
[TestClass]
[CoversNode("binding-error-log")]
public class BindingErrorLogTests
{
    private const string PathError =
        "BindingExpression path error: 'View' property not found on 'object' ''GenerateImageOptions' (HashCode=41962596)'. "
      + "BindingExpression:Path=Generation.View; DataItem='ImageStartViewModel' (HashCode=-12345); "
      + "target element is 'ContentControl' (Name=''); target property is 'Content' (type 'Object')";

    private const string NoAncestor =
        "Cannot find source for binding with reference 'RelativeSource FindAncestor, "
      + "AncestorType='System.Windows.Controls.ListView', AncestorLevel='1''. BindingExpression:Path=View.Columns; "
      + "DataItem=null; target element is 'GridViewRowPresenter' (Name=''); target property is 'Columns' (type 'GridViewColumnCollection')";

    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(TestTemp.Root, "nexaflow-binding-errors-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>Logs whose one destination is the binding-error log, in the test's folder.</summary>
    private ILoggerFactory NewLogs() => new LoggerFactory([new BindingErrorLogProvider(() => _dir)]);

    private string[] Written() =>
        File.Exists(Path.Combine(_dir, BindingErrorLogProvider.FileName)) ? File.ReadAllLines(Path.Combine(_dir, BindingErrorLogProvider.FileName)) : [];

    private static void Report(ILogger bindings, int id, string message) => bindings.LogWarning(new EventId(id), "{BindingError}", message);

    [TestMethod]
    public void ASignature_KeepsWhatWasBoundAndWhere_ButNotTheHashCodes()
    {
        Assert.AreEqual(
            "40: BindingExpression path error: 'View' property not found on 'object' ''GenerateImageOptions''. "
          + "BindingExpression:Path=Generation.View; DataItem='ImageStartViewModel'; "
          + "target element is 'ContentControl' (Name=''); target property is 'Content' (type 'Object')",
            BindingErrorLogProvider.SignatureOf(40, PathError));
    }

    [TestMethod]
    public void TheSameErrorFromTwoObjects_IsOneSignature()
    {
        Assert.AreEqual(BindingErrorLogProvider.SignatureOf(40, PathError),
                        BindingErrorLogProvider.SignatureOf(40, PathError.Replace("41962596", "7").Replace("-12345", "99")));
    }

    [TestMethod]
    public void EachErrorIsWrittenOnce_InTheFileTheHarnessReads()
    {
        var bindings = NewLogs().CreateLogger(BindingErrorLogProvider.Category);
        Report(bindings, 40, PathError);
        Report(bindings, 4, NoAncestor);
        Report(bindings, 40, PathError.Replace("41962596", "7"));

        CollectionAssert.AreEqual(
            new[] { BindingErrorLogProvider.SignatureOf(40, PathError), BindingErrorLogProvider.SignatureOf(4, NoAncestor) },
            Written());
        CollectionAssert.AreEqual(Written(), AppLaunchRecipe.ReadBindingErrors(_dir).ToArray(), "the harness reads where the app writes");
    }

    [TestMethod]
    public void WhatIsNotABindingError_IsNotWritten()
    {
        var logs = NewLogs();
        logs.CreateLogger("Nexaflow.Core.Services.FeatureCatalog").LogWarning(new EventId(40), "{BindingError}", PathError);
        logs.CreateLogger("Nexaflow.Core.App").LogError(new InvalidOperationException("a fault"), "unhandled on the UI thread");

        Assert.AreEqual(0, Written().Length, "the harness fails a journey on each line, so only WPF's binding errors are written");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void ABindingToAMemberTheObjectLacks_IsWrittenDown()
    {
        var trace = BindingErrorTrace.Attach(NewLogs());
        try
        {
            UiThread.Run(() =>
                new TextBlock().SetBinding(TextBlock.TextProperty, new Binding("NoSuchMember") { Source = new Uri("https://example.com") }));
        }
        finally { trace.Detach(); }

        var written = Written();
        Assert.AreEqual(1, written.Length, string.Join(Environment.NewLine, written));
        StringAssert.StartsWith(written[0], "40: BindingExpression path error: 'NoSuchMember' property not found on 'object' ''Uri''.");
        StringAssert.Contains(written[0], "target property is 'Text'");
    }
}
