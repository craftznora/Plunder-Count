using System.Globalization;
using System.Net;
using System.Text;

namespace PlunderCount;

public static class Pretty
{
    public static string Playlist(string? playlist)
    {
        if (string.IsNullOrEmpty(playlist))
        {
            return "unknown mode";
        }

        var p = playlist.ToLowerInvariant();
        var parts = new List<string>();
        if (p.Contains("habanero") || p.Contains("ranked")) parts.Add("Ranked");
        if (p.Contains("nobuild")) parts.Add("Zero Build");
        if (p.Contains("solo")) parts.Add("Solo");
        else if (p.Contains("duo")) parts.Add("Duos");
        else if (p.Contains("trio")) parts.Add("Trios");
        else if (p.Contains("squad")) parts.Add("Squads");
        return parts.Count > 0 ? string.Join(" · ", parts) : playlist.Replace("Playlist_", "");
    }

    public static string Placement(MatchRecord m)
    {
        if (m.Placement is not int place || place <= 0)
        {
            return "unknown placement";
        }
        if (place == 1)
        {
            return "Victory Royale!";
        }
        return m.TotalPlayers is int total && total > 0 ? $"{L.Ordinal(place)} / {total}" : L.Ordinal(place);
    }

    public static string Clock(float seconds)
    {
        var s = (int)seconds;
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>"at 7:16 · Rare chest" (certain) or "around 5:04 · Rare chest (probable)".</summary>
    public static string Obtained(TreasureFind f)
    {
        if (f.ObtainedAtSeconds is not float t)
        {
            return "time and source unknown";
        }
        var when = f.TimeExact ? $"at {Clock(t)}" : $"around {Clock(t)}";
        return f.SourceCertain ? $"{when} · {f.SourceLabel}" : $"{when} · {f.SourceLabel} ({Likely})";
    }

    public static string Likely => "likely";

    public static string ItemName(string item)
    {
        var name = item;
        foreach (var prefix in new[] { "WID_", "Athena", "AmmoData" })
        {
            name = name.Replace(prefix, "");
        }
        return name.Replace("_", " ").Trim();
    }
}

/// <summary>Replays analyzed so far / to analyze, shown as a loading card in the report.</summary>
public sealed record ScanProgress(int Done, int Total);

public static class ReportWriter
{
    public static readonly DateTime SessionStartUtc = DateTime.UtcNow;

    /// <summary>Set while several replays are being analyzed: the report shows a loading card and refreshes faster.</summary>
    public static volatile ScanProgress? Progress;

    /// <summary>A match belongs to this session if it ended after the tool was started (backlog replays do not count).</summary>
    public static bool IsThisSession(MatchRecord m)
    {
        var start = m.MatchStartUtc ?? m.AnalyzedAtUtc;
        if (start.Kind == DateTimeKind.Unspecified)
        {
            start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        }
        var end = start.ToUniversalTime().AddSeconds(m.DurationSeconds ?? 0);
        return end >= SessionStartUtc.AddMinutes(-2);
    }
    private static CultureInfo Fr => L.Culture;

