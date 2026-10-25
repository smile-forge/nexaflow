using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A control that names no style still follows the theme: Styles.xaml gives each themed control type a keyless style
/// based on its themed one, so a view that forgets <c>Style="…"</c> gets the theme rather than the system look.
/// </summary>
[TestClass]
[CoversNode("theme-control-styles")]
public class DefaultControlStyleTests
{
    [TestMethod]
    [DataRow("ComboBox",   "DarkComboBox")]
    [DataRow("DatePicker", "DarkDatePicker")]
    [DataRow("Calendar",   "DarkCalendar")]
    public void AControlNamingNoStyleGetsTheThemedOne(string targetType, string themed)
    {
        var implicitStyle = ShippedStyles.Implicit(targetType);

        Assert.IsNotNull(implicitStyle, $"Styles.xaml has no keyless {targetType} style, so one naming no style falls back to the system look");
        Assert.AreEqual($"{{StaticResource {themed}}}", (string?)implicitStyle.Attribute("BasedOn"));
    }
}
