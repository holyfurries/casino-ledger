using System;
using CasinoLedger;

internal static class Program
{
    private static void Main()
    {
        var ledger = new Ledger();
        ledger.settle(new Round("alex", "Alex", CasinoGame.Blackjack, 100, 250));
        ledger.settle(new Round("alex", "Alex", CasinoGame.Slots, 50, 0));
        ledger.settle(new Round("sam", "Sam", CasinoGame.RideTheBus, 200, 200));
        Totals alex = ledger.day("alex");
        require(alex.net == 100, "Net is returned minus wagered across games");
        require(alex.round_count == 2 && alex.win_count == 1 && alex.loss_count == 1, "Wins and losses counted per round");
        require(alex.win_largest == 150 && alex.loss_largest == 50, "Largest win and loss are net amounts");
        require(ledger.day("alex", CasinoGame.Slots).net == -50, "Per-game day totals stay separate");
        Totals sam = ledger.day("sam");
        require(sam.net == 0 && sam.push_count == 1 && sam.win_count == 0, "Returned stake is a push, not a win");
        require(ledger.day("nobody").round_count == 0, "Unknown players have empty totals");
        require(ledger.day_round_count == 3, "Day round count spans all players");

        var points = new SeriesPoint[Ledger.series_point_count_max];
        require(ledger.day_series("alex", points) == 2, "One series point per round");
        require(points[0] == new SeriesPoint(1, 150) && points[1] == new SeriesPoint(2, 100), "Series holds cumulative day net by round index");
        var keys = new string[8];
        require(ledger.day_player_keys(keys) == 2 && keys[0] == "alex" && keys[1] == "sam", "Day players listed in first-seen order");

        string saved = ledger.serialize();
        ledger.start_day();
        require(ledger.day("alex").round_count == 0 && ledger.day_round_count == 0, "New day clears day totals");
        require(ledger.day_series("alex", points) == 0 && ledger.day_player_keys(keys) == 0, "New day clears series");
        require(ledger.lifetime("alex").net == 100, "Lifetime survives the day boundary");

        var restored = new Ledger();
        restored.load(saved);
        require(restored.lifetime("alex") == ledger.lifetime("alex"), "Lifetime totals round-trip through the file format");
        require(restored.lifetime("sam", CasinoGame.RideTheBus).push_count == 1, "Per-game rows round-trip");
        require(restored.day("alex").round_count == 0, "Day totals are not persisted");
        require(restored.serialize() == saved, "Serialization is stable");
        rejects<FormatException>(() => restored.load("garbage"), "Unknown header rejected");
        rejects<FormatException>(() => restored.load("casino-ledger 1\nalex 9 1 0 0 1 1 0 0\n"), "Unknown game rejected");
        rejects<FormatException>(() => restored.load("casino-ledger 1\nalex 0 1 2 0 1 1 0 0\n"), "More wins than rounds rejected");
        rejects<FormatException>(() => restored.load("casino-ledger 1\nalex 0 1 0 0 NaN 1 0 0\n"), "Non-finite amount rejected");
        rejects<FormatException>(() => restored.load(new string('x', 70_000)), "Oversized file rejected");

        rejects<ArgumentOutOfRangeException>(() => ledger.settle(new Round("alex", "Alex", CasinoGame.Slots, 0, 10)), "Zero stake rejected");
        rejects<ArgumentOutOfRangeException>(() => ledger.settle(new Round("alex", "Alex", CasinoGame.Slots, 10, -1)), "Negative return rejected");
        rejects<ArgumentOutOfRangeException>(() => ledger.settle(new Round("alex", "Alex", CasinoGame.Slots, double.NaN, 1)), "NaN stake rejected");
        rejects<ArgumentOutOfRangeException>(() => ledger.settle(new Round("alex", "Alex", (CasinoGame)7, 10, 1)), "Unknown game rejected");
        rejects<ArgumentOutOfRangeException>(() => ledger.settle(new Round("", "Alex", CasinoGame.Slots, 10, 1)), "Empty player key rejected");
        require(ledger.day_round_count == 0, "Rejected rounds leave the ledger untouched");

        string world = CasinoStats.world_key(1234, "Holy Furries");
        require(world.Length == 16 && world == CasinoStats.world_key(1234, "Holy Furries"), "World key is stable for host and clients");
        require(world != CasinoStats.world_key(1235, "Holy Furries") && world != CasinoStats.world_key(1234, "Other"), "Different saves get different ledgers");
        require(CasinoStats.world_key(0, null).Length == 16, "Missing organisation name still yields a key");
        rejects<ArgumentOutOfRangeException>(() => CasinoStats.world_key(1, new string('a', 129)), "Oversized organisation name rejected");

        var busy = new Ledger();
        for (int i = 0; i < 200; i++) busy.settle(new Round("alex", "Alex", CasinoGame.Slots, 10, i % 2 == 0 ? 0 : 25));
        int busy_count = busy.day_series("alex", points);
        require(busy_count > Ledger.series_point_count_max / 2 && busy_count <= Ledger.series_point_count_max, "Series stays bounded under many rounds");
        require(points[busy_count - 1] == new SeriesPoint(200, busy.day("alex").net), "Series always ends at the latest round");
        for (int i = 1; i < busy_count; i++) require(points[i].day_round_index > points[i - 1].day_round_index, "Series round indexes increase");

        var crowded = new Ledger();
        for (int i = 0; i < Ledger.player_count_max; i++) crowded.settle(new Round($"p{i}", "P", CasinoGame.Slots, 10, 0));
        rejects<InvalidOperationException>(() => crowded.settle(new Round("late", "Late", CasinoGame.Slots, 10, 0)), "Active players are never evicted");
        crowded.settle(new Round("p5", "P", CasinoGame.Slots, 10, 0));
        crowded.start_day();
        crowded.settle(new Round("late", "Late", CasinoGame.Slots, 10, 0));
        require(crowded.lifetime("late").round_count == 1, "Idle player slot is reclaimed when full");
        require(crowded.lifetime("p5").round_count == 2, "Eviction prefers players with fewer rounds");
        require(crowded.lifetime("p0").round_count == 0, "Evicted player is forgotten");

        require(Ledger.format_money(4690, true) == "+$4.69K", "Thousands abbreviate with sign");
        require(Ledger.format_money(-762, true) == "-$762", "Small losses stay whole");
        require(Ledger.format_money(447.4, true) == "+$447", "Cents are dropped below one thousand");
        require(Ledger.format_money(0, true) == "+$0", "Break-even is a plus zero");
        require(Ledger.format_money(2800, false) == "$2.8K", "Trailing zeros trimmed");
        require(Ledger.format_money(999.6, false) == "$1K", "Rounding up crosses into the next unit");
        require(Ledger.format_money(999_999, false) == "$1M", "Rounding up crosses into millions");
        require(Ledger.format_money(470_660_000, true) == "+$470.66M", "Millions");
        require(Ledger.format_money(-29_320_000_000, false) == "-$29.32B", "Billions keep the sign when unsigned");
        require(Ledger.format_money(5_000_000_000_000, false) == "$5000B", "Billions is the last unit");

        require(Ledger.safe_name("<size=999>Alex</size>\n‮") == "size999Alexsize", "Names cannot inject rich text");
        require(Ledger.safe_name("  ") == "Player" && Ledger.safe_name(null) == "Player", "Blank names get a placeholder");
        require(Ledger.safe_name(new string('a', 99)).Length == 24, "Names are bounded");

        RoundReport? seen = null;
        int failure_count = 0;
        CasinoStats.subscriber_failed = _ => failure_count++;
        CasinoStats.round_settled += _ => throw new InvalidOperationException("bad subscriber");
        CasinoStats.round_settled += report => seen = report;
        var round = new Round(CasinoStats.player_key("steam-1"), "Riley", CasinoGame.Blackjack, 100, 0);
        CasinoStats.record(round);
        require(seen != null && seen.Value.round == round, "Subscribers receive the settled round");
        require(seen!.Value.lifetime.net == -100 && seen.Value.day.net == -100, "Report carries totals after the round");
        require(failure_count == 1, "A throwing subscriber is isolated from the others");
        require(CasinoStats.lifetime(round.player_key, CasinoGame.Blackjack).loss_count == 1, "Totals are queryable by key");
        require(CasinoStats.player_key("steam-1").Length == 64 && CasinoStats.player_key("steam-1") != CasinoStats.player_key("steam-2"), "Player keys are stable hashes");
        Console.WriteLine("All checks passed.");
    }

    private static void require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException($"FAILED: {description}");
    }

    private static void rejects<T>(Action action, string description) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"FAILED: {description}");
    }
}
