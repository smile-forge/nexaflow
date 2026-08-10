using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Checks the corpus itself, before anything is built against it.
///
/// <para>
/// The captures were constructed from protocol specifications rather than sniffed off a wire, so they are
/// exactly as trustworthy as their own internal consistency — and a grammar validated against a wrong
/// fixture is worse than no validation at all. These tests are the cheapest possible guard on that: the
/// hex must parse, and the field breakdown must account for every byte exactly once.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("test-data integrity guard — validates the corpus, maps to no product node")]
public class ProtocolCorpusIntegrityTests
{
    [TestMethod]
    public void The_corpus_ships_all_ten_protocols()
    {
        var slugs = ProtocolCorpus.All().Select(p => p.Slug).OrderBy(s => s).ToList();

        CollectionAssert.AreEquivalent(
            new[] { "bacnet", "coap", "dhcp", "mdns", "modbus", "mqtt", "ntp", "snmp", "ssdp", "tls" },
            slugs,
            "each protocol defeats the grammar differently; losing one loses that failure mode. "
          + $"Found: {string.Join(", ", slugs)}");
    }

    [TestMethod]
    public void Every_capture_is_valid_hex_and_non_empty()
    {
        List<string> bad = [];

        foreach (var p in ProtocolCorpus.All())
            foreach (var c in p.Captures)
            {
                try
                {
                    if (c.Bytes.Length == 0) bad.Add($"{p.Slug}/{c.Label}: empty");
                }
                catch (FormatException ex)
                {
                    bad.Add($"{p.Slug}/{c.Label}: {ex.Message}");
                }
            }

        Assert.AreEqual(0, bad.Count, string.Join("\n", bad));
    }

    [TestMethod]
    public void Every_field_breakdown_stays_inside_its_packet()
    {
        // A row running past the end means the breakdown and the hex disagree — the fixture is wrong, and
        // any conclusion drawn from it would be too.
        List<string> bad = [];

        foreach (var p in ProtocolCorpus.All())
            foreach (var c in p.Captures)
            {
                int len = c.Bytes.Length;
                foreach (var (off, flen, rest) in c.Fields())
                    if (off < 0 || flen < 0 || off + flen > len)
                        bad.Add($"{p.Slug}/{c.Label}: row '{off}:{flen}:{Truncate(rest)}' "
                              + $"runs to {off + flen} but the packet is {len} bytes");
            }

        Assert.AreEqual(0, bad.Count, string.Join("\n", bad));
    }

    [TestMethod]
    public void Every_byte_of_every_capture_is_accounted_for_exactly_once()
    {
        // The strongest fixture check there is: no gap (a byte nobody explained) and no overlap (two rows
        // claiming the same byte, which is how an off-by-one in a breakdown hides).
        List<string> problems = [];

        foreach (var p in ProtocolCorpus.All())
            foreach (var c in p.Captures)
            {
                int len = c.Bytes.Length;
                var rows = c.Fields();
                if (rows.Count == 0) { problems.Add($"{p.Slug}/{c.Label}: no parsable breakdown rows"); continue; }

                var claims = new int[len];
                foreach (var (off, flen, _) in rows)
                {
                    if (off < 0 || off + flen > len) continue;   // already reported by the bounds test
                    for (int i = off; i < off + flen; i++) claims[i]++;
                }

                var uncovered = Ranges(claims, n => n == 0);
                var overlapped = Ranges(claims, n => n > 1);

                if (uncovered.Count > 0)
                    problems.Add($"{p.Slug}/{c.Label} ({len}B): unexplained bytes at {string.Join(", ", uncovered)}");
                if (overlapped.Count > 0)
                    problems.Add($"{p.Slug}/{c.Label} ({len}B): doubly-claimed bytes at {string.Join(", ", overlapped)}");
            }

        Assert.AreEqual(0, problems.Count,
            "Every byte must be explained by exactly one breakdown row.\n" + string.Join("\n", problems));
    }

    [TestMethod]
    public void Every_protocol_reports_what_it_needs_from_the_state_model()
    {
        // The state model was designed from these; an empty one would mean a protocol's demands went
        // unrepresented in that design.
        var silent = ProtocolCorpus.All()
            .Where(p => string.IsNullOrWhiteSpace(p.Statefulness))
            .Select(p => p.Slug)
            .ToList();

        Assert.AreEqual(0, silent.Count, $"no statefulness analysis for: {string.Join(", ", silent)}");
    }

    [TestMethod]
    public void The_recorded_gaps_are_classified_and_carry_a_concrete_fix()
    {
        // These gaps are the specification of the work. One with no proposed fix is a complaint, not a
        // requirement, and would quietly drop out of the v2 design.
        List<string> bad = [];
        var kinds = new[] { "converter", "fieldKind", "layer", "stateModel", "engine", "expression" };

        foreach (var p in ProtocolCorpus.All())
            foreach (var g in p.Gaps)
            {
                if (!kinds.Contains(g.FixKind)) bad.Add($"{p.Slug}: unknown fixKind '{g.FixKind}' for '{Truncate(g.What)}'");
                if (string.IsNullOrWhiteSpace(g.ProposedFix)) bad.Add($"{p.Slug}: no proposed fix for '{Truncate(g.What)}'");
            }

        Assert.AreEqual(0, bad.Count, string.Join("\n", bad));
    }

    [TestMethod]
    public void The_corpus_records_that_the_first_grammar_failed_all_ten()
    {
        // Deliberately asserted rather than assumed: the v2 spec exists BECAUSE none of these round-tripped.
        // If a future change makes this pass, the corpus was edited and the premise needs re-examining.
        var roundTripped = ProtocolCorpus.All().Where(p => p.V0RoundTrips).Select(p => p.Slug).ToList();

        Assert.AreEqual(0, roundTripped.Count,
            "the v0 grammar round-tripped nothing — that result is why the engine is built against this "
          + $"corpus. Now claims to round-trip: {string.Join(", ", roundTripped)}");
    }

    private static List<string> Ranges(int[] claims, Func<int, bool> match)
    {
        List<string> ranges = [];
        int start = -1;

        for (int i = 0; i <= claims.Length; i++)
        {
            bool hit = i < claims.Length && match(claims[i]);
            if (hit && start < 0) start = i;
            else if (!hit && start >= 0)
            {
                ranges.Add(i - start == 1 ? $"{start}" : $"{start}-{i - 1}");
                start = -1;
            }
        }
        return ranges;
    }

    private static string Truncate(string s) => s.Length <= 60 ? s : s[..60] + "…";
}