    public static void WriteAll(Store store, Config config, TrackerData data)
    {
        try
        {
            var view = Profiles.View(data, config);
            WriteAtomic(store.ReportPath, Report(config, view, Profiles.List(data)));
            WriteAtomic(store.OverlayPath, Overlay(OverlayModel.Build(view, null, false)));
            WriteAtomic(store.CsvPath, Csv(data));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cannot write the report: {ex.Message}");
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static DateTime When(MatchRecord m) => (m.MatchStartUtc ?? m.AnalyzedAtUtc).ToLocalTime();

    // Dark glass look: near-black page, translucent panels with hairline borders, one muted sand accent.
    // Figures use Bahnschrift (installed with Windows), text uses Segoe UI.
    private const string Style = """
        :root{--bg:#08090b;--ink:#e6e7ea;--ink2:#f1f2f4;--muted:#7d828c;--title:#f1f2f4;--card:rgba(255,255,255,.035);--cink:#e6e7ea;--cmuted:#868b95;--line:rgba(255,255,255,.07);--track:rgba(255,255,255,.08);--gold:#d6b77c;--star:#b6a2d9;--team:#8fb3c4;--warn:#e0897f;--warnbg:rgba(217,119,111,.08)}
        *{box-sizing:border-box}
        body{margin:0;background:radial-gradient(1200px 600px at 15% -10%,#15171c 0%,transparent 60%),radial-gradient(900px 500px at 100% 0%,#121418 0%,transparent 55%),var(--bg);background-attachment:fixed;color:var(--ink);font:15px/1.5 "Segoe UI Variable","Segoe UI",system-ui,-apple-system,sans-serif;-webkit-font-smoothing:antialiased}
        main{max-width:1120px;margin:0 auto;padding:40px 18px 64px;display:flex;flex-direction:column;gap:28px}
        .pirate{font-family:"Segoe UI Variable Display","Segoe UI",sans-serif;font-weight:600;letter-spacing:-.01em}
        .num{font-family:Bahnschrift,"Segoe UI",sans-serif;font-variant-numeric:tabular-nums}
        header{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:12px;border-bottom:1px solid var(--line);padding-bottom:20px}
        .brand{display:flex;align-items:center;gap:14px}
        h1{margin:0;font-size:30px;line-height:1.1;color:var(--title)}
        .who{font-size:14px;color:var(--muted);margin-top:2px}.who b{color:var(--ink2);font-weight:600}
        .muted{color:var(--muted)}
        h2{margin:0 0 12px;font-size:13px;line-height:1.2;color:var(--muted);font-weight:600;letter-spacing:.14em;text-transform:uppercase}
        .tiles{display:grid;grid-template-columns:repeat(6,minmax(0,1fr));gap:12px}
        .tile{background:var(--card);color:var(--cink);border:1px solid var(--line);border-radius:14px;padding:16px 18px;display:flex;flex-direction:column;gap:2px;backdrop-filter:blur(14px);box-shadow:inset 0 1px 0 rgba(255,255,255,.04)}
        .tile .v{font-size:32px;font-weight:600;line-height:1.1;color:var(--ink2)}
        .tile .l{font-size:12px;color:var(--cmuted);letter-spacing:.04em}
        .tile .s{font-size:12px;color:var(--cmuted);margin-top:4px}
        .tile.hero{grid-column:span 2;background:linear-gradient(160deg,rgba(214,183,124,.10),rgba(255,255,255,.02) 60%);border:1px solid rgba(214,183,124,.28);justify-content:space-between}
        .tile.hero .k{font-size:11px;letter-spacing:.18em;text-transform:uppercase;color:var(--gold)}
        .tile.hero .v{font-size:58px;color:var(--ink2);line-height:1}
        .tile.hero .l{color:var(--cmuted)}
        @media (max-width:820px){.tiles{grid-template-columns:repeat(2,minmax(0,1fr))}.tile.hero{grid-column:span 2}}
        .card{background:var(--card);color:var(--cink);border:1px solid var(--line);border-radius:14px;padding:6px 18px;overflow-x:auto;backdrop-filter:blur(14px);box-shadow:inset 0 1px 0 rgba(255,255,255,.04)}
        .two{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:20px}
        @media (max-width:820px){.two{grid-template-columns:1fr}}
        table{width:100%;border-collapse:collapse;font-variant-numeric:tabular-nums}
        th,td{text-align:left;padding:10px 8px;border-bottom:1px solid var(--line);vertical-align:middle}
        th{white-space:nowrap;font-size:11px;text-transform:uppercase;letter-spacing:.12em;color:var(--cmuted);font-weight:600}
        tr:last-child td{border-bottom:none}
        tbody tr:hover td{background:rgba(255,255,255,.02)}
        td.n{text-align:right;font-weight:600;color:var(--ink2)}
        td.nw{white-space:nowrap}
        .big{font-size:20px}
        .items{color:var(--cmuted);font-size:13px}
        .star{color:var(--star);font-weight:600}
        .gold{color:var(--gold)}
        .sw{display:inline-block;width:8px;height:8px;border-radius:50%;margin-right:10px;vertical-align:1px}
        .meter{display:block;width:140px;height:4px;background:var(--track);border-radius:2px;overflow:hidden}
        .meter i{display:block;height:4px}
        .flag{display:flex;gap:12px;align-items:flex-start;background:rgba(217,119,111,.06);color:#e9c9c4;border:1px solid rgba(217,119,111,.35);border-radius:12px;padding:12px 14px;font-size:14px;margin-top:12px}
        tr.unusual td{background:var(--warnbg)}
        .warn{color:var(--warn)}
        .chart{padding:20px 20px 14px}
        .bars{display:flex;align-items:stretch;gap:4px;height:150px;padding-top:26px}
        .col{flex:1;min-width:5px;display:flex;align-items:flex-end;position:relative;cursor:default}
        .bar{width:100%;background:rgba(241,242,244,.22);border-radius:4px 4px 0 0;transition:background .12s}
        .bar.top{background:var(--gold)}
        .bar.zero{background:var(--line);height:2px!important}
        .col:hover .bar{background:rgba(241,242,244,.45)}
        .col:hover .bar.top{background:#e3c792}
        .tip{position:absolute;left:50%;transform:translateX(-50%);white-space:nowrap;background:#1a1c21;color:var(--ink2);border:1px solid var(--line);
             padding:3px 8px;border-radius:6px;font-size:12px;font-weight:600;opacity:0;pointer-events:none;transition:opacity .12s;z-index:2}
        .col:nth-child(-n+3) .tip{left:0;transform:none}
        .col:nth-last-child(-n+3) .tip{left:auto;right:0;transform:none}
        .col:hover .tip{opacity:1}
        .axis{display:flex;justify-content:space-between;gap:8px;color:var(--cmuted);font-size:12px;margin-top:8px;border-top:1px solid var(--line);padding-top:6px}
        .head{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:12px;margin-bottom:12px}
        .head h2{margin:0}
        .teamtoggle{display:inline-flex;align-items:center;gap:10px;min-height:44px;font-size:14px;color:var(--ink2);cursor:pointer;user-select:none}
        .teamtoggle input{width:18px;height:18px;accent-color:var(--gold);cursor:pointer}
        .team{display:none}
        body.showteam tr.team{display:table-row}
        body.showteam span.team{display:inline}
        tr.team td{opacity:.75}
        .by{white-space:nowrap}
        .by svg{vertical-align:-3px;margin-right:6px}
        footer{font-size:13px;color:var(--muted);border-top:1px solid var(--line);padding-top:16px}
        .loading{display:flex;gap:16px;align-items:center;background:var(--card);color:var(--cink);border:1px solid var(--line);border-radius:14px;padding:16px 18px}
        .loading .muted{color:var(--cmuted)}
        .spin{width:28px;height:28px;flex:none;border:2px solid var(--line);border-top-color:var(--gold);border-radius:50%;animation:spin .8s linear infinite}
        @keyframes spin{to{transform:rotate(360deg)}}
        .loading .grow{flex:1;min-width:0}
        .pbar{height:4px;background:var(--line);border-radius:2px;overflow:hidden;margin:8px 0 6px}
        .pbar i{display:block;height:100%;background:var(--gold)}
        .profiles{margin-top:-16px;font-size:13px}
        """;

    /// <summary>No web font: the page uses the fonts that come with Windows.</summary>
    private const string FontFace = "";

    /// <summary>Plunder Count mark: a geometric "P" on a dark tile, with a sand coin.</summary>
    private const string LogoIcon = """<svg width="44" height="44" viewBox="0 0 64 64" aria-hidden="true"><rect x="2" y="2" width="60" height="60" rx="15" fill="#111318" stroke="#2a2d34" stroke-width="2"/><path d="M23 47V17h11a9.5 9.5 0 0 1 0 19H23" fill="none" stroke="#f1f2f4" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/><circle cx="43" cy="46" r="4.5" fill="#d6b77c"/></svg>""";

    private const string TeamIcon = """<svg width="16" height="16" viewBox="0 0 24 24" aria-hidden="true"><circle cx="9" cy="8" r="3.2" fill="none" stroke="#8fb3c4" stroke-width="2"/><path d="M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6" fill="none" stroke="#8fb3c4" stroke-width="2"/><circle cx="17" cy="9" r="2.6" fill="none" stroke="#8fb3c4" stroke-width="2"/><path d="M16 14c3 0 5 2.2 5 5" fill="none" stroke="#8fb3c4" stroke-width="2"/></svg>""";
    private const string FlagIcon = """<svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true" style="flex:none;margin-top:2px"><path d="M5 21V4M5 4h11l-2 4 2 4H5" fill="none" stroke="#e0897f" stroke-width="2" stroke-linejoin="round"/></svg>""";

    private static readonly Dictionary<string, string> KindColor = new()
    {
        [ChestKinds.Normal] = "#d6b77c",
        [ChestKinds.Rare] = "#7d93ad",
        [ChestKinds.Buried] = "#a08a6a",
        [ChestKinds.Extraction] = "#8fb3c4",
        [ChestKinds.PosterChest] = "#b6a2d9",
        [ChestKinds.Other] = "#6f747d",
    };

    public static string Report(Config config, TrackerData data, IReadOnlyList<Profile> profiles)
    {
        var progress = Progress;
        var matches = data.Matches.OrderByDescending(When).ToList();
        var totalChests = matches.Sum(m => m.ChestsOpened);
        var avg = matches.Count > 0 ? (double)totalChests / matches.Count : 0;
        var best = matches.Count > 0 ? matches.Max(m => m.ChestsOpened) : 0;
        var treasures = matches.Sum(m => m.TreasureFinds.Count);
        var teamTreasures = matches.Sum(m => m.TeamTreasureFinds.Count);
        var session = matches.Where(IsThisSession).Sum(m => m.ChestsOpened);
        var player = matches.Select(m => m.PlayerName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";
        // Own treasures only: the team's are found with chests that are not counted here.
        var every = treasures > 0 ? (int)Math.Round((double)totalChests / treasures) : 0;

        var sb = new StringBuilder();
        var title = "Plunder Count";
        var updated = $"Updated {DateTime.Now.ToString("d MMM yyyy 'at' HH:mm:ss", Fr)} · auto refresh";
        string Tile(string value, string label, string? sub = null) =>
            $"""<div class="tile"><div class="v num">{value}</div><div class="l">{label}</div>{(sub != null ? $"<div class=\"s\">{sub}</div>" : "")}</div>""";

        sb.Append($"""
            <!doctype html><html lang="{L.Code}"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="refresh" content="{(progress != null ? 2 : 15)}">
            <title>{title}</title><style>{FontFace}{Style}</style></head><body><main>
            <header><div class="brand">{LogoIcon}<div><h1 class="pirate">{title}</h1>{(player.Length > 0 ? $"<div class=\"who\"><b>{H(player)}</b></div>" : "")}</div></div>
            <span class="muted" style="font-size:13px">{updated}</span></header>
            {LoadingCard(progress)}{ProfilesLine(profiles, player)}
            <section class="tiles">
              <div class="tile hero"><div class="k">Chests in total</div><div class="v num">{totalChests.ToString("N0", Fr)}</div><div class="l">{$"opened or destroyed by you · {session.ToString("N0", Fr)} since the tool started"}</div></div>
              {Tile(matches.Count.ToString("N0", Fr), "matches analyzed")}
              {Tile(avg.ToString("0.0", Fr), "chests per match")}
              {Tile(best.ToString("N0", Fr), "best match")}
              {Tile(treasures.ToString("N0", Fr), "treasures found",
                  (every > 0 ? $"1 every {every.ToString("N0", Fr)} chests" : null)
                  + (teamTreasures > 0 ? (every > 0 ? "<br>" : "") + $"+{teamTreasures} by the team (estimate)" : ""))}
            </section>
            """);

        // Last 30 days, one bar per day (the best day stands out).
        var today = DateTime.Now.Date;
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29)).ToList();
        var perDay = days.Select(d => matches.Where(m => When(m).Date == d).Sum(m => m.ChestsOpened)).ToList();
        var max = Math.Max(1, perDay.Max());
        sb.Append($"<section><h2 class=\"pirate\">Chests per day</h2><div class=\"card chart\"><div class=\"bars\">");
        for (var i = 0; i < days.Count; i++)
        {
            var v = perDay[i];
            var h = (int)Math.Round(100.0 * v / max);
            var tip = $"{days[i].ToString("ddd d MMM", Fr)}: {L.Count(v, "chest")}";
            var cls = v == 0 ? " zero" : v == max ? " top" : "";
            sb.Append($"<div class=\"col\" aria-label=\"{H(tip)}\"><span class=\"tip\" style=\"bottom:calc({Math.Max(h, 3)}% + 6px)\">{H(tip)}</span><div class=\"bar{cls}\" style=\"height:{h}%\"></div></div>");
        }
        var bestDay = perDay[perDay.IndexOf(perDay.Max())];
        var bestDate = days[perDay.IndexOf(perDay.Max())];
        sb.Append($"</div><div class=\"axis\"><span>{days[0].ToString("d MMM", Fr)}</span>"
            + (bestDay > 0 ? $"<span>best day : {bestDate.ToString("d MMM", Fr)} · {L.Count(bestDay, "chest")}</span>" : "")
            + $"<span>today</span></div></div></section>");

        // By chest type (with the other interactions, not in the total) | where the player's treasures come from.
        var byKind = matches.SelectMany(m => m.Chests).GroupBy(c => ChestKinds.Canonical(c.Kind)).OrderByDescending(g => g.Count()).ToList();
        var kindMax = Math.Max(1, byKind.Count > 0 ? byKind.Max(g => g.Count()) : 1);
        var otherKinds = matches
            .SelectMany(m => m.OtherInteractions.Count > 0
                ? m.OtherInteractions
                : new Dictionary<string, int> { [ChestKinds.AmmoBox] = m.AmmoBoxesOpened, [ChestKinds.OtherContainer] = m.OtherContainersOpened })
            .GroupBy(kv => kv.Key)
            .Select(g => (Kind: g.Key, Count: g.Sum(kv => kv.Value)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ToList();
        sb.Append($"<div class=\"two\"><section><h2 class=\"pirate\">By chest type</h2><div class=\"card\"><table>");
        foreach (var g in byKind)
        {
            var color = KindColor.GetValueOrDefault(g.Key, "#9a8f7a");
            var w = Math.Max(2, (int)Math.Round(100.0 * g.Count() / kindMax));
            sb.Append($"<tr><td><span class=\"sw\" style=\"background:{color}\"></span>{H(L.Name(g.Key))}</td><td style=\"width:150px\"><span class=\"meter\"><i style=\"width:{w}%;background:{color}\"></i></span></td><td class=\"n num big\">{g.Count().ToString("N0", Fr)}</td></tr>");
        }
        var destroyedTotal = matches.Sum(m => m.Chests.Count(c => c.Destroyed));
        var notes = new List<string>();
        if (destroyedTotal > 0)
        {
            notes.Add($"of which {destroyedTotal.ToString("N0", Fr)} destroyed (broken instead of opened)");
        }
        foreach (var (kind, count) in otherKinds)
        {
            sb.Append($"<tr class=\"items\"><td>{H(L.Name(kind))} (not in the total)</td><td></td><td class=\"n num\">{count.ToString("N0", Fr)}</td></tr>");
        }
        sb.Append("</table>");
        if (notes.Count > 0)
        {
            sb.Append($"<div class=\"items\" style=\"padding:10px 0 8px\">{string.Join(" · ", notes)}</div>");
        }
        sb.Append("</div></section>");

        var treasureRows = matches.SelectMany(m => m.TreasureFinds.Select(f => (m, f, Team: false))).ToList();
        var teamRows = matches.SelectMany(m => m.TeamTreasureFinds.Select(f => (m, f, Team: true))).ToList();
        sb.Append($"<section><h2 class=\"pirate\">Where your treasures come from</h2><div class=\"card\"><table>");
        if (treasureRows.Count == 0)
        {
            sb.Append($"<tr><td class=\"items\">No treasure yet.</td></tr>");
        }
        foreach (var g in treasureRows.GroupBy(r => r.f.SourceLabel).OrderByDescending(g => g.Count()))
        {
            var isChest = g.First().f.SourceIsChest;
            sb.Append(isChest
                ? $"<tr><td>{H(g.Key)}</td><td class=\"n num big\">{g.Count()}</td></tr>"
                : $"<tr class=\"unusual\"><td class=\"warn\">{H(g.Key)} <span style=\"font-size:12px\">· low reliability</span></td><td class=\"n num big warn\">{g.Count()}</td></tr>");
        }
        sb.Append("</table></div>");
        var unusual = treasureRows.Where(r => !r.f.SourceIsChest).ToList();
        if (unusual.Count > 0)
        {
            var count = L.Count(unusual.Count, "treasure", "treasures");
            sb.Append($"<div class=\"flag\">{FlagIcon}<span>" + $"<b>{count} obtained other than from a chest.</b> Low reliability: most likely a mistake in the way the replays are analyzed." + "</span></div>");
        }
        sb.Append("</section></div>");

        // Treasures: the player's own, then (hidden unless asked) those found by teammates, shared with the whole team.
        if (treasureRows.Count > 0 || teamRows.Count > 0)
        {
            sb.Append($"<section><div class=\"head\"><h2 class=\"pirate\">Treasures found · <span class=\"num\">{treasures}</span></h2>");
            if (teamRows.Count > 0)
            {
                sb.Append($"<label class=\"teamtoggle\"><input type=\"checkbox\" id=\"showteam\"> {$"Also show the team (+{teamRows.Count}, estimate)"}</label>");
            }
            sb.Append($"</div><div class=\"card\"><table><tr><th>Match</th><th>Treasure</th><th>Obtained at</th><th>Source</th><th>By</th></tr>");
            foreach (var (m, f, team) in treasureRows.Concat(teamRows).OrderByDescending(r => When(r.m)).ThenBy(r => r.f.ObtainedAtSeconds ?? 0))
            {
                var when = f.ObtainedAtSeconds is float t ? (f.TimeExact ? Pretty.Clock(t) : $"around {Pretty.Clock(t)}") : "?";
                string source;
                if (team && string.IsNullOrEmpty(f.Teammate))
                {
                    source = $"<span class=\"items\">chest out of your sight</span>";
                }
                else if (!f.SourceIsChest)
                {
                    source = $"<span class=\"warn\">{H(f.SourceLabel)} · low reliability</span>";
                }
                else
                {
                    source = H(f.SourceLabel) + (f.SourceCertain ? "" : $" <span class=\"items\">({Pretty.Likely})</span>");
                }
                var who = !team ? "you"
                    : TeamIcon + (string.IsNullOrEmpty(f.Teammate) ? "a teammate" : H(f.Teammate));
                var cls = team ? "team" : f.SourceIsChest ? "" : "unusual";
                var stars = f.FromGoldenChest ? "<span class=\"gold\">★★</span>" : "<span class=\"star\">★</span>";
                sb.Append($"<tr{(cls.Length > 0 ? $" class=\"{cls}\"" : "")}><td class=\"nw num items\">{L.DateTimeShort(When(m))}</td><td>{stars} <b>{H(f.DisplayName)}</b></td><td class=\"nw num\">{when}</td><td>{source}</td><td class=\"by\">{who}</td></tr>");
            }
            sb.Append("</table></div></section>");
        }

        // Latest matches.
        sb.Append($"<section><h2 class=\"pirate\">Latest matches</h2><div class=\"card\"><table><tr><th>Date</th><th>Mode</th><th>Placement</th><th style=\"text-align:right\">Chests</th><th>Treasure</th></tr>");
        foreach (var m in matches.Take(100))
        {
            var treasure = string.Join("<br>", m.TreasureFinds.Select(f => $"<span class=\"{(f.FromGoldenChest ? "gold" : "star")}\">{(f.FromGoldenChest ? "★★" : "★")} {H(f.DisplayName)}</span>"))
                + string.Concat(m.TeamTreasureFinds.Select((f, i) => $"<span class=\"team\">{(i == 0 && m.TreasureFinds.Count == 0 ? "" : "<br>")}{TeamIcon} ★ {H(f.DisplayName)}</span>"));
            var win = m.Placement == 1;
            sb.Append($"<tr><td class=\"nw num items\">{L.DateTimeShort(When(m))}</td><td>{H(Pretty.Playlist(m.Playlist))}</td><td class=\"nw num\"{(win ? " style=\"color:#d6b77c;font-weight:700\"" : "")}>{H(Pretty.Placement(m))}</td><td class=\"n num big\">{m.ChestsOpened}</td><td class=\"items\">{treasure}</td></tr>");
        }
        sb.Append("</table></div></section>");

