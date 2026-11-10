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

    // Premium dark dashboard: near-black page with a faint aurora and grid, glass panels, one sand accent.
    // Figures use Bahnschrift (installed with Windows), text uses Segoe UI Variable.
    private const string Style = """
        :root{--bg:#07080a;--ink:#e8e9ec;--ink2:#f4f5f7;--muted:#80858f;--faint:#4a4e56;--line:rgba(255,255,255,.07);--line2:rgba(255,255,255,.12);--glass:rgba(255,255,255,.028);--glass2:rgba(255,255,255,.05);--gold:#d6b77c;--gold2:#ecd3a0;--green:#8cc5a0;--star:#b6a2d9;--team:#8fb3c4;--warn:#e0897f;--warnbg:rgba(224,137,127,.07)}
        *{box-sizing:border-box}
        html{background:var(--bg)}
        body{margin:0;color:var(--ink);font:14.5px/1.55 "Segoe UI Variable Text","Segoe UI",system-ui,-apple-system,sans-serif;-webkit-font-smoothing:antialiased;min-height:100vh}
        .aurora{position:fixed;inset:0;pointer-events:none;z-index:0;background:radial-gradient(700px 420px at 12% -8%,rgba(214,183,124,.10),transparent 70%),radial-gradient(800px 500px at 92% 4%,rgba(120,140,170,.08),transparent 70%),radial-gradient(900px 600px at 50% 110%,rgba(182,162,217,.05),transparent 70%)}
        .aurora:after{content:"";position:absolute;inset:0;background-image:linear-gradient(rgba(255,255,255,.018) 1px,transparent 1px),linear-gradient(90deg,rgba(255,255,255,.018) 1px,transparent 1px);background-size:48px 48px;mask-image:radial-gradient(ellipse at 50% 0%,#000 30%,transparent 75%)}
        main{position:relative;z-index:1;max-width:1160px;margin:0 auto;padding:28px 22px 72px;display:flex;flex-direction:column;gap:20px}
        .num{font-family:Bahnschrift,"Segoe UI",sans-serif;font-variant-numeric:tabular-nums;letter-spacing:-.01em}
        .muted{color:var(--muted)}
        .glass{background:linear-gradient(180deg,rgba(255,255,255,.045),rgba(255,255,255,.018));border:1px solid var(--line);border-radius:20px;box-shadow:inset 0 1px 0 rgba(255,255,255,.06),0 20px 50px -20px rgba(0,0,0,.6);backdrop-filter:blur(18px) saturate(120%)}
        nav.top{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:4px 2px 8px}
        .brand{display:flex;align-items:center;gap:12px}
        .brand svg{width:36px;height:36px;filter:drop-shadow(0 6px 14px rgba(0,0,0,.5))}
        .brand .name{font:600 18px "Segoe UI Variable Display","Segoe UI",sans-serif;letter-spacing:-.01em;color:var(--ink2)}
        .chip{display:inline-flex;align-items:center;gap:8px;margin-left:6px;padding:4px 12px 4px 4px;border-radius:999px;background:var(--glass2);border:1px solid var(--line);font-size:13px;color:var(--ink)}
        .chip .av{display:grid;place-items:center;width:22px;height:22px;border-radius:50%;font:700 11px "Segoe UI",sans-serif;font-style:normal;color:#1a1408;background:linear-gradient(135deg,var(--gold2),var(--gold))}
        .sync{display:flex;align-items:center;gap:8px;font-size:12.5px;color:var(--muted)}
        .pulse{width:7px;height:7px;border-radius:50%;background:var(--green);box-shadow:0 0 0 0 rgba(140,197,160,.5);animation:pulse 2.4s infinite}
        @keyframes pulse{0%{box-shadow:0 0 0 0 rgba(140,197,160,.45)}70%{box-shadow:0 0 0 8px rgba(140,197,160,0)}100%{box-shadow:0 0 0 0 rgba(140,197,160,0)}}
        .hero{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1.15fr);gap:28px;padding:30px 32px;overflow:hidden;position:relative}
        .hero:before{content:"";position:absolute;inset:-1px;border-radius:20px;pointer-events:none;background:linear-gradient(135deg,rgba(214,183,124,.35),transparent 35%,transparent 70%,rgba(214,183,124,.12));-webkit-mask:linear-gradient(#000 0 0) content-box,linear-gradient(#000 0 0);-webkit-mask-composite:xor;mask-composite:exclude;padding:1px}
        .eyebrow{font-size:11.5px;font-weight:600;letter-spacing:.2em;text-transform:uppercase;color:var(--gold)}
        .huge{font-size:92px;font-weight:600;line-height:1;margin:10px 0 18px;background:linear-gradient(180deg,#fff 20%,#c9ccd2);-webkit-background-clip:text;background-clip:text;color:transparent}
        .hero-meta{display:flex;flex-wrap:wrap;gap:8px}
        .tag{display:inline-flex;align-items:baseline;gap:6px;padding:6px 12px;border-radius:999px;background:var(--glass2);border:1px solid var(--line);font:600 14px Bahnschrift,"Segoe UI",sans-serif;color:var(--ink2)}
        .tag em{font:500 12px "Segoe UI",sans-serif;font-style:normal;color:var(--muted)}
        .tag.up{background:rgba(140,197,160,.09);border-color:rgba(140,197,160,.22);color:var(--green)}
        .hero-chart{display:flex;flex-direction:column;justify-content:flex-end;min-height:170px}
        .spark{width:100%;height:150px;display:block}
        .spark-axis{display:flex;justify-content:space-between;font-size:11.5px;color:var(--faint);margin-top:6px}
        .stats{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:14px}
        .stat{background:linear-gradient(180deg,rgba(255,255,255,.04),rgba(255,255,255,.015));border:1px solid var(--line);border-radius:18px;padding:18px 20px;display:flex;flex-direction:column;gap:2px;box-shadow:inset 0 1px 0 rgba(255,255,255,.05);transition:border-color .2s,transform .2s}
        .stat:hover{border-color:var(--line2);transform:translateY(-2px)}
        .stat .ico{width:34px;height:34px;border-radius:10px;display:grid;place-items:center;color:var(--gold);background:rgba(214,183,124,.08);border:1px solid rgba(214,183,124,.16);margin-bottom:12px}
        .stat .v{font-size:34px;font-weight:600;line-height:1.05;color:var(--ink2)}
        .stat .l{font-size:12.5px;color:var(--muted)}
        .stat .s{font-size:12px;color:var(--faint);margin-top:6px}
        .panel{padding:20px 22px}
        .phead{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:10px;margin-bottom:14px}
        h2{margin:0;font:600 15px "Segoe UI Variable Display","Segoe UI",sans-serif;color:var(--ink2);letter-spacing:0;display:flex;align-items:center;gap:10px}
        h2 .count{font-size:12px;font-weight:600;padding:2px 9px;border-radius:999px;background:rgba(182,162,217,.12);color:var(--star)}
        .bars{display:flex;align-items:stretch;gap:5px;height:170px;padding-top:30px}
        .col{flex:1;min-width:5px;display:flex;align-items:flex-end;position:relative;cursor:default}
        .bar{width:100%;border-radius:6px 6px 2px 2px;background:linear-gradient(180deg,rgba(242,243,245,.32),rgba(242,243,245,.08));transition:background .15s}
        .bar.top{background:linear-gradient(180deg,var(--gold2),rgba(214,183,124,.25));box-shadow:0 0 24px -6px rgba(214,183,124,.55)}
        .bar.zero{background:var(--line);height:3px!important;border-radius:2px}
        .col.today .bar:not(.zero){background:linear-gradient(180deg,var(--green),rgba(140,197,160,.15))}
        .col:hover .bar:not(.zero){background:linear-gradient(180deg,#fff,rgba(242,243,245,.2))}
        .tip{position:absolute;left:50%;transform:translateX(-50%) translateY(4px);white-space:nowrap;background:#16181d;color:var(--ink2);border:1px solid var(--line2);padding:4px 9px;border-radius:8px;font-size:12px;font-weight:600;opacity:0;pointer-events:none;transition:.15s;z-index:3;box-shadow:0 8px 20px rgba(0,0,0,.4)}
        .col:nth-child(-n+3) .tip{left:0;transform:translateY(4px)}
        .col:nth-last-child(-n+3) .tip{left:auto;right:0;transform:translateY(4px)}
        .col:hover .tip{opacity:1;transform:translateX(-50%) translateY(0)}
        .col:nth-child(-n+3):hover .tip,.col:nth-last-child(-n+3):hover .tip{transform:translateY(0)}
        .axis{display:flex;justify-content:space-between;gap:8px;color:var(--faint);font-size:12px;margin-top:10px;padding-top:10px;border-top:1px solid var(--line)}
        .axis .best{color:var(--gold)}
        .two{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:20px;align-items:start}
        tr.more{display:none}
        body.allt tr.more{display:table-row}
        body.allt tr.more.team{display:none}
        body.allt.showteam tr.more.team{display:table-row}
        .showall{display:block;margin:14px auto 0;padding:8px 18px;border-radius:999px;border:1px solid var(--line2);background:var(--glass2);color:var(--ink);font:600 13px "Segoe UI",sans-serif;cursor:pointer;transition:.15s}
        .showall:hover{border-color:rgba(214,183,124,.45);color:var(--ink2)}
        .stack{display:flex;height:10px;border-radius:999px;overflow:hidden;gap:2px;margin:4px 0 16px}
        .stack i{display:block;min-width:3px}
        .kinds{display:flex;flex-direction:column}
        .kind{display:grid;grid-template-columns:auto 1fr auto auto;align-items:center;gap:12px;padding:9px 2px;border-top:1px solid var(--line)}
        .kind:first-child{border-top:none}
        .kind .dot{width:9px;height:9px;border-radius:3px}
        .kind .kn{color:var(--ink)}
        .kind .kn small{color:var(--warn);font-size:11.5px;margin-left:6px}
        .kind .kp{color:var(--faint);font-size:12.5px}
        .kind .kv{font-size:17px;font-weight:600;color:var(--ink2);min-width:44px;text-align:right}
        .kind.src{grid-template-columns:minmax(0,1.2fr) minmax(0,1fr) auto}
        .kind.warnrow .kn{color:var(--warn)}
        .meter{display:block;height:6px;background:var(--line);border-radius:999px;overflow:hidden}
        .meter i{display:block;height:100%;border-radius:999px;background:linear-gradient(90deg,rgba(182,162,217,.5),var(--star))}
        .note{margin-top:14px;padding-top:12px;border-top:1px dashed var(--line2);font-size:12.5px;color:var(--muted)}
        .empty{padding:28px 0;text-align:center;color:var(--muted)}
        .flag{display:flex;gap:12px;align-items:flex-start;background:var(--warnbg);color:#ecc8c2;border:1px solid rgba(224,137,127,.28);border-radius:14px;padding:12px 14px;font-size:13.5px;margin-top:14px}
        .tablewrap{overflow-x:auto;margin:0 -8px}
        table{width:100%;border-collapse:separate;border-spacing:0;font-variant-numeric:tabular-nums}
        th,td{text-align:left;padding:11px 10px;border-bottom:1px solid var(--line);vertical-align:middle}
        th{white-space:nowrap;font-size:11px;text-transform:uppercase;letter-spacing:.14em;color:var(--faint);font-weight:600;border-bottom:1px solid var(--line2)}
        tbody tr{transition:background .12s}
        tbody tr:hover td{background:rgba(255,255,255,.025)}
        tbody tr:last-child td{border-bottom:none}
        .r{text-align:right}
        td.nw,.nw{white-space:nowrap}
        .items{color:var(--muted);font-size:13px}
        .faint{color:var(--faint)}
        .star{color:var(--star);font-weight:600}
        .gold{color:var(--gold);font-weight:600}
        .mode{display:inline-block;padding:3px 10px;border-radius:999px;background:var(--glass2);border:1px solid var(--line);font-size:12.5px;color:var(--ink)}
        .win{display:inline-block;padding:3px 10px;border-radius:999px;font-size:12.5px;font-weight:700;color:#1a1408;background:linear-gradient(135deg,var(--gold2),var(--gold));box-shadow:0 0 18px -4px rgba(214,183,124,.6)}
        .cc{display:inline-flex;align-items:center;gap:12px;justify-content:flex-end}
        .cc b{font-size:17px;font-weight:600;color:var(--ink2);min-width:26px;text-align:right}
        .mini{display:block;width:72px;height:4px;border-radius:999px;background:var(--line);overflow:hidden}
        .mini i{display:block;height:100%;border-radius:999px;background:linear-gradient(90deg,rgba(214,183,124,.4),var(--gold))}
        .you{color:var(--ink);font-weight:600}
        .warn{color:var(--warn)}
        tr.unusual td{background:var(--warnbg)}
        .teamtoggle{display:inline-flex;align-items:center;gap:10px;font-size:13px;color:var(--muted);cursor:pointer;user-select:none}
        .teamtoggle input{position:absolute;opacity:0;pointer-events:none}
        .teamtoggle .sw{position:relative;width:34px;height:20px;border-radius:999px;background:var(--line2);transition:.2s}
        .teamtoggle .sw:after{content:"";position:absolute;top:3px;left:3px;width:14px;height:14px;border-radius:50%;background:#cfd2d8;transition:.2s}
        .teamtoggle input:checked+.sw{background:rgba(214,183,124,.55)}
        .teamtoggle input:checked+.sw:after{left:17px;background:#fff}
        .team{display:none}
        body.showteam tr.team{display:table-row}
        body.showteam span.team{display:inline}
        tr.team td{opacity:.75}
        .by{white-space:nowrap}
        .by svg{vertical-align:-3px;margin-right:6px}
        footer{display:flex;flex-direction:column;gap:6px;font-size:12.5px;color:var(--muted);padding:18px 4px 0;border-top:1px solid var(--line)}
        footer .fine{color:var(--faint)}
        .loading{display:flex;gap:16px;align-items:center;background:var(--glass);border:1px solid rgba(214,183,124,.25);border-radius:18px;padding:16px 18px}
        .spin{width:26px;height:26px;flex:none;border:2px solid var(--line2);border-top-color:var(--gold);border-radius:50%;animation:spin .8s linear infinite}
        @keyframes spin{to{transform:rotate(360deg)}}
        .loading .grow{flex:1;min-width:0}
        .pbar{height:4px;background:var(--line);border-radius:999px;overflow:hidden;margin:8px 0 6px}
        .pbar i{display:block;height:100%;background:linear-gradient(90deg,var(--gold),var(--gold2))}
        .profiles{margin-top:-8px;font-size:13px}
        @media (max-width:900px){.hero{grid-template-columns:1fr}.stats{grid-template-columns:repeat(2,minmax(0,1fr))}.two{grid-template-columns:1fr}.huge{font-size:72px}}
        @media (prefers-reduced-motion:reduce){*{animation:none!important;transition:none!important}}
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
        var sessionMatches = matches.Where(IsThisSession).ToList();
        var session = sessionMatches.Sum(m => m.ChestsOpened);
        var last = matches.FirstOrDefault();
        var wins = matches.Count(m => m.Placement == 1);
        var player = matches.Select(m => m.PlayerName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";
        // Own treasures only: the team's are found with chests that are not counted here.
        var every = treasures > 0 ? (int)Math.Round((double)totalChests / treasures) : 0;

        var sb = new StringBuilder();
        var title = "Plunder Count";
        var updated = DateTime.Now.ToString("HH:mm", Fr);
        string N(int n) => n.ToString("N0", Fr);

        string Stat(string icon, string value, string label, string? sub = null) =>
            $"""<div class="stat"><div class="ico">{icon}</div><div class="v num">{value}</div><div class="l">{label}</div>{(sub != null ? $"<div class=\"s\">{sub}</div>" : "")}</div>""";

        // Last 30 days, one value per day: hero sparkline + activity bars.
        var today = DateTime.Now.Date;
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29)).ToList();
        var perDay = days.Select(d => matches.Where(m => When(m).Date == d).Sum(m => m.ChestsOpened)).ToList();
        var max = Math.Max(1, perDay.Max());
        var last7 = perDay.Skip(23).Sum();
        var prev7 = perDay.Skip(16).Take(7).Sum();

        sb.Append($"""
            <!doctype html><html lang="{L.Code}"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="refresh" content="{(progress != null ? 2 : 15)}">
            <title>{title}</title><style>{FontFace}{Style}</style></head><body><div class="aurora"></div><main>
            <nav class="top">
              <div class="brand">{LogoIcon}<span class="name">{title}</span>{(player.Length > 0 ? $"<span class=\"chip\"><i class=\"av\">{H(player[..1].ToUpperInvariant())}</i>{H(player)}</span>" : "")}</div>
              <div class="sync"><i class="pulse"></i>Live · updated {updated}</div>
            </nav>
            {LoadingCard(progress)}{ProfilesLine(profiles, player)}
            <section class="hero glass">
              <div class="hero-main">
                <div class="eyebrow">Total chests</div>
                <div class="huge num">{N(totalChests)}</div>
                <div class="hero-meta">
                  <span class="tag{(session > 0 ? " up" : "")}">+{N(session)} <em>this session</em></span>
                  {(last != null ? $"<span class=\"tag\">{N(last.ChestsOpened)} <em>last match</em></span>" : "")}
                  <span class="tag">{N(last7)} <em>last 7 days{(prev7 > 0 ? $" · {(last7 >= prev7 ? "▲" : "▼")} {Math.Abs(last7 - prev7)}" : "")}</em></span>
                </div>
              </div>
              <div class="hero-chart">{Sparkline(perDay)}<div class="spark-axis"><span>{days[0].ToString("d MMM", Fr)}</span><span>today</span></div></div>
            </section>
            <section class="stats">
              {Stat(IconMatches, N(matches.Count), "Matches", wins > 0 ? $"{L.Count(wins, "Victory Royale", "Victory Royales")}" : null)}
              {Stat(IconAvg, avg.ToString("0.0", Fr), "Chests per match")}
              {Stat(IconBest, N(best), "Best match")}
              {Stat(IconStar, N(treasures), "Treasures",
                  (every > 0 ? $"1 every {N(every)} chests" : null)
                  + (teamTreasures > 0 ? (every > 0 ? " · " : "") + $"+{teamTreasures} team" : ""))}
            </section>
            """);

        // Activity: one bar per day, best day highlighted.
        sb.Append($"<section class=\"glass panel\"><div class=\"phead\"><h2>Activity</h2><span class=\"muted\">last 30 days</span></div><div class=\"bars\">");
        for (var i = 0; i < days.Count; i++)
        {
            var v = perDay[i];
            var h = (int)Math.Round(100.0 * v / max);
            var tip = $"{days[i].ToString("ddd d MMM", Fr)} · {L.Count(v, "chest")}";
            var cls = v == 0 ? " zero" : v == max ? " top" : "";
            sb.Append($"<div class=\"col{(days[i] == today ? " today" : "")}\" aria-label=\"{H(tip)}\"><span class=\"tip\" style=\"bottom:calc({Math.Max(h, 3)}% + 8px)\">{H(tip)}</span><div class=\"bar{cls}\" style=\"height:{h}%\"></div></div>");
        }
        var bestDay = perDay.Max();
        var bestDate = days[perDay.IndexOf(bestDay)];
        sb.Append($"</div><div class=\"axis\"><span>{days[0].ToString("d MMM", Fr)}</span>"
            + (bestDay > 0 ? $"<span class=\"best\">Best day · {bestDate.ToString("d MMM", Fr)} · {L.Count(bestDay, "chest")}</span>" : "")
            + $"<span>Today</span></div></section>");

        // Chest types | where the player's treasures come from.
        var byKind = matches.SelectMany(m => m.Chests).GroupBy(c => ChestKinds.Canonical(c.Kind)).OrderByDescending(g => g.Count()).ToList();
        var kindTotal = Math.Max(1, byKind.Sum(g => g.Count()));
        var otherKinds = matches
            .SelectMany(m => m.OtherInteractions.Count > 0
                ? m.OtherInteractions
                : new Dictionary<string, int> { [ChestKinds.AmmoBox] = m.AmmoBoxesOpened, [ChestKinds.OtherContainer] = m.OtherContainersOpened })
            .GroupBy(kv => kv.Key)
            .Select(g => (Kind: g.Key, Count: g.Sum(kv => kv.Value)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ToList();
        sb.Append("<div class=\"two\"><section class=\"glass panel\"><div class=\"phead\"><h2>Chest types</h2></div>");
        // Stacked bar of the whole mix, then one row per kind.
        sb.Append("<div class=\"stack\">");
        foreach (var g in byKind)
        {
            var color = KindColor.GetValueOrDefault(g.Key, "#6f747d");
            sb.Append($"<i style=\"flex:{g.Count()};background:{color}\" title=\"{H(L.Name(g.Key))}\"></i>");
        }
        sb.Append("</div><div class=\"kinds\">");
        foreach (var g in byKind)
        {
            var color = KindColor.GetValueOrDefault(g.Key, "#6f747d");
            var pct = 100.0 * g.Count() / kindTotal;
            sb.Append($"<div class=\"kind\"><span class=\"dot\" style=\"background:{color}\"></span><span class=\"kn\">{H(L.Name(g.Key))}</span><span class=\"kp\">{pct.ToString(pct < 10 ? "0.0" : "0", Fr)}%</span><span class=\"kv num\">{N(g.Count())}</span></div>");
        }
        sb.Append("</div>");
        var destroyedTotal = matches.Sum(m => m.Chests.Count(c => c.Destroyed));
        var extras = new List<string>();
        if (destroyedTotal > 0)
        {
            extras.Add($"{N(destroyedTotal)} destroyed instead of opened");
        }
        foreach (var (kind, count) in otherKinds)
        {
            extras.Add($"{H(L.Name(kind))} {N(count)}");
        }
        if (extras.Count > 0)
        {
            sb.Append($"<div class=\"note\">Not in the total · {string.Join(" · ", extras)}</div>");
        }
        sb.Append("</section>");

        var treasureRows = matches.SelectMany(m => m.TreasureFinds.Select(f => (m, f, Team: false))).ToList();
        var teamRows = matches.SelectMany(m => m.TeamTreasureFinds.Select(f => (m, f, Team: true))).ToList();
        sb.Append("<section class=\"glass panel\"><div class=\"phead\"><h2>Treasure sources</h2></div>");
        if (treasureRows.Count == 0)
        {
            sb.Append("<div class=\"empty\">No treasure yet. Keep looting.</div>");
        }
        else
        {
            var srcMax = treasureRows.GroupBy(r => r.f.SourceLabel).Max(g => g.Count());
            sb.Append("<div class=\"kinds\">");
            foreach (var g in treasureRows.GroupBy(r => r.f.SourceLabel).OrderByDescending(g => g.Count()))
            {
                var isChest = g.First().f.SourceIsChest;
                var w = Math.Max(4, (int)Math.Round(100.0 * g.Count() / srcMax));
                sb.Append($"<div class=\"kind src{(isChest ? "" : " warnrow")}\"><span class=\"kn\">{H(g.Key)}{(isChest ? "" : " <small>low reliability</small>")}</span><span class=\"meter\"><i style=\"width:{w}%\"></i></span><span class=\"kv num\">{g.Count()}</span></div>");
            }
            sb.Append("</div>");
        }
        var unusual = treasureRows.Where(r => !r.f.SourceIsChest).ToList();
        if (unusual.Count > 0)
        {
            var count = L.Count(unusual.Count, "treasure", "treasures");
            sb.Append($"<div class=\"flag\">{FlagIcon}<span><b>{count} obtained other than from a chest.</b> Low reliability: most likely a mistake in the way the replays are analyzed.</span></div>");
        }
        sb.Append("</section></div>");

        // Treasures: the player's own, then (hidden unless asked) those found by teammates, shared with the whole team.
        if (treasureRows.Count > 0 || teamRows.Count > 0)
        {
            sb.Append($"<section class=\"glass panel\"><div class=\"phead\"><h2>Treasures <span class=\"count num\">{treasures}</span></h2>");
            if (teamRows.Count > 0)
            {
                sb.Append($"<label class=\"teamtoggle\"><input type=\"checkbox\" id=\"showteam\"><span class=\"sw\"></span>Show team finds (+{teamRows.Count}, estimate)</label>");
            }
            sb.Append("</div><div class=\"tablewrap\"><table><thead><tr><th>Match</th><th>Treasure</th><th>Obtained</th><th>Source</th><th>By</th></tr></thead><tbody>");
            var rowIndex = 0;
            foreach (var (m, f, team) in treasureRows.Concat(teamRows).OrderByDescending(r => When(r.m)).ThenBy(r => r.f.ObtainedAtSeconds ?? 0))
            {
                var more = !team && rowIndex++ >= 10; // the 10 latest, the rest behind "Show all"
                var when = f.ObtainedAtSeconds is float t ? (f.TimeExact ? Pretty.Clock(t) : $"~{Pretty.Clock(t)}") : "?";
                string source;
                if (team && string.IsNullOrEmpty(f.Teammate))
                {
                    source = "<span class=\"items\">out of your sight</span>";
                }
                else if (!f.SourceIsChest)
                {
                    source = $"<span class=\"warn\">{H(f.SourceLabel)} · low reliability</span>";
                }
                else
                {
                    source = H(f.SourceLabel) + (f.SourceCertain ? "" : $" <span class=\"items\">({Pretty.Likely})</span>");
                }
                var who = !team ? "<span class=\"you\">You</span>"
                    : TeamIcon + (string.IsNullOrEmpty(f.Teammate) ? "a teammate" : H(f.Teammate));
                var cls = (team ? "team" : f.SourceIsChest ? "" : "unusual") + (more ? " more" : "");
                var stars = f.FromGoldenChest ? "<span class=\"gold\">★★</span>" : "<span class=\"star\">★</span>";
                sb.Append($"<tr{(cls.Trim().Length > 0 ? $" class=\"{cls.Trim()}\"" : "")}><td class=\"nw items\">{L.DateTimeShort(When(m))}</td><td>{stars} <b>{H(f.DisplayName)}</b></td><td class=\"nw num\">{when}</td><td>{source}</td><td class=\"by\">{who}</td></tr>");
            }
            sb.Append("</tbody></table></div>");
            if (treasureRows.Count > 10)
            {
                sb.Append($"<button class=\"showall\" id=\"showall\" type=\"button\">Show all {treasureRows.Count} treasures</button>");
            }
            sb.Append("</section>");
        }

        // Latest matches.
        sb.Append("<section class=\"glass panel\"><div class=\"phead\"><h2>Latest matches</h2><span class=\"muted\">" + L.Count(Math.Min(100, matches.Count), "match", "matches") + "</span></div>");
        sb.Append("<div class=\"tablewrap\"><table><thead><tr><th>Date</th><th>Mode</th><th>Placement</th><th class=\"r\">Chests</th><th>Treasure</th></tr></thead><tbody>");
        var bestMatch = Math.Max(1, best);
        foreach (var m in matches.Take(100))
        {
            var treasure = string.Join("<br>", m.TreasureFinds.Select(f => $"<span class=\"{(f.FromGoldenChest ? "gold" : "star")}\">{(f.FromGoldenChest ? "★★" : "★")} {H(f.DisplayName)}</span>"))
                + string.Concat(m.TeamTreasureFinds.Select((f, i) => $"<span class=\"team\">{(i == 0 && m.TreasureFinds.Count == 0 ? "" : "<br>")}{TeamIcon} ★ {H(f.DisplayName)}</span>"));
            var win = m.Placement == 1;
            var place = win ? "<span class=\"win\">#1 Victory</span>" : $"<span class=\"num\">{H(Pretty.Placement(m))}</span>";
            var w = (int)Math.Round(100.0 * m.ChestsOpened / bestMatch);
            sb.Append($"<tr><td class=\"nw items\">{L.DateTimeShort(When(m))}</td><td><span class=\"mode\">{H(Pretty.Playlist(m.Playlist))}</span></td><td class=\"nw\">{place}</td><td class=\"r\"><span class=\"cc\"><span class=\"mini\"><i style=\"width:{w}%\"></i></span><b class=\"num\">{m.ChestsOpened}</b></span></td><td class=\"items\">{(treasure.Length > 0 ? treasure : "<span class=\"faint\">·</span>")}</td></tr>");
        }
        sb.Append("</tbody></table></div></section>");

        var script = """<script>(function(){var k='plundercount-showteam',b=document.getElementById('showteam');var on=false;try{on=localStorage.getItem(k)==='1'}catch(e){}function set(v){document.body.classList.toggle('showteam',v);if(b)b.checked=v}set(on);if(b)b.addEventListener('change',function(){set(b.checked);try{localStorage.setItem(k,b.checked?'1':'0')}catch(e){}})})();</script>""";
        sb.Append("<footer><div>Counts the chests you opened or destroyed yourself, read from your replays after each match. Ammo and food boxes are not counted. ★★ golden treasure hunt chest (certain) · ★ treasure from one of your own chests.</div><div class=\"fine\">Plunder Count · not affiliated with Epic Games</div></footer>");
        sb.Append(script).Append("""<script>(function(){var b=document.getElementById('showall');if(b)b.addEventListener('click',function(){document.body.classList.add('allt');b.remove()})})();</script>""").Append("</main></body></html>");
        return sb.ToString();
    }

    /// <summary>Smooth area chart of the last 30 days (inline SVG, no script).</summary>
    private static string Sparkline(IReadOnlyList<int> values)
    {
        const double W = 520, Hh = 150, Pad = 8;
        var max = Math.Max(1, values.Max());
        var pts = values.Select((v, i) => (X: Pad + i * (W - 2 * Pad) / Math.Max(1, values.Count - 1), Y: Hh - Pad - (Hh - 2 * Pad - 10) * v / max)).ToList();
        string F(double d) => d.ToString("0.#", CultureInfo.InvariantCulture);
        // Catmull-Rom to Bezier: a smooth line through every day.
        var path = new StringBuilder($"M{F(pts[0].X)},{F(pts[0].Y)}");
        for (var i = 0; i < pts.Count - 1; i++)
        {
            var p0 = pts[Math.Max(0, i - 1)];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = pts[Math.Min(pts.Count - 1, i + 2)];
            var c1 = (X: p1.X + (p2.X - p0.X) / 6, Y: p1.Y + (p2.Y - p0.Y) / 6);
            var c2 = (X: p2.X - (p3.X - p1.X) / 6, Y: p2.Y - (p3.Y - p1.Y) / 6);
            path.Append($" C{F(c1.X)},{F(Math.Min(Hh - Pad, c1.Y))} {F(c2.X)},{F(Math.Min(Hh - Pad, c2.Y))} {F(p2.X)},{F(p2.Y)}");
        }
        var line = path.ToString();
        var area = line + $" L{F(pts[^1].X)},{F(Hh)} L{F(pts[0].X)},{F(Hh)} Z";
        var lastPt = pts[^1];
        return $"""<svg class="spark" viewBox="0 0 {F(W)} {F(Hh)}" preserveAspectRatio="none" aria-hidden="true"><defs><linearGradient id="sg" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#d6b77c" stop-opacity=".32"/><stop offset="1" stop-color="#d6b77c" stop-opacity="0"/></linearGradient><linearGradient id="sl" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#d6b77c" stop-opacity=".35"/><stop offset="1" stop-color="#e9cf9c"/></linearGradient></defs><path d="{area}" fill="url(#sg)"/><path d="{line}" fill="none" stroke="url(#sl)" stroke-width="2.4" stroke-linecap="round" vector-effect="non-scaling-stroke"/><circle cx="{F(lastPt.X)}" cy="{F(lastPt.Y)}" r="4" fill="#f2e2bf" stroke="#0b0c0f" stroke-width="2" vector-effect="non-scaling-stroke"/></svg>""";
    }

    private const string IconMatches = """<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/></svg>""";
    private const string IconAvg = """<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 17l6-6 4 4 8-8"/><path d="M15 7h6v6"/></svg>""";
    private const string IconBest = """<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0V4z"/><path d="M17 5h3v2a3 3 0 0 1-3 3M7 5H4v2a3 3 0 0 0 3 3"/></svg>""";
    private const string IconStar = """<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z"/></svg>""";

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
