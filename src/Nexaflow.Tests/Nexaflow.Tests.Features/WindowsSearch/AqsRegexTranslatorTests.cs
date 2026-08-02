using Nexaflow.Search;
using System.Text.RegularExpressions;
using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

/// <summary>
/// Seeding the index for a regex search. Two properties matter, and the second is the one that was missed:
/// the seed must never exclude something the regex matches, AND it must be a query
/// <see cref="SearchQueryParser"/> actually understands, in the form that searches file CONTENTS.
/// <para>
/// The original version passed every shape test and still returned nothing in the app, because it emitted
/// raw AQS property syntax the parser doesn't speak — and even a valid glob would have taken the parser's
/// filename-only branch, starving a content regex of candidates. Unit-testing both sides while never
/// crossing the seam is exactly how that survived.
/// </para>
/// </summary>
[TestClass]
[CoversNode("page-search-regex")]
public class AqsRegexTranslatorTests
{
    // ── The seam: translator output must survive the real parser ──────────────

    [TestMethod]
    public void SeededQuery_SearchesFileContents_NotJustNames()
    {
        // The reported bug: "?magic" found 34 files, "?/magic/" found none.
        var seed = AqsRegexTranslator.ToIndexQuery("magic");
        Assert.IsNotNull(seed, "a plain literal pattern must be seedable");

        var parsed = SearchQueryParser.Parse(seed);

        Assert.IsFalse(parsed.IsGlob,
            "a glob-shaped seed makes the parser match filenames only, so a content regex sees no candidates");
        StringAssert.Contains(parsed.WhereClause, "System.Search.Contents",
            "the seeded query must reach into file contents");
    }

    [TestMethod]
    public void BarInput_SlashMagicSlash_AsksTheIndexWhatMagicAsks()
    {
        // The whole reported chain from what the user typed: the router strips "?", SearchSyntax reads the
        // delimiters, the seed goes to the parser. "?magic" found 34 files; "?/magic/" found none.
        var request = SearchSyntax.Parse("/magic/");
        Assert.IsTrue(request.IsRegex);
        Assert.AreEqual("magic", request.Text);

        var seed = AqsRegexTranslator.ToIndexQuery(request.Text);
        Assert.AreEqual("magic", seed);

        Assert.AreEqual(SearchQueryParser.Parse("magic").WhereClause,
                        SearchQueryParser.Parse(seed!).WhereClause,
                        "a regex must reach the index with the same question its literal text would");
    }

    [TestMethod]
    public void SeededQuery_MatchesWhatTheSameTextTypedLiterallyWouldFind()
    {
        // "?/magic/" should ask the index the same question "?magic" does, then verify the answers.
        var viaRegex   = SearchQueryParser.Parse(AqsRegexTranslator.ToIndexQuery("magic")!);
        var viaLiteral = SearchQueryParser.Parse("magic");

        Assert.AreEqual(viaLiteral.WhereClause, viaRegex.WhereClause);
    }

    [TestMethod]
    public void SeededQuery_IsNeverPropertySyntaxTheParserCannotRead()
    {
        foreach (var pattern in new[] { "magic", @"report\d+\.pdf", @"file(name|set)\.txt", @"^notes\.md$" })
        {
            var seed = AqsRegexTranslator.ToIndexQuery(pattern);
            if (seed is null) continue;

            Assert.IsFalse(seed.Contains("System."), $"'{pattern}' produced raw AQS: {seed}");
            Assert.IsFalse(seed.Contains(':'), $"'{pattern}' produced property syntax: {seed}");
            Assert.IsFalse(seed.Contains(' '), $"'{pattern}' produced a multi-term seed: {seed}");
        }
    }

    // ── The superset property ─────────────────────────────────────────────────

    private static readonly (string Pattern, string[] Matching)[] Corpus =
    [
        ("magic",                 ["magic.txt", "the magic word", "magical"]),
        (@"report\d+\.pdf",       ["report1.pdf", "report2024.pdf"]),
        (@"file(name|set)\.txt",  ["filename.txt", "fileset.txt"]),
        (@"^notes\.md$",          ["notes.md"]),
        (@"\w+_backup\.zip",      ["nightly_backup.zip"]),
        (@"TODO:\s*fix",          ["TODO: fix the parser", "TODO:fix"]),
    ];