        var script = """<script>(function(){var k='plundercount-showteam',b=document.getElementById('showteam');var on=false;try{on=localStorage.getItem(k)==='1'}catch(e){}function set(v){document.body.classList.toggle('showteam',v);if(b)b.checked=v}set(on);if(b)b.addEventListener('change',function(){set(b.checked);try{localStorage.setItem(k,b.checked?'1':'0')}catch(e){}})})();</script>""";
        sb.Append("<footer>Counts the chests you opened or destroyed yourself, from your replays, analyzed after each match. Ammo boxes and food boxes are not counted. ★★ = golden treasure hunt chest, detected for sure. ★ = treasure received from one of your own chests (exact moment), named when the item can be identified. The team icon marks a treasure found by a teammate (estimate: deduced from the treasures you receive without opening a chest, as the treasure hunt shares them with the whole team); it is not in your totals.</footer>");
        sb.Append(script).Append("</main></body></html>");
        return sb.ToString();
    }

    private static string LoadingCard(ScanProgress? p)
    {
        if (p == null)
        {
            return "";
        }
        var pct = p.Total > 0 ? (int)Math.Round(100.0 * p.Done / p.Total) : 0;
        return $"""<div class="loading"><div class="spin"></div><div class="grow"><b>Analyzing your replays… {p.Done} / {p.Total}</b><div class="pbar"><i style="width:{pct}%"></i></div><span class="muted">This page updates by itself: the numbers below grow as the replays are read.</span></div></div>""";
    }

