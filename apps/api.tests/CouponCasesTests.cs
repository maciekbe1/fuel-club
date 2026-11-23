using FuelClub.Simulator;

namespace FuelClub.Api.Tests;

public sealed class CouponCasesTests
{
    [Fact]
    public void Every_case_expects_only_offers_its_account_holds()
    {
        Assert.Equal(11, CouponCases.All.Length);
        Assert.Equal(CouponCases.All.Length, CouponCases.All.Select(c => c.Id).Distinct().Count());
        foreach (var c in CouponCases.All)
        {
            Assert.All(
                c.Expected,
                offer => Assert.Contains(offer, CouponCases.Holdings[c.Account])
            );
            if (c.IdOf is { } of)
                Assert.Contains(of.Offer, CouponCases.Holdings[of.Account]);
        }
    }

    [Fact]
    public void Only_the_exact_proposals_count_and_none_expected_means_none_made()
    {
        Assert.True(CouponCases.Matches([], []));
        Assert.False(CouponCases.Matches([], [CouponCases.HotDog]));
        Assert.True(CouponCases.Matches([CouponCases.Rabat], [CouponCases.Rabat]));
        Assert.False(CouponCases.Matches([CouponCases.Rabat], [CouponCases.HotDog]));
        Assert.False(CouponCases.Matches([CouponCases.Rabat], []));
    }

    [Fact]
    public void The_report_puts_the_same_case_from_both_runs_side_by_side()
    {
        CouponCases.ResultFile Run(string id, params string[] proposals) =>
            new(
                id,
                "demo",
                "demo-rules",
                DateTimeOffset.UnixEpoch,
                [
                    new(
                        "1",
                        "nazwany",
                        "Aktywuj kupon CMP rabat",
                        [],
                        proposals,
                        proposals.Length == 0,
                        ""
                    ),
                ]
            );

        var report = CouponCases.Report(Run("cmp-v7", CouponCases.HotDog), Run("cmp-v8"));

        Assert.Contains("| 1 | nazwany |", report);
        Assert.Contains("CMP hot dog ✗ | brak propozycji ✓ |", report);
        Assert.Contains("cmp-v7: 0/1; cmp-v8: 1/1.", report);
    }

    [Fact]
    public void A_case_missing_on_one_side_is_shown_not_dropped_or_fatal()
    {
        static CouponCases.Result Case(string id) => new(id, "r", "m", [], [], true, "");
        var older = new CouponCases.ResultFile(
            "a",
            "demo",
            "x",
            DateTimeOffset.UnixEpoch,
            [Case("1")]
        );
        var newer = new CouponCases.ResultFile(
            "b",
            "demo",
            "x",
            DateTimeOffset.UnixEpoch,
            [Case("1"), Case("2")]
        );

        Assert.Contains(
            "| 2 | r | „m” | brak propozycji | brak w przebiegu |",
            CouponCases.Report(older, newer)
        );
        Assert.Contains("| brak w przebiegu |", CouponCases.Report(newer, older));
    }
}