    [TestMethod]
    public void SeedIsAlwaysContainedInEverythingTheRegexMatches()
    {
        foreach (var (pattern, matching) in Corpus)
        {
            var seed = AqsRegexTranslator.ToIndexQuery(pattern);
            if (seed is null) continue;   // unseedable is handled by the caller, not by narrowing

            foreach (var text in matching)
            {
                Assert.IsTrue(Regex.IsMatch(text, pattern), $"test data error: '{text}' vs {pattern}");
                Assert.IsTrue(text.Contains(seed, StringComparison.OrdinalIgnoreCase),
                    $"'{pattern}' seeded '{seed}', which '{text}' does not contain — the index would " +
                    "never return it and the verifier could not get it back");
            }
        }
    }

    [TestMethod]
    public void OptionalText_IsNeverTreatedAsMandatory()
    {
        // "colou?r" matches "color", so "colour" must not be the seed.
        var seed = AqsRegexTranslator.ToIndexQuery("colou?r");

        Assert.IsNotNull(seed);
        Assert.IsTrue("color".Contains(seed, StringComparison.OrdinalIgnoreCase),
            $"seed '{seed}' excludes 'color', which the pattern matches");
    }

    [TestMethod]
    public void AlternationAtTopLevel_HasNoMandatoryLiteral()
    {
        // "cat|dog" matches "dog", so neither branch may be required.
        Assert.IsNull(AqsRegexTranslator.ToIndexQuery("cat|dog"));
    }

    [TestMethod]
    public void StarredText_IsNotMandatory()
    {
        // "ab*c" matches "ac".
        var seed = AqsRegexTranslator.ToIndexQuery("ab*c");
        if (seed is not null)
            Assert.IsTrue("ac".Contains(seed, StringComparison.OrdinalIgnoreCase), $"seed '{seed}' excludes 'ac'");
    }

    [TestMethod]
    public void SingleAlternativeGroup_IsPartOfTheLiteralRun()
    {
        // "ma(ths)" must seed "maths". Treating the group as opaque seeds "ma", which in a documents
        // folder drags back the index's whole result cap and loses real matches to truncation.
        Assert.AreEqual("maths", AqsRegexTranslator.ToIndexQuery("ma(ths)"));
        Assert.AreEqual("maths", AqsRegexTranslator.ToIndexQuery("ma(?:ths)"));

        // The run continues THROUGH the group, so the literal either side joins up too.
        Assert.AreEqual("report2024.pdf", AqsRegexTranslator.ToIndexQuery(@"report(2024)\.pdf"));
    }

    [TestMethod]
    public void OptionalOrAlternatingGroup_IsStillOpaque()
    {
        // "(ths)?" may be absent and "(ths|sci)" may take the other branch — neither is mandatory, so
        // neither may join the run.
        Assert.AreEqual("ma", AqsRegexTranslator.ToIndexQuery("ma(ths)?"));
        Assert.AreEqual("ma", AqsRegexTranslator.ToIndexQuery("ma(ths|sci)"));
    }

    [TestMethod]
    public void GroupWithStructureInside_IsNotFlattened()
    {
        // "(t.s)" contains a wildcard, so its literal content isn't guaranteed either.
        var seed = AqsRegexTranslator.ToIndexQuery("ma(t.s)");

        Assert.AreEqual("ma", seed);
    }

    [TestMethod]
    public void GroupedSeed_StaysASupersetOfTheRegex()
    {
        foreach (var (pattern, text) in new[] { ("ma(ths)", "the maths homework"), (@"report(2024)\.pdf", "report2024.pdf") })
        {
            var seed = AqsRegexTranslator.ToIndexQuery(pattern);
            Assert.IsTrue(Regex.IsMatch(text, pattern), $"test data: '{text}' vs {pattern}");
            Assert.IsTrue(text.Contains(seed!, StringComparison.OrdinalIgnoreCase),
                $"'{pattern}' seeded '{seed}', which '{text}' does not contain");
        }
    }

    [TestMethod]
    public void LongestMandatoryRunWins()
    {
        // "report" narrows far better than ".pdf" does.
        Assert.AreEqual("report", AqsRegexTranslator.ToIndexQuery(@"report\d+\.pdf"));
    }

    [TestMethod]
    public void PatternWithNoLiteral_IsUnseedable()
    {
        // Better to tell the user than to pull back the entire corpus and read every file in it.
        foreach (var pattern in new[] { @"\d{4}", "^.+$", ".*", @"\w+", "a" })
            Assert.IsNull(AqsRegexTranslator.ToIndexQuery(pattern), $"'{pattern}' should not be seedable");
    }

    [TestMethod]
    public void MalformedPattern_DoesNotThrowOrHang()
    {
        foreach (var bad in new[] { "[unclosed", "(unclosed", @"trailing\", "", "***", "a{2" })
            _ = AqsRegexTranslator.ToIndexQuery(bad);
    }
}
