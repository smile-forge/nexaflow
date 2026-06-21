using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Providers.Local.ServerTools;

namespace Nexaflow.Tests.Providers.Unit;

[TestClass]
public class CalculatorServerToolTests
{
    private static Task<string> Eval(string expr) =>
        new CalculatorServerTool().InvokeAsync(
            new Dictionary<string, object?> { ["expression"] = expr }, CancellationToken.None);

    [TestMethod]
    public async Task Multiplies_ExactInteger()
        => StringAssert.Contains(await Eval("18432 * 977"), "= 18008064");

    [TestMethod]
    public async Task Honours_Precedence()
        => StringAssert.Contains(await Eval("2 + 3 * 4"), "= 14");

    [TestMethod]
    public async Task Honours_Parentheses()
        => StringAssert.Contains(await Eval("(2 + 3) * 4"), "= 20");

    [TestMethod]
    public async Task Supports_Power()
        => StringAssert.Contains(await Eval("2 ^ 10"), "= 1024");

    [TestMethod]
    public async Task Missing_Expression_ReturnsError()
        => StringAssert.Contains(
            await new CalculatorServerTool().InvokeAsync(new Dictionary<string, object?>(), CancellationToken.None),
            "Error");

    [TestMethod]
    public async Task Garbage_ReturnsError()
        => StringAssert.Contains(await Eval("2 +* 3"), "Error");
}
