namespace PlunderCount;

/// <summary>Size and opacity choices of the overlay window (percent), offered in the settings.</summary>
public static class OverlayAppearance
{
    public static readonly int[] Sizes = { 70, 80, 90, 100, 115, 130, 150, 175 };

    /// <summary>Below 30 % the numbers can no longer be read.</summary>
    public static readonly int[] Opacities = { 100, 92, 85, 75, 65, 50, 40, 30 };

    public static int ClampSize(int percent) => Math.Clamp(percent, Sizes[0], Sizes[^1]);
    public static int ClampOpacity(int percent) => Math.Clamp(percent, Opacities[^1], 100);

    /// <summary>Next value of the list, back to the first one after the last (or for a value edited by hand).</summary>
    public static int Next(int[] values, int current) => values[(Array.IndexOf(values, current) + 1) % values.Length];
}

/// <summary>Builds what the overlays (window and OBS page) show from the saved matches and the match in progress.</summary>
public static class OverlayModel
{
    /// <summary>Caption of the right counter between two matches (also used to keep the overlay width stable).</summary>
    public static string LastMatchCaption => "last match";

    /// <param name="live">Figures of the match in progress, when the replay could be read (null otherwise).</param>
    /// <param name="inProgress">A match is being recorded (even if its figures are not readable yet).</param>
    public static OverlayView Build(TrackerData d, MatchRecord? live, bool inProgress)
    {
        var liveChests = live?.ChestsOpened ?? 0;
        var liveTreasures = live?.TreasureFinds.Count ?? 0;

        var total = d.Matches.Sum(m => m.ChestsOpened) + liveChests;
        var session = d.Matches.Where(ReportWriter.IsThisSession).ToList();
        var rows = new List<OverlayRow>();

        // Session: chests, then small grey "n matches · avg. x" (finished matches of this session).
        string? sessionDetail = null;
        if (session.Count > 0)
        {
            var avg = (double)session.Sum(m => m.ChestsOpened) / session.Count;
            var avgText = Math.Round(avg, MidpointRounding.AwayFromZero).ToString("0", L.Culture);
            sessionDetail = $"{L.Count(session.Count, "match", "matches")} · avg. {avgText}";
        }
        rows.Add(new OverlayRow("Session", L.Count(session.Sum(m => m.ChestsOpened) + liveChests, "chest"), Detail: sessionDetail));

        var last = d.Matches.OrderByDescending(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).FirstOrDefault();
        if (last != null)
        {
            rows.Add(new OverlayRow("Last", L.Count(last.ChestsOpened, "chest")));
        }

        var treasures = d.Matches.Sum(m => m.TreasureFinds.Count) + liveTreasures;
        rows.Add(new OverlayRow("Treasures", $"★ {L.N(treasures)}", Treasure: true));

        var best = Math.Max(d.Matches.Count > 0 ? d.Matches.Max(m => m.ChestsOpened) : 0, liveChests);
        var lastChests = last?.ChestsOpened ?? 0;
        // Latest matches, oldest first (the match in progress last when its figures are readable).
        var recent = d.Matches.OrderBy(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).Select(m => m.ChestsOpened).ToList();
        if (live != null)
        {
            recent.Add(liveChests);
        }
        recent = recent.TakeLast(16).ToList();

        // Right counter: the match in progress when readable, "…" while it cannot be read yet, else the last match.
        if (live != null)
        {
            return new OverlayView(L.N(total), L.N(live.ChestsOpened), "current match", true, "● LIVE", rows, L.N(treasures), L.N(session.Sum(m => m.ChestsOpened) + liveChests), last != null ? L.N(last.ChestsOpened) : null, best, liveChests, recent, total);
        }
        if (inProgress)
        {
            return new OverlayView(L.N(total), "…", "in progress", true, "● MATCH IN PROGRESS", rows, L.N(treasures), L.N(session.Sum(m => m.ChestsOpened) + liveChests), last != null ? L.N(last.ChestsOpened) : null, best, 0, recent, total);
        }
        return new OverlayView(L.N(total), last != null ? L.N(last.ChestsOpened) : null, LastMatchCaption, false, null, rows, L.N(treasures), L.N(session.Sum(m => m.ChestsOpened) + liveChests), last != null ? L.N(last.ChestsOpened) : null, best, lastChests, recent, total);
    }
}

/// <summary>Round totals worth celebrating: the overlay and the report show the road to the next one.</summary>
public static class Milestones
{
    private static readonly int[] Steps = { 50, 100, 250, 500, 750, 1000, 1500, 2000, 2500, 3000, 4000, 5000 };

    public static int Next(int total)
    {
        foreach (var s in Steps)
        {
            if (total < s) return s;
        }
        return (total / 2500 + 1) * 2500;
    }

    public static int Previous(int total)
    {
        if (total >= 5000) return total / 2500 * 2500;
        var p = 0;
        foreach (var s in Steps)
        {
            if (s <= total) p = s;
        }
        return p;
    }

    /// <summary>Every milestone up to and including <paramref name="next"/>.</summary>
    public static IEnumerable<int> UpTo(int next)
    {
        foreach (var s in Steps)
        {
            if (s <= next) yield return s;
        }
        for (var m = 7500; m <= next; m += 2500)
        {
            yield return m;
        }
    }
}
