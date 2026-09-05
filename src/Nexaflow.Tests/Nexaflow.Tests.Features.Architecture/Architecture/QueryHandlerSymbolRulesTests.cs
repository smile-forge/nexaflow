using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// <c>?</c> belongs to the shell's search route, and a feature that also declares it must answer only for
/// its own pages.
/// <para>
/// This is not a rule about global handlers in general — a handler that answers for no particular page is
/// a reasonable thing to write, and several do. It is a rule about this one symbol. The router narrows to
/// EVERY handler whose symbol matches and then scores them, so <c>?</c> is shared by construction: search
/// claims it on any <c>ISearchable</c> page, which is most of them. A feature handler that claims the same
/// character page-independently therefore ties with search wherever search applies.
/// </para>
/// <para>
/// The tie is silent, which is why this is a test rather than a convention.
/// <c>AIService.ScoreHandlers</c> awards a clear winner only on a &gt;0.2 gap, so two 1.0 scores produce
/// none: the bar shows no symbol, the query falls through to disambiguation, and <c>?</c> does nothing at
/// all on every searchable page. Nothing throws, nothing is logged, and the unit tests for both handlers
/// pass — it shipped that way once already.
/// </para>
/// Constructed without running a constructor, so a handler needing IShellServices is still checkable:
/// CanProcess is a decision about its arguments and must not need the instance's state to make it.
/// </summary>
[TestClass]
[NoCoverage("architecture guard")]
public class QueryHandlerSymbolRulesTests
{
    /// <summary>The shell's search route (<c>SearchQueryHandler</c>), which every searchable page answers.</summary>
    private const string SearchSymbol = "?";

    private static IEnumerable<Type> HandlerTypes()
        => Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.Features.*.dll")
            .SelectMany(p => Assembly.Load(Path.GetFileNameWithoutExtension(p)).GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IQueryHandler).IsAssignableFrom(t));

    [TestMethod]
    public void A_feature_sharing_the_search_symbol_answers_only_for_its_own_pages()
    {
        var offenders = new List<string>();

        foreach (var type in HandlerTypes())
        {
            if (RuntimeHelpers.GetUninitializedObject(type) is not IQueryHandler handler) continue;
            if (handler.Symbol != SearchSymbol) continue;       // any other symbol may be page-independent

            float score;
            // Reaching for the page without checking there is one breaks the same rule, more loudly.
            try { score = handler.CanProcess("anything", prefixed: true, pageVm: null); }
            catch (NullReferenceException) { continue; }

            if (score > 0f) offenders.Add($"{type.FullName} scored {score:0.##} with no page");
        }

        Assert.AreEqual(0, offenders.Count,
            $"'{SearchSymbol}' is the shell's search route and is shared, not owned: a feature handler " +
            "declaring it must gate CanProcess on its own page view-model. Claiming it for any page ties " +
            "with search at 1.0 on every searchable page, and a tie means the bar shows no symbol and the " +
            "query silently goes nowhere. Offenders: " + string.Join(", ", offenders));
    }
}