    private static string ProfilesLine(IReadOnlyList<Profile> profiles, string player)
    {
        var others = profiles.Where(p => p.Name != player).Select(p => H(p.Name)).ToList();
        if (others.Count == 0)
        {
            return "";
        }
        return $"""<div class="profiles muted">Other profiles on this PC: {string.Join(", ", others)} · {"\"Switch profile\" in the counter"}</div>""";
    }

    /// <summary>Rewrites only the OBS overlay page (called on every live update).</summary>

    public static void WriteOverlay(Store store, OverlayView view)
    {
        try
        {
            WriteAtomic(store.OverlayPath, Overlay(view));
        }
        catch
        {
            // the page is a convenience: never stop the tracker for it
        }
    }

    public static string Overlay(OverlayView v)
    {
        var rows = new StringBuilder();
        foreach (var r in v.Rows)
        {
            rows.Append($"<div class=\"row{(r.Treasure ? " t" : "")}\"><span>{H(r.Label)}</span><b>{H(r.Value)}{(r.Detail != null ? $" <small>{H(r.Detail)}</small>" : "")}</b></div>");
        }
        return $$"""
            <!doctype html><html lang="{{L.Code}}"><head><meta charset="utf-8"><meta http-equiv="refresh" content="10">
            <title>Plunder Count overlay</title>
            <style>
            html,body{margin:0;background:transparent;color:#e6e7ea;font:500 16px/1.3 "Segoe UI Variable","Segoe UI",system-ui,sans-serif}
            .box{display:inline-block;min-width:260px;padding:14px 18px 12px;border-radius:12px;background:linear-gradient(180deg,rgba(24,26,31,.88),rgba(11,12,15,.86));border:1px solid rgba(255,255,255,.08);box-shadow:inset 0 1px 0 rgba(255,255,255,.05),0 8px 24px rgba(0,0,0,.35)}
            .head{display:flex;justify-content:space-between;align-items:center;gap:16px;font:600 11px "Segoe UI",sans-serif;letter-spacing:.18em;color:#848891}
            .head span:first-child{display:flex;align-items:center;gap:8px}.head span:first-child:before{content:"";width:3px;height:8px;background:#d6b77c;border-radius:1px}
            .live{color:#8cc5a0}
            .counters{display:flex;align-items:stretch;gap:18px;margin:8px 0 10px}
            .counters>div{display:flex;flex-direction:column}
            .sep{width:1px;background:rgba(255,255,255,.07);margin:8px 0 2px}
            .big{font:600 40px/1.1 Bahnschrift,"Segoe UI",sans-serif;font-variant-numeric:tabular-nums}
            .total{color:#f1f2f4}.cur{color:#8cc5a0}.none{color:#40434a}
            .cap{font-size:10px;font-weight:600;letter-spacing:.14em;text-transform:uppercase;color:#686c75}
            .rows{border-top:1px solid rgba(255,255,255,.07);padding-top:8px;display:grid;grid-template-columns:auto 1fr;gap:4px 12px}
            .row{display:contents}.row span{font-size:13px;font-weight:600;color:#70747d}.row b{font-weight:500;color:#dee0e4}
            .row.t b{color:#b6a2d9}.row small{font-size:11px;font-weight:600;color:#686c75}
            </style></head><body><div class="box">
            <div class="head"><span>PLUNDER COUNT</span>{{(v.Badge != null ? $"<span class=\"live\">{H(v.Badge)}</span>" : "")}}</div>
            <div class="counters">
              <div><span class="big total">{{H(v.Total)}}</span><span class="cap">total chests</span></div>
              <div class="sep"></div>
              <div><span class="big {{(v.Current == null ? "none" : v.CurrentLive ? "cur" : "")}}">{{H(v.Current ?? "—")}}</span><span class="cap">{{H(v.CurrentCaption)}}</span></div>
            </div>
            <div class="rows">{{rows}}</div>
            </div></body></html>
            """;
    }

    public static string Csv(TrackerData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("date;mode;player;placement;players;chests;ammo_boxes;treasures;file");

        foreach (var m in data.Matches.OrderBy(When))
        {
            var t = string.Join(" + ", m.TreasureFinds.Select(f => f.DisplayName));
            sb.AppendLine(string.Join(";",
                When(m).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Pretty.Playlist(m.Playlist), m.PlayerName, m.Placement, m.TotalPlayers,
                m.ChestsOpened, m.AmmoBoxesOpened, t, m.ReplayFile));
        }
        return sb.ToString();
    }
}
