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

    // Design v2 (2b): near-black editorial dashboard, Sora light figures, JetBrains Mono labels, Geist text, one gold accent.
    private const string Style = """
        :root{--bg:#050506;--panel:#09090a;--ink:#f2efe8;--ink2:#d9d6cf;--muted:#8f8d88;--dim:#6f6d69;--faint:#5a5955;--line:rgba(255,255,255,.07);--line2:rgba(255,255,255,.1);--gold:#e6c88f;--green:#8fd6a8;--violet:#cfc2f5;--warn:#e0897f}
        *{box-sizing:border-box}
        html{background:var(--bg)}
        body{margin:0;color:var(--ink);font:400 14px/1.5 "Geist","Segoe UI",system-ui,sans-serif;-webkit-font-smoothing:antialiased;padding:40px 24px}
        main{max-width:1280px;margin:0 auto;padding:44px 56px 64px;border-radius:16px;border:1px solid rgba(255,255,255,.06);background:radial-gradient(70% 40% at 10% 0%,rgba(230,200,143,.07),transparent 70%),var(--panel)}
        .mono{font-family:"JetBrains Mono",Consolas,monospace}
        .dim{font-size:11px;letter-spacing:.1em;color:var(--dim)}
        .r{text-align:right}
        .dia{display:inline-block;width:7px;height:7px;transform:rotate(45deg);border:1px solid var(--gold);flex:none}
        .dia.glow{width:10px;height:10px;box-shadow:0 0 12px rgba(230,200,143,.4)}
        header.top{display:flex;justify-content:space-between;align-items:center;gap:16px}
        .brand{display:flex;align-items:center;gap:14px}
        .brand .name{font:400 20px "Sora","Segoe UI",sans-serif;letter-spacing:-.01em}
        .brand .who{font:400 11px "JetBrains Mono",monospace;letter-spacing:.14em;color:var(--dim)}
        .sync{display:flex;align-items:center;gap:10px;font:400 11px "JetBrains Mono",monospace;letter-spacing:.06em;color:var(--dim)}
        .gdot{width:6px;height:6px;border-radius:50%;background:var(--green);box-shadow:0 0 10px var(--green);animation:breathe 2.6s ease-in-out infinite}
        @keyframes breathe{50%{opacity:.45}}
        .hero{display:grid;grid-template-columns:1.25fr 1fr;gap:64px;margin-top:56px;padding-bottom:40px;border-bottom:1px solid var(--line)}
        .eyebrow{font:400 11px "JetBrains Mono",monospace;letter-spacing:.24em;color:var(--gold)}
        .huge{margin-top:6px;font:200 200px/.85 "Sora","Segoe UI Light",sans-serif;letter-spacing:-.05em;font-variant-numeric:lining-nums tabular-nums;background:linear-gradient(180deg,#f7f2e6 30%,#b79a63);-webkit-background-clip:text;background-clip:text;color:transparent;white-space:nowrap}
        .sub{margin-top:14px;font:300 15px "Sora",sans-serif;color:var(--muted)}
        .krows{display:flex;flex-direction:column;justify-content:flex-end}
        .krow{display:flex;justify-content:space-between;align-items:baseline;padding:14px 0;border-top:1px solid var(--line)}
        .krow .k{font:400 11px "JetBrains Mono",monospace;letter-spacing:.18em;color:var(--muted)}
        .krow .kx{color:var(--faint)}
        .krow .kv{font:200 34px/1 "Sora",sans-serif;letter-spacing:-.03em}
        .krow .kv.violet{color:var(--violet)}
        .block{margin-top:40px}
        .bhead{display:flex;justify-content:space-between;align-items:baseline;gap:12px}
        h2{margin:0;font:300 22px "Sora","Segoe UI",sans-serif;letter-spacing:-.01em}
        .chart{position:relative;margin-top:24px;height:200px;display:flex;align-items:flex-end;background:repeating-linear-gradient(0deg,rgba(255,255,255,.045) 0 1px,transparent 1px 50px)}
        .col{flex:1;height:100%;display:flex;flex-direction:column;justify-content:flex-end;align-items:center;cursor:default}
        .col i{display:block;width:10px;border-radius:10px;background:linear-gradient(180deg,rgba(242,239,232,.42),rgba(242,239,232,.12));transition:background .15s}
        .col i.zero{background:rgba(255,255,255,.08)}
        .col i.best{background:linear-gradient(180deg,#f5dfae,#a88a52);box-shadow:0 0 24px rgba(230,200,143,.45)}
        .col .lab{font:400 10px "JetBrains Mono",monospace;color:var(--ink);margin-bottom:6px;min-height:15px;visibility:hidden}
        .col.isbest .lab{visibility:visible;color:var(--gold)}
        .chart.hovering .col.isbest .lab{visibility:hidden}
        .chart .col.hov .lab{visibility:visible}
        .col.hov i:not(.zero){background:var(--ink)}
        .axis{display:flex;margin-top:12px;border-top:1px solid var(--line2);padding-top:10px}
        .axis div{flex:1;display:flex;flex-direction:column;align-items:center;gap:6px}
        .axis .t{width:1px;height:3px;background:rgba(255,255,255,.25)}
        .axis .t.major{height:6px}
        .axis .al{font:400 9.5px "JetBrains Mono",monospace;letter-spacing:.06em;color:var(--dim);white-space:nowrap;min-height:13px}
        .split{display:grid;grid-template-columns:1.5fr 1fr;gap:56px;margin-top:56px;align-items:start}
        .split.even{grid-template-columns:1fr 1fr;margin-top:64px}
        .stack{display:flex;gap:3px;height:10px;margin-top:20px}
        .stack span{border-radius:2px;min-width:2px}
        .types{margin-top:18px}
        .trow{display:grid;grid-template-columns:1fr 60px 60px;align-items:baseline;gap:16px;padding:11px 0;border-top:1px solid rgba(255,255,255,.06)}
        .trow .tn{display:flex;align-items:center;gap:12px;font-size:14px;color:var(--ink2)}
        .trow .tp{text-align:right;font:400 11px "JetBrains Mono",monospace;color:var(--dim)}
        .trow .tv{text-align:right;font:300 18px/1 "Sora",sans-serif}
        .trow.extra{border-top:1px dashed var(--line2);font:400 11px "JetBrains Mono",monospace;letter-spacing:.06em;color:#5f5e5a;padding-top:12px}
        .trow.extra+.trow.extra{border-top:none;padding-top:4px}
        .trow.extra span:last-child{text-align:right}
        .tcard{margin-top:20px;padding:28px;border-radius:14px;border:1px solid rgba(196,181,242,.2);background:radial-gradient(120% 120% at 100% 0%,rgba(196,181,242,.1),transparent 60%),#0d0d10}
        .tcard+.tcard{margin-top:12px;padding:20px 28px}
        .tc-top{display:flex;justify-content:space-between;align-items:baseline;font-size:14px;color:var(--ink2)}
        .tc-n{font:200 56px/.9 "Sora",sans-serif;letter-spacing:-.04em;color:var(--violet)}
        .tcard+.tcard .tc-n{font-size:36px}
        .tc-bar{height:2px;margin-top:18px;border-radius:2px;background:linear-gradient(90deg,var(--violet),rgba(196,181,242,.2))}
        .tc-cap{margin-top:12px;font:400 10.5px "JetBrains Mono",monospace;letter-spacing:.12em;color:#7d7a86}
        .tc-empty{color:var(--dim);font-size:14px}
        .tcard.warn{border-color:rgba(224,137,127,.3);background:#0f0c0c}
        .tcard.warn .tc-n{color:var(--warn)}
        .tcard.warn .tc-bar{background:linear-gradient(90deg,var(--warn),rgba(224,137,127,.15))}
        .flag{display:flex;gap:12px;align-items:flex-start;margin-top:12px;padding:12px 14px;border-radius:12px;border:1px solid rgba(224,137,127,.28);background:rgba(224,137,127,.06);color:#ecc8c2;font-size:13px}
        .grid{display:grid;gap:12px;align-items:center}
        .tg{grid-template-columns:96px 1fr 52px 64px 44px}
        .mg{grid-template-columns:96px 1fr 58px 44px 118px}
        .ghead{margin-top:20px;padding-bottom:10px;font-size:9.5px;letter-spacing:.16em;color:var(--faint)}
        .grow{padding:10px 0;border-top:1px solid rgba(255,255,255,.05);font-size:13.5px;transition:background .12s}
        .mg.grow{padding:8px 0}
        .grow:hover{background:rgba(255,255,255,.02)}
        .grow .d{font-size:11px;color:var(--dim)}
        .grow .tt{display:flex;align-items:center;gap:9px;color:var(--ink);min-width:0}
        .sp{color:var(--violet);font-size:10px;flex:none}
        .sp.gold{color:var(--gold)}
        .grow .at{font-size:11.5px;color:#b5b2ab}
        .grow .fr{color:#7d7b76;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .grow .mode{color:var(--ink2);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .grow .pl{font-size:11.5px;color:#9a978f;white-space:nowrap}
        .grow .n{font:300 18px/1 "Sora",sans-serif}
        .grow .mt{font-size:12.5px;color:var(--violet);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .win{justify-self:start;padding:2px 7px;border-radius:4px;border:1px solid rgba(230,200,143,.45);background:rgba(230,200,143,.1);font-size:10.5px;letter-spacing:.1em;color:var(--gold)}
        .warn{color:var(--warn)}
        .grow.unusual{background:rgba(224,137,127,.05)}
        .grow.more{display:none}
        section.all .grow.more{display:grid}
        .grow.team{display:none}
        body.showteam .grow.team{display:grid;opacity:.7}
        .more-btn{display:block;margin-top:14px;padding:8px 14px;border-radius:6px;border:1px solid var(--line2);background:transparent;color:var(--muted);font-size:10.5px;letter-spacing:.14em;cursor:pointer;transition:.15s}
        .more-btn:hover{border-color:rgba(230,200,143,.45);color:var(--gold)}
        .teamtoggle{display:inline-flex;align-items:center;gap:8px;margin-top:12px;font-size:10px;letter-spacing:.12em;color:var(--dim);cursor:pointer;user-select:none}
        .teamtoggle input{position:absolute;opacity:0;pointer-events:none}
        .teamtoggle .box{width:9px;height:9px;transform:rotate(45deg);border:1px solid var(--dim);transition:.15s}
        .teamtoggle input:checked+.box{background:var(--gold);border-color:var(--gold)}
        .empty{padding:22px 0;color:var(--dim);font-size:13.5px;border-top:1px solid rgba(255,255,255,.05)}
        footer{display:flex;flex-direction:column;gap:6px;margin-top:64px;padding-top:18px;border-top:1px solid var(--line);font-size:10px;letter-spacing:.1em;color:#4a4946;line-height:1.7}
        .loading{display:flex;gap:16px;align-items:center;margin-top:28px;padding:16px 18px;border-radius:12px;border:1px solid rgba(230,200,143,.25);background:rgba(230,200,143,.04)}
        .loading .muted{color:var(--dim)}
        .spin{width:22px;height:22px;flex:none;border:1px solid var(--line2);border-top-color:var(--gold);border-radius:50%;animation:spin .9s linear infinite}
        @keyframes spin{to{transform:rotate(360deg)}}
        .loading .lgrow{flex:1;min-width:0}
        .pbar{height:2px;background:var(--line2);border-radius:2px;overflow:hidden;margin:10px 0 8px}
        .pbar i{display:block;height:100%;background:var(--gold)}
        .profiles{margin-top:14px;font:400 11px "JetBrains Mono",monospace;letter-spacing:.06em;color:var(--dim)}
        @media (max-width:1100px){main{padding:32px 28px 48px}.hero,.split,.split.even{grid-template-columns:1fr;gap:40px}.huge{font-size:140px}}
        .voy{margin-top:30px;max-width:560px}
        .vtrack{position:relative;height:1px;background:rgba(255,255,255,.12);margin:0 6px}
        .vtrack i{position:absolute;left:0;top:-.5px;height:2px;background:linear-gradient(90deg,rgba(230,200,143,.15),var(--gold))}
        .vm{position:absolute;top:0;transform:translate(-50%,-50%)}
        .vm b{display:block;width:7px;height:7px;transform:rotate(45deg);border:1px solid var(--gold);background:var(--panel)}
        .vm.on b{background:var(--gold)}
        .vm em{position:absolute;top:14px;left:50%;transform:translateX(-50%);font:400 9.5px "JetBrains Mono",monospace;font-style:normal;letter-spacing:.08em;color:var(--dim)}
        .vm.next em{color:var(--gold)}
        .vship{position:absolute;top:0;width:9px;height:9px;border-radius:50%;background:#f7f2e6;transform:translate(-50%,-50%);box-shadow:0 0 0 4px rgba(230,200,143,.18),0 0 18px rgba(230,200,143,.7);animation:breathe 2.6s ease-in-out infinite}
        .vcap{margin-top:36px;font-size:10.5px;letter-spacing:.14em;color:var(--dim)}
        .vcap span{color:var(--gold)}
        .ins{display:grid;grid-template-columns:repeat(4,1fr);border-bottom:1px solid var(--line)}
        .in{padding:28px 24px;border-left:1px solid var(--line);min-width:0}
        .in:first-child{border-left:none;padding-left:0}
        .il{font-size:10px;letter-spacing:.2em;color:var(--muted)}
        .iv{margin-top:12px;font:200 42px/1 "Sora",sans-serif;letter-spacing:-.03em}
        .iv.violet{color:var(--violet)}.iv.gold{color:var(--gold)}
        .ic{margin-top:10px;font-size:12.5px;color:var(--dim)}
        .legend{display:flex;flex-wrap:wrap;gap:22px;margin-top:10px;font-size:9.5px;letter-spacing:.14em;color:var(--dim)}
        .lg{display:inline-flex;align-items:center;gap:8px}.lg.v{color:var(--violet)}.lg.g{color:var(--gold)}
        .ld{width:5px;height:5px;border-radius:50%;background:rgba(242,239,232,.6)}
        .lw{width:6px;height:6px;transform:rotate(45deg);border:1px solid var(--gold)}
        .ll{width:16px;height:1px;background:rgba(242,239,232,.35)}
        .stars{display:block;width:100%;height:auto;margin-top:14px;overflow:visible}
        .stars text{font:400 9.5px "JetBrains Mono",monospace;letter-spacing:.06em;fill:#5a5955}
        .stars g.m{cursor:default}
        .stars g.m:hover .d{fill:#fff;r:4.5px}
        .stars g.m:hover .s{fill:#fff}
        .stars g.m:hover>rect:first-child{fill:rgba(255,255,255,.035)}
        .stars .s{transform-box:fill-box;transform-origin:center;animation:twinkle 3.4s ease-in-out infinite}
        .stars g.m:nth-child(3n) .s{animation-delay:-1.2s}.stars g.m:nth-child(3n+1) .s{animation-delay:-2.3s}
        @keyframes twinkle{50%{transform:scale(.72);opacity:.7}}
        .stars .ping{transform-box:fill-box;transform-origin:center;animation:ping 2.2s ease-out infinite}
        @keyframes ping{from{transform:scale(.5);opacity:.9}to{transform:scale(1.9);opacity:0}}
        #starread.hot,#readout{transition:color .15s}#starread.hot{color:var(--ink)}
        @media (max-width:1100px){.ins{grid-template-columns:repeat(2,1fr)}.in:nth-child(3){border-left:none;padding-left:0}}
        @media (prefers-reduced-motion:reduce){*{animation:none!important;transition:none!important}}
        """;

    /// <summary>@font-face rules for the embedded Sora, Geist and JetBrains Mono (SIL OFL): the page works offline.</summary>
    private static readonly string FontFace = BuildFontFace();

    private static string BuildFontFace()
    {
        var sb = new StringBuilder();
        foreach (var (family, file, weight) in new[]
        {
            ("Sora", "sora-200", 200), ("Sora", "sora-300", 300), ("Sora", "sora-400", 400),
            ("Geist", "geist-sans-300", 300), ("Geist", "geist-sans-400", 400), ("Geist", "geist-sans-500", 500),
            ("JetBrains Mono", "jetbrains-mono-400", 400), ("JetBrains Mono", "jetbrains-mono-500", 500), ("JetBrains Mono", "jetbrains-mono-700", 700),
        })
        {
            try
            {
                using var s = typeof(ReportWriter).Assembly.GetManifestResourceStream($"Fonts.{file}.woff2");
                if (s == null) continue;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                sb.Append($"@font-face{{font-family:'{family}';font-weight:{weight};font-style:normal;font-display:swap;src:url(data:font/woff2;base64,{Convert.ToBase64String(ms.ToArray())}) format('woff2')}}");
            }
            catch
            {
                // the page falls back to Segoe UI / Consolas
            }
        }
        return sb.ToString();
    }


    private const string TeamIcon = """<svg width="16" height="16" viewBox="0 0 24 24" aria-hidden="true"><circle cx="9" cy="8" r="3.2" fill="none" stroke="#8fb3c4" stroke-width="2"/><path d="M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6" fill="none" stroke="#8fb3c4" stroke-width="2"/><circle cx="17" cy="9" r="2.6" fill="none" stroke="#8fb3c4" stroke-width="2"/><path d="M16 14c3 0 5 2.2 5 5" fill="none" stroke="#8fb3c4" stroke-width="2"/></svg>""";
    private const string FlagIcon = """<svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true" style="flex:none;margin-top:2px"><path d="M5 21V4M5 4h11l-2 4 2 4H5" fill="none" stroke="#e0897f" stroke-width="2" stroke-linejoin="round"/></svg>""";

    private static readonly Dictionary<string, string> KindColor = new()
    {
        [ChestKinds.Normal] = "#e6c88f",
        [ChestKinds.Rare] = "#7f8fb8",
        [ChestKinds.Buried] = "#a8875a",
        [ChestKinds.Extraction] = "#8cc4e0",
        [ChestKinds.PosterChest] = "#cfc2f5",
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
        var session = matches.Where(IsThisSession).Sum(m => m.ChestsOpened);
        var player = matches.Select(m => m.PlayerName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";
        // Own treasures only: the team's are found with chests that are not counted here.
        var every = treasures > 0 ? (int)Math.Round((double)totalChests / treasures) : 0;
        string N(int n) => n.ToString("N0", Fr);
        string Up(string s) => s.ToUpperInvariant();

        var sb = new StringBuilder();
        var title = "Plunder Count";
        var synced = $"SYNCED {DateTime.Now.ToString("HH:mm:ss", Fr)} · {Up(DateTime.Now.ToString("d MMM yyyy", Fr))}";
        string Row(string label, string value, string? extra = null, string? cls = null) =>
            $"""<div class="krow"><span class="k">{label}{(extra != null ? $" <span class=\"kx\">{extra}</span>" : "")}</span><span class="kv{(cls != null ? " " + cls : "")}">{value}</span></div>""";

        sb.Append($"""
            <!doctype html><html lang="{L.Code}"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="refresh" content="{(progress != null ? 2 : 15)}">
            <title>{title}</title><style>{FontFace}{Style}</style></head><body><main>
            <header class="top">
              <div class="brand"><span class="dia glow"></span><span class="name">{title}</span>{(player.Length > 0 ? $"<span class=\"who\">/ {H(Up(player))}</span>" : "")}</div>
              <div class="sync"><span class="gdot"></span>{synced}</div>
            </header>
            {LoadingCard(progress)}{ProfilesLine(profiles, player)}
            <section class="hero">
              <div>
                <div class="eyebrow">CHESTS IN TOTAL</div>
                <div class="huge">{N(totalChests)}</div>
                <div class="sub">opened or destroyed by you · {N(session)} since the tool started</div>
                {Voyage(totalChests)}
              </div>
              <div class="krows">
                {Row("MATCHES ANALYZED", N(matches.Count))}
                {Row("CHESTS PER MATCH", avg.ToString("0.0", Fr))}
                {Row("BEST MATCH", N(best))}
                {Row("TREASURES FOUND", "✦ " + N(treasures), every > 0 ? $"· 1 PER {N(every)}" : null, "violet")}
              </div>
            </section>
            {Insights(matches)}{StarChart(matches)}
            """);

        // Chests per day: 30 pill bars, best day in gold, hover shows the day in the readout.
        var today = DateTime.Now.Date;
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29)).ToList();
        var perDay = days.Select(d => matches.Where(m => When(m).Date == d).Sum(m => m.ChestsOpened)).ToList();
        var max = Math.Max(1, perDay.Max());
        var bestIdx = perDay.IndexOf(perDay.Max());
        string DayLabel(int i) => i == 29 ? "TODAY" : Up(days[i].ToString("d MMM", Fr));
        var readout = perDay[bestIdx] > 0 ? $"BEST DAY · {DayLabel(bestIdx)} · {N(perDay[bestIdx])} CHESTS" : "NO CHESTS YET";
        sb.Append($"""<section class="block"><div class="bhead"><h2>Chests per day</h2><div class="mono dim" id="readout" data-default="{H(readout)}">{H(readout)}</div></div><div class="chart" id="chart">""");
        for (var i = 0; i < days.Count; i++)
        {
            var v = perDay[i];
            var px = v > 0 ? Math.Max(4, (int)Math.Round(170.0 * v / max)) : 4;
            var cls = v == 0 ? "zero" : i == bestIdx ? "best" : "";
            sb.Append($"""<div class="col{(i == bestIdx && v > 0 ? " isbest" : "")}" data-r="{H(DayLabel(i))} · {N(v)} CHESTS"><span class="lab">{(v > 0 ? N(v) : "")}</span><i class="{cls}" style="height:{px}px"></i></div>""");
        }
        sb.Append("</div><div class=\"axis\">");
        for (var i = 0; i < days.Count; i++)
        {
            var major = (i % 7 == 0 && i < 26) || i == 29; // no label right before "TODAY": they would overlap
            sb.Append($"""<div><span class="t{(major ? " major" : "")}"></span><span class="al">{(major ? DayLabel(i) : "")}</span></div>""");
        }
        sb.Append("</div></section>");

        // By chest type | treasure sources.
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
        sb.Append($"""<div class="split"><section><div class="bhead"><h2>By chest type</h2><div class="mono dim">{N(kindTotal == 1 && byKind.Count == 0 ? 0 : kindTotal)}</div></div><div class="stack">""");
        foreach (var g in byKind)
        {
            sb.Append($"""<span style="flex-grow:{g.Count()};background:{KindColor.GetValueOrDefault(g.Key, "#6f747d")}" title="{H(L.Name(g.Key))}"></span>""");
        }
        sb.Append("</div><div class=\"types\">");
        foreach (var g in byKind)
        {
            var color = KindColor.GetValueOrDefault(g.Key, "#6f747d");
            sb.Append($"""<div class="trow"><span class="tn"><span class="dia" style="background:{color};border-color:{color}"></span>{H(L.Name(g.Key))}</span><span class="tp">{Math.Round(100.0 * g.Count() / kindTotal)}%</span><span class="tv">{N(g.Count())}</span></div>""");
        }
        var destroyedTotal = matches.Sum(m => m.Chests.Count(c => c.Destroyed));
        if (destroyedTotal > 0)
        {
            sb.Append($"""<div class="trow extra"><span>OF WHICH DESTROYED</span><span></span><span>{N(destroyedTotal)}</span></div>""");
        }
        foreach (var (kind, count) in otherKinds)
        {
            sb.Append($"""<div class="trow extra"><span>{H(Up(L.Name(kind)))} · NOT IN TOTAL</span><span></span><span>{N(count)}</span></div>""");
        }
        sb.Append("</div></section>");

        var treasureRows = matches.SelectMany(m => m.TreasureFinds.Select(f => (m, f, Team: false))).ToList();
        var teamRows = matches.SelectMany(m => m.TeamTreasureFinds.Select(f => (m, f, Team: true))).ToList();
        sb.Append("<section><div class=\"bhead\"><h2>Treasure sources</h2></div>");
        if (treasureRows.Count == 0)
        {
            sb.Append("<div class=\"tcard\"><div class=\"tc-empty\">No treasure yet.</div></div>");
        }
        foreach (var g in treasureRows.GroupBy(r => r.f.SourceLabel).OrderByDescending(g => g.Count()))
        {
            var isChest = g.First().f.SourceIsChest;
            var pct = Math.Round(100.0 * g.Count() / treasureRows.Count);
            sb.Append($"""<div class="tcard{(isChest ? "" : " warn")}"><div class="tc-top"><span>{H(g.Key)}</span><span class="tc-n">{g.Count()}</span></div><div class="tc-bar" style="width:{Math.Max(4, pct)}%"></div><div class="tc-cap">{pct}% OF TREASURES{(isChest ? "" : " · LOW RELIABILITY")}</div></div>""");
        }
        var unusual = treasureRows.Where(r => !r.f.SourceIsChest).ToList();
        if (unusual.Count > 0)
        {
            sb.Append($"<div class=\"flag\">{FlagIcon}<span><b>{L.Count(unusual.Count, "treasure", "treasures")} obtained other than from a chest.</b> Low reliability: most likely a mistake in the way the replays are analyzed.</span></div>");
        }
        sb.Append("</section></div>");

        // Treasures found | latest matches.
        sb.Append("<div class=\"split even\"><section>");
        sb.Append($"""<div class="bhead"><h2>Treasures found</h2><div class="mono dim">{treasures}</div></div>""");
        if (teamRows.Count > 0)
        {
            sb.Append($"""<label class="teamtoggle mono"><input type="checkbox" id="showteam"><span class="box"></span>SHOW TEAM FINDS · +{teamRows.Count} ESTIMATE</label>""");
        }
        sb.Append("<div class=\"grid tg ghead mono\"><span>MATCH</span><span>TREASURE</span><span class=\"r\">AT</span><span>FROM</span><span>BY</span></div>");
        if (treasureRows.Count == 0 && teamRows.Count == 0)
        {
            sb.Append("<div class=\"empty\">Your treasures will show up here.</div>");
        }
        var ti = 0;
        foreach (var (m, f, team) in treasureRows.Concat(teamRows).OrderByDescending(r => When(r.m)).ThenBy(r => r.f.ObtainedAtSeconds ?? 0))
        {
            var more = !team && ti++ >= 15;
            var at = f.ObtainedAtSeconds is float t ? (f.TimeExact ? Pretty.Clock(t) : $"~{Pretty.Clock(t)}") : "?";
            var from = team && string.IsNullOrEmpty(f.Teammate) ? "?" : !f.SourceIsChest ? $"<span class=\"warn\" title=\"low reliability\">{H(f.SourceLabel)}</span>" : H(L.Name(f.SourceKind) is { Length: > 0 } k ? k : f.SourceLabel);
            var by = !team ? "you" : (string.IsNullOrEmpty(f.Teammate) ? "team" : H(f.Teammate));
            var glyph = f.FromGoldenChest ? "<span class=\"sp gold\">✦✦</span>" : "<span class=\"sp\">✦</span>";
            var cls = (team ? " team" : "") + (more ? " more" : "") + (!f.SourceIsChest ? " unusual" : "");
            sb.Append($"""<div class="grid tg grow{cls}"><span class="d mono">{Up(L.DateTimeShort(When(m)))}</span><span class="tt">{glyph}{H(f.DisplayName)}</span><span class="r at mono">{at}</span><span class="fr">{from}</span><span class="fr">{by}</span></div>""");
        }
        if (treasureRows.Count > 15)
        {
            sb.Append($"""<button class="more-btn mono" data-target="t" type="button">SHOW ALL {treasureRows.Count}</button>""");
        }
        sb.Append("</section><section>");

        var shown = Math.Min(15, matches.Count);
        sb.Append($"""<div class="bhead"><h2>Latest matches</h2><div class="mono dim" id="mcount">{shown} / {N(matches.Count)}</div></div>""");
        sb.Append("<div class=\"grid mg ghead mono\"><span>DATE</span><span>MODE</span><span>PLACE</span><span class=\"r\">CHESTS</span><span>TREASURE</span></div>");
        if (matches.Count == 0)
        {
            sb.Append("<div class=\"empty\">Play a match with replays on: it shows up here a few seconds after it ends.</div>");
        }
        var mi = 0;
        foreach (var m in matches.Take(100))
        {
            var more = mi++ >= 15;
            var place = m.Placement == 1
                ? "<span class=\"win mono\">WIN</span>"
                : m.Placement is int p && p > 0 ? $"<span class=\"pl mono\">{p} /{(m.TotalPlayers is int tp && tp > 0 ? tp : 100)}</span>" : "<span class=\"pl mono\">?</span>";
            var tr = string.Join(", ", m.TreasureFinds.Select(f => H(f.DisplayName)));
            sb.Append($"""<div class="grid mg grow{(more ? " more" : "")}"><span class="d mono">{Up(L.DateTimeShort(When(m)))}</span><span class="mode">{H(Pretty.Playlist(m.Playlist))}</span>{place}<span class="r n">{m.ChestsOpened}</span><span class="mt">{(tr.Length > 0 ? "✦ " + tr : "")}</span></div>""");
        }
        if (matches.Count > 15)
        {
            sb.Append($"""<button class="more-btn mono" data-target="m" data-all="{Math.Min(100, matches.Count)} / {N(matches.Count)}" type="button">SHOW {Math.Min(100, matches.Count)} MATCHES</button>""");
        }
        sb.Append("</section></div>");

        var script = """<script>(function(){var k='plundercount-showteam',b=document.getElementById('showteam');var on=false;try{on=localStorage.getItem(k)==='1'}catch(e){}function set(v){document.body.classList.toggle('showteam',v);if(b)b.checked=v}set(on);if(b)b.addEventListener('change',function(){set(b.checked);try{localStorage.setItem(k,b.checked?'1':'0')}catch(e){}})})();</script>""";
        var interactions = """
            <script>(function(){
            var c=document.getElementById('chart'),r=document.getElementById('readout');
            if(c&&r){c.querySelectorAll('.col').forEach(function(col){col.addEventListener('mouseenter',function(){c.classList.add('hovering');c.querySelectorAll('.col.hov').forEach(function(x){x.classList.remove('hov')});col.classList.add('hov');r.textContent=col.getAttribute('data-r')})});
            c.addEventListener('mouseleave',function(){c.classList.remove('hovering');c.querySelectorAll('.col.hov').forEach(function(x){x.classList.remove('hov')});r.textContent=r.getAttribute('data-default')})}
            var sc=document.getElementById('stars'),sr=document.getElementById('starread');
            if(sc&&sr){sc.querySelectorAll('g.m').forEach(function(m){m.addEventListener('mouseenter',function(){sr.textContent=m.getAttribute('data-r');sr.classList.add('hot')})});
            sc.addEventListener('mouseleave',function(){sr.textContent=sr.getAttribute('data-default');sr.classList.remove('hot')})}
            document.querySelectorAll('.more-btn').forEach(function(btn){btn.addEventListener('click',function(){var s=btn.closest('section');s.classList.add('all');var n=btn.getAttribute('data-all'),m=document.getElementById('mcount');if(n&&m)m.textContent=n;btn.remove()})});
            })();</script>
            """;
        sb.Append("<footer class=\"mono\"><span>COUNTS THE CHESTS YOU OPENED OR DESTROYED, READ FROM YOUR REPLAYS AFTER EACH MATCH · AMMO AND FOOD BOXES NOT COUNTED · ✦✦ GOLDEN CHEST (CERTAIN) · ✦ FROM ONE OF YOUR CHESTS</span><span>PLUNDER COUNT · NOT AFFILIATED WITH EPIC GAMES</span></footer>");
        sb.Append(script).Append(interactions).Append("</main></body></html>");
        return sb.ToString();
    }

    private static string Inv(double d) => d.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>The road to the next milestone: passed milestones as filled diamonds, a glowing marker where you are.</summary>
    private static string Voyage(int total)
    {
        var next = Milestones.Next(total);
        var sb = new StringBuilder();
        var pos = 100.0 * total / next;
        sb.Append($"""<div class="voy"><div class="vtrack"><i style="width:{Inv(pos)}%"></i>""");
        foreach (var m in Milestones.UpTo(next).Where(m => 100.0 * m / next >= 12).TakeLast(5))
        {
            var cls = m == next ? "next" : "on";
            sb.Append($"""<span class="vm {cls}" style="left:{Inv(100.0 * m / next)}%"><b></b><em>{m.ToString("N0", Fr)}</em></span>""");
        }
        sb.Append($"""<span class="vship" style="left:{Inv(pos)}%"></span></div>""");
        sb.Append($"""<div class="vcap mono">NEXT MILESTONE · {next.ToString("N0", Fr)} · <span>{(next - total).ToString("N0", Fr)} TO GO</span></div></div>""");
        return sb.ToString();
    }

    /// <summary>Four things the numbers say: dry spell, longest drought, best mode, peak hour.</summary>
    private static string Insights(IReadOnlyList<MatchRecord> newestFirst)
    {
        if (newestFirst.Count == 0)
        {
            return "";
        }
        var chrono = newestFirst.Reverse().ToList();
        int run = 0, longest = 0;
        var anyTreasure = false;
        foreach (var m in chrono)
        {
            run += m.ChestsOpened;
            if (m.TreasureFinds.Count > 0)
            {
                anyTreasure = true;
                longest = Math.Max(longest, run);
                run = 0;
            }
        }
        longest = Math.Max(longest, run);
        var mode = chrono.GroupBy(m => Pretty.Playlist(m.Playlist)).Where(g => g.Count() >= 2)
            .Select(g => (Name: g.Key, Avg: g.Average(m => m.ChestsOpened), Count: g.Count())).OrderByDescending(x => x.Avg).FirstOrDefault();
        var hour = chrono.GroupBy(m => When(m).Hour).Where(g => g.Count() >= 2)
            .Select(g => (Hour: g.Key, Avg: g.Average(m => m.ChestsOpened), Count: g.Count())).OrderByDescending(x => x.Avg).FirstOrDefault();

        string Cell(string label, string value, string caption, string cls = "") =>
            $"""<div class="in"><div class="il mono">{label}</div><div class="iv{(cls.Length > 0 ? " " + cls : "")}">{value}</div><div class="ic">{caption}</div></div>""";

        var sb = new StringBuilder("<section class=\"ins\">");
        sb.Append(Cell("DRY SPELL", run.ToString("N0", Fr), anyTreasure ? "chests since your last treasure" : "chests, no treasure yet", "violet"));
        sb.Append(Cell("LONGEST DROUGHT", longest.ToString("N0", Fr), "chests between two treasures"));
        sb.Append(mode.Name != null
            ? Cell("BEST MODE", mode.Avg.ToString("0.0", Fr), $"per match in {H(mode.Name)} · {mode.Count} played", "gold")
            : Cell("BEST MODE", "—", "play a mode twice to compare"));
        sb.Append(hour.Count > 0
            ? Cell("PEAK HOUR", $"{hour.Hour:00}:00", $"{hour.Avg.ToString("0.0", Fr)} chests per match around then")
            : Cell("PEAK HOUR", "—", "not enough matches yet"));
        return sb.Append("</section>").ToString();
    }

    /// <summary>Every match as a star: height = chests, ✦ = a treasure (linked like a constellation), ◇ = a win.</summary>
    private static string StarChart(IReadOnlyList<MatchRecord> newestFirst)
    {
        var ms = newestFirst.Take(60).Reverse().ToList();
        if (ms.Count < 2)
        {
            return "";
        }
        const double W = 1160, Hh = 280, Lp = 36, Rp = 14, Tp = 18, Bp = 26;
        var max = Math.Max(5, (int)Math.Ceiling(ms.Max(m => m.ChestsOpened) / 5.0) * 5);
        double X(int i) => Lp + i * (W - Lp - Rp) / (ms.Count - 1);
        double Y(double v) => Tp + (Hh - Tp - Bp) * (1 - v / max);

        var sb = new StringBuilder();
        var legend = "<span class=\"lg\"><i class=\"ld\"></i>MATCH</span><span class=\"lg v\">✦ TREASURE</span><span class=\"lg g\"><i class=\"lw\"></i>WIN</span><span class=\"lg\"><i class=\"ll\"></i>5-MATCH AVERAGE</span>";
        var def = $"LAST {ms.Count} MATCHES";
        sb.Append($"""<section class="block"><div class="bhead"><h2>Star chart</h2><div class="mono dim" id="starread" data-default="{def}">{def}</div></div><div class="legend mono">{legend}</div>""");
        sb.Append($"""<svg class="stars" id="stars" viewBox="0 0 {Inv(W)} {Inv(Hh)}" role="img" aria-label="Chests per match">""");
        foreach (var gv in new[] { 0, max / 2, max })
        {
            sb.Append($"""<line x1="{Inv(Lp)}" x2="{Inv(W - Rp)}" y1="{Inv(Y(gv))}" y2="{Inv(Y(gv))}" stroke="rgba(255,255,255,.055)"/><text x="{Inv(Lp - 10)}" y="{Inv(Y(gv) + 3)}" text-anchor="end">{gv}</text>""");
        }
        // Rolling average of 5 matches.
        var avg = new StringBuilder();
        for (var i = 0; i < ms.Count; i++)
        {
            var from = Math.Max(0, i - 4);
            var a = ms.Skip(from).Take(i - from + 1).Average(m => m.ChestsOpened);
            avg.Append($"{(i == 0 ? "M" : " L")}{Inv(X(i))},{Inv(Y(a))}");
        }
        sb.Append($"""<path d="{avg}" fill="none" stroke="rgba(242,239,232,.2)" stroke-width="1.2" stroke-linejoin="round"/>""");
        // Constellation: the matches that gave a treasure, linked.
        var stars = Enumerable.Range(0, ms.Count).Where(i => ms[i].TreasureFinds.Count > 0).ToList();
        if (stars.Count > 1)
        {
            sb.Append($"""<polyline points="{string.Join(" ", stars.Select(i => $"{Inv(X(i))},{Inv(Y(ms[i].ChestsOpened))}"))}" fill="none" stroke="rgba(207,194,245,.38)" stroke-width="1" stroke-dasharray="2 5" stroke-linecap="round"/>""");
        }
        for (var i = 0; i < ms.Count; i++)
        {
            var m = ms[i];
            double x = X(i), y = Y(m.ChestsOpened);
            var win = m.Placement == 1;
            var tr = m.TreasureFinds.Count > 0;
            var read = $"{L.DateTimeShort(When(m)).ToUpperInvariant()} · {Pretty.Playlist(m.Playlist).ToUpperInvariant()} · {m.ChestsOpened} CHESTS"
                + (win ? " · WIN" : "") + (tr ? " · ✦ " + string.Join(", ", m.TreasureFinds.Select(f => f.DisplayName)).ToUpperInvariant() : "");
            sb.Append($"""<g class="m" data-r="{H(read)}"><rect x="{Inv(x - 9)}" y="{Inv(Tp)}" width="18" height="{Inv(Hh - Tp - Bp)}" fill="transparent"/>""");
            if (win)
            {
                sb.Append($"""<rect x="{Inv(x - 6)}" y="{Inv(y - 6)}" width="12" height="12" transform="rotate(45 {Inv(x)} {Inv(y)})" fill="none" stroke="#e6c88f" stroke-width="1"/>""");
            }
            if (tr)
            {
                const double r = 8, k = 1.9;
                sb.Append($"""<circle cx="{Inv(x)}" cy="{Inv(y)}" r="13" fill="rgba(207,194,245,.1)"/><path class="s" d="M{Inv(x)},{Inv(y - r)} L{Inv(x + k)},{Inv(y - k)} L{Inv(x + r)},{Inv(y)} L{Inv(x + k)},{Inv(y + k)} L{Inv(x)},{Inv(y + r)} L{Inv(x - k)},{Inv(y + k)} L{Inv(x - r)},{Inv(y)} L{Inv(x - k)},{Inv(y - k)} Z" fill="#cfc2f5"/>""");
            }
            else
            {
                sb.Append($"""<circle class="d" cx="{Inv(x)}" cy="{Inv(y)}" r="2.6" fill="{(win ? "#e6c88f" : "rgba(242,239,232,.6)")}"/>""");
            }
            if (i == ms.Count - 1)
            {
                sb.Append($"""<circle class="ping" cx="{Inv(x)}" cy="{Inv(y)}" r="7" fill="none" stroke="#f2efe8" stroke-width="1"/>""");
            }
            sb.Append("</g>");
        }
        sb.Append($"""<text x="{Inv(Lp)}" y="{Inv(Hh - 6)}">{L.DateTimeShort(When(ms[0])).ToUpperInvariant()}</text><text x="{Inv(W - Rp)}" y="{Inv(Hh - 6)}" text-anchor="end">LATEST</text>""");
        return sb.Append("</svg></section>").ToString();
    }

    private static string LoadingCard(ScanProgress? p)
    {
        if (p == null)
        {
            return "";
        }
        var pct = p.Total > 0 ? (int)Math.Round(100.0 * p.Done / p.Total) : 0;
        return $"""<div class="loading"><div class="spin"></div><div class="lgrow"><b>Analyzing your replays… {p.Done} / {p.Total}</b><div class="pbar"><i style="width:{pct}%"></i></div><span class="muted">This page updates by itself: the numbers below grow as the replays are read.</span></div></div>""";
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
        // Same card as the in-game overlay (design 2a), for an OBS browser source (transparent background).
        var live = v.CurrentLive && v.Current is { } c && c != "…";
        var (status, color, sec, secColor, secLabel) = live
            ? ("LIVE", "#8fd6a8", v.Current!, "#8fd6a8", "THIS MATCH")
            : v.CurrentLive
                ? ("IN MATCH", "#e6c88f", "···", "#e6c88f", "IN PROGRESS")
                : ("IDLE", "#8f8d88", v.Last ?? "—", "#f2efe8", "LAST");
        var on = v.CurrentLive && !live ? 0 : (int)Math.Round(26.0 * Math.Clamp(v.TickValue, 0, Math.Max(1, v.Best)) / Math.Max(1, v.Best));
        var tickOn = live ? "#8fd6a8" : "rgba(242,239,232,.5)";
        var ticks = string.Concat(Enumerable.Range(0, 26).Select(i => $"<i style=\"background:{(i < on ? tickOn : "rgba(255,255,255,.08)")}\"></i>"));
        return $$"""
            <!doctype html><html lang="{{L.Code}}"><head><meta charset="utf-8"><meta http-equiv="refresh" content="10">
            <title>Plunder Count overlay</title>
            <style>{{FontFace}}
            html,body{margin:0;background:transparent}
            .wrap{display:inline-block;margin:24px;padding:1px;border-radius:14px;background:linear-gradient(180deg,rgba(255,255,255,.12),rgba(255,255,255,.04));box-shadow:0 30px 60px -20px rgba(0,0,0,.7)}
            .card{width:340px;box-sizing:border-box;border-radius:13px;padding:16px 20px 14px;background:linear-gradient(180deg,rgba(18,18,22,.84),rgba(10,10,12,.9));color:#f2efe8;font-family:"Geist","Segoe UI",sans-serif}
            .mono{font-family:"JetBrains Mono",Consolas,monospace}
            .head{display:flex;justify-content:space-between;align-items:center;font-size:9.5px;letter-spacing:.22em}
            .head span{display:flex;align-items:center;gap:8px}
            .dia{width:5px;height:5px;transform:rotate(45deg);border:1px solid #e6c88f}
            .dot{width:5px;height:5px;border-radius:50%}
            .main{display:flex;align-items:flex-end;justify-content:space-between;margin-top:10px}
            .tot{font:200 72px/.9 "Sora",sans-serif;letter-spacing:-.05em;font-variant-numeric:lining-nums tabular-nums}
            .unit{font:300 13px "Sora",sans-serif;color:#8f8d88;margin-left:8px}
            .sec{text-align:right;padding-bottom:6px}
            .sec b{display:block;font:200 30px/1 "Sora",sans-serif;letter-spacing:-.03em}
            .sec span{display:block;margin-top:5px;font-size:9px;letter-spacing:.2em;color:#6f6d69}
            .ticks{display:flex;gap:3px;margin-top:12px}.ticks i{flex:1;height:3px;border-radius:1px}
            .foot{display:flex;justify-content:space-between;margin-top:12px;font-size:10.5px;letter-spacing:.08em;color:#8f8d88}
            .foot b{font-weight:500;color:#f2efe8}.foot .v{color:#c4b5f2}
            </style></head><body><div class="wrap"><div class="card">
            <div class="head mono"><span style="color:#8f8d88"><span class="dia"></span>PLUNDER · COUNT</span><span style="color:{{color}}"><span class="dot" style="background:{{color}};box-shadow:0 0 10px {{color}}"></span>{{status}}</span></div>
            <div class="main"><div><span class="tot">{{H(v.Total)}}</span><span class="unit">chests</span></div><div class="sec"><b style="color:{{secColor}}">{{H(sec)}}</b><span class="mono">{{secLabel}}</span></div></div>
            <div class="ticks">{{ticks}}</div>
            <div class="foot mono"><span>SESSION <b>{{H(v.Session)}}</b></span><span>LAST <b>{{H(v.Last ?? "—")}}</b></span><span>TREASURES <b class="v">✦ {{H(v.Treasures)}}</b></span></div>
            </div></div></body></html>
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
