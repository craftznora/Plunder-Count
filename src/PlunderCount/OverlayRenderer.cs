using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PlunderCount;

/// <summary>
/// Draws the overlay (design "2a") into a 32-bit bitmap with real per-pixel transparency (GDI+). NativeOverlay puts
/// the bitmap on screen with UpdateLayeredWindow. Fonts are embedded in the exe (SIL OFL, see Assets/Fonts).
/// </summary>
[SupportedOSPlatform("windows")]
public static class OverlayRenderer
{
    // ---- Palette (design tokens) ----------------------------------------------------------------
    private static readonly Color Ink = Color.FromArgb(242, 239, 232);      // #f2efe8
    private static readonly Color InkBright = Color.FromArgb(247, 245, 240); // #f7f5f0
    private static readonly Color Muted = Color.FromArgb(143, 141, 136);    // #8f8d88
    private static readonly Color Dim = Color.FromArgb(111, 109, 105);      // #6f6d69
    private static readonly Color Gold = Color.FromArgb(230, 200, 143);     // #e6c88f
    private static readonly Color GoldPale = Color.FromArgb(241, 217, 166); // #f1d9a6
    private static readonly Color Green = Color.FromArgb(143, 214, 168);    // #8fd6a8
    private static readonly Color Violet = Color.FromArgb(196, 181, 242);   // #c4b5f2
    private static readonly Color VioletPale = Color.FromArgb(207, 194, 245); // #cfc2f5
    private static readonly Color Silver = Color.FromArgb(201, 198, 191);   // #c9c6bf

    // ---- Embedded fonts -------------------------------------------------------------------------
    private static readonly PrivateFontCollection Fonts = LoadFonts();

    private static PrivateFontCollection LoadFonts()
    {
        var pfc = new PrivateFontCollection();
        var asm = typeof(OverlayRenderer).Assembly;
        foreach (var name in new[] { "display-200.ttf", "display-300.ttf", "mono-400.ttf", "mono-500.ttf" })
        {
            try
            {
                using var s = asm.GetManifestResourceStream("Fonts." + name);
                if (s == null) continue;
                var bytes = new byte[s.Length];
                s.ReadExactly(bytes);
                // GDI+ reads the font from this memory for as long as the collection lives: never freed.
                var mem = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, mem, bytes.Length);
                pfc.AddMemoryFont(mem, bytes.Length);
            }
            catch
            {
                // a missing font falls back to a system one
            }
        }
        return pfc;
    }

    private static FontFamily Family(string embedded, params string[] fallbacks)
    {
        foreach (var f in Fonts.Families)
        {
            if (f.Name.Equals(embedded, StringComparison.OrdinalIgnoreCase)) return f;
        }
        foreach (var n in fallbacks)
        {
            try { return new FontFamily(n); } catch (ArgumentException) { }
        }
        return FontFamily.GenericSansSerif;
    }

    private static readonly Lazy<FontFamily> DisplayXL = new(() => Family("PlunderDisplay ExtraLight", "Segoe UI Light", "Segoe UI"));
    private static readonly Lazy<FontFamily> DisplayL = new(() => Family("PlunderDisplay Light", "Segoe UI Semilight", "Segoe UI"));
    private static readonly Lazy<FontFamily> Mono = new(() => Family("PlunderMono", "Cascadia Mono", "Consolas"));
    private static readonly Lazy<FontFamily> MonoM = new(() => Family("PlunderMono Medium", "Cascadia Mono", "Consolas"));

    private static Font F(Lazy<FontFamily> fam, float px) => new(fam.Value, px, FontStyle.Regular, GraphicsUnit.Pixel);

    // ---- Text helpers ---------------------------------------------------------------------------
    private static readonly StringFormat Typo = CreateTypo();

    private static StringFormat CreateTypo()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        return f;
    }

    private static readonly Graphics Measurer = Graphics.FromImage(new Bitmap(1, 1));

    private static float Width(string text, Font font) => text.Length == 0 ? 0 : Measurer.MeasureString(text, font, PointF.Empty, Typo).Width;

    /// <summary>Width of text drawn with letter spacing (negative = tighter, like CSS letter-spacing).</summary>
    private static float TrackedWidth(string text, Font font, float tracking)
    {
        float w = 0;
        foreach (var c in text) w += Width(c.ToString(), font) + tracking;
        return Math.Max(0, w - tracking);
    }

    private static void Tracked(Graphics g, string text, Font font, Color color, float x, float y, float tracking)
    {
        using var b = new SolidBrush(color);
        foreach (var c in text)
        {
            var s = c.ToString();
            g.DrawString(s, font, b, x, y, Typo);
            x += Width(s, font) + tracking;
        }
    }

    /// <summary>Distance from the top of the line box to the baseline.</summary>
    private static float Ascent(Font f) => f.Size * f.FontFamily.GetCellAscent(FontStyle.Regular) / f.FontFamily.GetEmHeight(FontStyle.Regular);

    private static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Graphics Prepare(Bitmap bmp)
    {
        var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias; // grayscale: ClearType needs an opaque background
        g.Clear(Color.Transparent);
        return g;
    }

    /// <summary>Soft shadow under the card (box-shadow: 0 30px 60px -20px rgba(0,0,0,.7)).</summary>
    private static void Shadow(Graphics g, RectangleF card, float radius, float s)
    {
        var steps = (int)Math.Max(8, 24 * s);
        var spread = 24f * s;
        var offset = 12f * s;
        for (var i = steps; i >= 1; i--)
        {
            var t = (float)i / steps;
            var grow = spread * t - 8 * s;
            var alpha = (int)Math.Round(46 * Math.Pow(1 - t, 2.4));
            if (alpha <= 0) continue;
            var r = new RectangleF(card.X - grow, card.Y - grow + offset, card.Width + 2 * grow, card.Height + 2 * grow);
            if (r.Width <= 0 || r.Height <= 0) continue;
            using var path = Round(r, Math.Max(2, radius + grow));
            using var b = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            g.FillPath(b, path);
        }
    }

    /// <summary>The brand diamond: a small square turned 45°, outlined or filled.</summary>
    private static void Diamond(Graphics g, float cx, float cy, float size, Color color, bool filled, float s, bool glow = false)
    {
        var h = size * 0.7071f; // half diagonal of a square of side "size"
        var pts = new[] { new PointF(cx, cy - h), new PointF(cx + h, cy), new PointF(cx, cy + h), new PointF(cx - h, cy) };
        if (glow)
        {
            using var halo = new SolidBrush(Color.FromArgb(34, color));
            g.FillEllipse(halo, cx - h * 1.7f, cy - h * 1.7f, h * 3.4f, h * 3.4f);
        }
        if (filled)
        {
            using var b = new SolidBrush(color);
            g.FillPolygon(b, pts);
        }
        else
        {
            using var p = new Pen(color, Math.Max(1f, s));
            g.DrawPolygon(p, pts);
        }
    }

    /// <summary>Status dot with a soft glow (box-shadow: 0 0 10px color).</summary>
    private static void Dot(Graphics g, float cx, float cy, float d, Color color)
    {
        for (var i = 3; i >= 1; i--)
        {
            var r = d / 2 + i * d * 0.28f;
            using var halo = new SolidBrush(Color.FromArgb(26, color));
            g.FillEllipse(halo, cx - r, cy - r, 2 * r, 2 * r);
        }
        using var b = new SolidBrush(color);
        g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
    }

    /// <summary>The ✦ treasure glyph, drawn (not in the embedded fonts).</summary>
    private static void Sparkle(Graphics g, float cx, float cy, float r, Color color)
    {
        var k = r * 0.24f;
        var pts = new[]
        {
            new PointF(cx, cy - r), new PointF(cx + k, cy - k), new PointF(cx + r, cy), new PointF(cx + k, cy + k),
            new PointF(cx, cy + r), new PointF(cx - k, cy + k), new PointF(cx - r, cy), new PointF(cx - k, cy - k),
        };
        using var b = new SolidBrush(color);
        g.FillPolygon(b, pts);
    }

    /// <summary>Text with a soft dark halo (text-shadow: 0 1px 2px rgba(0,0,0,.6), 0 0 22px rgba(0,0,0,.4)).</summary>
    private static void Shadowed(Graphics g, string text, Font font, Color color, float x, float y, float tracking, float s)
    {
        using var path = new GraphicsPath();
        var cx = x;
        foreach (var c in text)
        {
            var ch = c.ToString();
            path.AddString(ch, font.FontFamily, (int)FontStyle.Regular, font.Size, new PointF(cx, y), Typo);
            cx += Width(ch, font) + tracking;
        }
        for (var i = 6; i >= 1; i--)
        {
            using var pen = new Pen(Color.FromArgb(14 + (6 - i) * 7, 0, 0, 0), i * 2.2f * s) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
        }
        using (var m = new Matrix())
        {
            m.Translate(0, 1 * s);
            using var drop = (GraphicsPath)path.Clone();
            drop.Transform(m);
            using var db = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            g.FillPath(db, drop);
        }
        using var b = new SolidBrush(color);
        g.FillPath(b, path);
    }

    // ---- State ----------------------------------------------------------------------------------

    private sealed record State(string Status, Color StatusColor, string Sec, Color SecColor, string SecLabel, int Ticks, Color TickColor, Color TotalColor);

    private static State Resolve(OverlayView v, bool locked)
    {
        var liveValue = v.CurrentLive && v.Current is { } cur && cur != "…";
        var tick = (int)Math.Round(26.0 * Math.Clamp(v.TickValue, 0, Math.Max(1, v.Best)) / Math.Max(1, v.Best));
        State st;
        if (liveValue)
        {
            st = new State("LIVE", Green, v.Current!, Green, "THIS MATCH", tick, Green, GoldPale);
        }
        else if (v.CurrentLive)
        {
            st = new State("IN MATCH", Gold, "···", Gold, "IN PROGRESS", 0, Gold, InkBright);
        }
        else
        {
            st = new State("IDLE", Muted, v.Last ?? "—", Ink, "LAST", tick, Color.FromArgb(128, Ink), InkBright);
        }
        return locked ? st : st with { Status = "UNLOCKED", StatusColor = Silver };
    }

    // ---- Card (default): the "dial" ---------------------------------------------------------------
    //
    //   ◇ PLUNDER · COUNT                              ● LIVE
    //    ╭ ' ' ' ╮      TOTAL CHESTS
    //   '   12    '     642
    //   ' THIS MATCH'   NEXT 750            108 TO GO
    //    ╰ BEST 26╯     ━━━━━━━━━━━━━◆─────────────
    //   ▂▅▃▆▂▇▄▅▃▆▄█▅▃▆▄                SESSION 12   ✦ 22
    //
    // The dial compares this match (or the last one) with your best match; the line is the road to the next
    // milestone; the small bars are your latest matches (best in gold, newest highlighted).

    /// <param name="lockRect">Click target that locks the overlay (window coordinates); empty when locked.</param>
    public static Bitmap Glass(OverlayView v, bool locked, float s, out Rectangle lockRect)
    {
        var st = Resolve(v, locked);
        var margin = 30f * s;          // room for the shadow
        var padX = 20f * s;
        var padTop = 16f * s;
        var padBottom = 16f * s;
        var cardW = 360f * s;

        using var fHead = F(Mono, 9.5f * s);
        using var fSec = F(DisplayXL, 30f * s);
        using var fTiny = F(Mono, 8f * s);
        using var fFoot = F(Mono, 10f * s);
        using var fFootValue = F(MonoM, 10f * s);

        var headH = 12f * s;
        var dial = 96f * s;
        var histH = 24f * s;
        var cardH = padTop + headH + 14 * s + dial + 16 * s + histH + padBottom;

        var w = (int)Math.Ceiling(cardW + 2 * margin);
        var h = (int)Math.Ceiling(cardH + 2 * margin);
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Prepare(bmp);

        var card = new RectangleF(margin, margin, cardW, cardH);
        var radius = 16f * s;
        Shadow(g, card, radius, s);

        using (var path = Round(card, radius))
        {
            // No backdrop blur behind a window: the glass is dense enough to stay readable on any map.
            using (var fill = new LinearGradientBrush(new RectangleF(card.X, card.Y - 1, card.Width, card.Height + 2), Color.FromArgb(218, 18, 18, 22), Color.FromArgb(236, 10, 10, 12), LinearGradientMode.Vertical))
            {
                g.FillPath(fill, path);
            }
            using var edgeBrush = new LinearGradientBrush(new RectangleF(card.X, card.Y - 1, card.Width, card.Height + 2), Color.FromArgb(34, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), LinearGradientMode.Vertical);
            using var edge = new Pen(edgeBrush, Math.Max(1f, s));
            g.DrawPath(edge, path);
        }

        var x = card.X + padX;
        var right = card.Right - padX;
        var y = card.Y + padTop;
        var mid = y + headH / 2;

        // Header: ◇ PLUNDER · COUNT ......... ● STATUS
        Diamond(g, x + 3.5f * s, mid, 5 * s, Gold, filled: false, s);
        var headTop = mid - Ascent(fHead) * 0.62f - 1 * s;
        Tracked(g, "PLUNDER · COUNT", fHead, Muted, x + 13 * s, headTop, 2.1f * s);
        var stW = TrackedWidth(st.Status, fHead, 2.1f * s);
        Tracked(g, st.Status, fHead, st.StatusColor, right - stW, headTop, 2.1f * s);
        Dot(g, right - stW - 9.5f * s, mid, 5 * s, st.StatusColor);
        lockRect = locked ? Rectangle.Empty : Rectangle.Round(new RectangleF(right - stW - 20 * s, y - 8 * s, stW + 28 * s, headH + 16 * s));
        y += headH + 14 * s;

        // ---- Dial: 48 ticks over 270°, lit up to "this match / best match".
        var cx = x + dial / 2;
        var cy = y + dial / 2;
        var R = dial / 2 - 1 * s;
        const int tickCount = 48;
        var lit = (int)Math.Round(tickCount * st.Ticks / 26.0);
        using (var off = new Pen(Color.FromArgb(30, 255, 255, 255), 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var on = new Pen(st.TickColor, 1.7f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            for (var i = 0; i < tickCount; i++)
            {
                var a = (135.0 + 270.0 * i / (tickCount - 1)) * Math.PI / 180.0;
                var len = (i % 12 == 0 || i == tickCount - 1 ? 9f : 6f) * s;
                float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                g.DrawLine(i < lit ? on : off, cx + (R - len) * cos, cy + (R - len) * sin, cx + R * cos, cy + R * sin);
            }
        }
        if (lit > 0)
        {
            // Glowing head at the end of the lit arc.
            var a = (135.0 + 270.0 * (lit - 1) / (tickCount - 1)) * Math.PI / 180.0;
            Dot(g, cx + (R - 13 * s) * (float)Math.Cos(a), cy + (R - 13 * s) * (float)Math.Sin(a), 3.5f * s, st.SecColor);
        }
        var secTrack = -0.03f * 30f * s;
        var secW = TrackedWidth(st.Sec, fSec, secTrack);
        Tracked(g, st.Sec, fSec, st.SecColor, cx - secW / 2, cy + 7 * s - Ascent(fSec), secTrack);
        var slW = TrackedWidth(st.SecLabel, fTiny, 1 * s);
        Tracked(g, st.SecLabel, fTiny, Dim, cx - slW / 2, cy + 13 * s, 1 * s);
        var bestText = "BEST " + v.Best;
        var bW = TrackedWidth(bestText, fTiny, 1 * s);
        Tracked(g, bestText, fTiny, Color.FromArgb(150, Gold), cx - bW / 2, cy + R - 9 * s, 1 * s);

        // ---- Right column: total, then the road to the next milestone.
        var x2 = x + dial + 22 * s;
        var colW = right - x2;
        Tracked(g, "TOTAL CHESTS", fTiny, Dim, x2, y + 1 * s, 1.6f * s);
        var totalPx = 58f * s;
        using (var probe = F(DisplayXL, totalPx))
        {
            var pw = TrackedWidth(v.Total, probe, -0.05f * totalPx);
            if (pw > colW) totalPx *= colW / pw;
        }
        using (var fTotal = F(DisplayXL, totalPx))
        {
            Tracked(g, v.Total, fTotal, st.TotalColor, x2 - 3 * s, y + 62 * s - Ascent(fTotal), -0.05f * totalPx);
        }

        var next = Milestones.Next(v.TotalValue);
        var prev = Milestones.Previous(v.TotalValue);
        var frac = Math.Clamp((v.TotalValue - prev) / (float)Math.Max(1, next - prev), 0f, 1f);
        var my = y + 72 * s;
        Tracked(g, "NEXT ", fTiny, Dim, x2, my, 1.2f * s);
        Tracked(g, next.ToString("N0", L.Culture), fTiny, Ink, x2 + TrackedWidth("NEXT ", fTiny, 1.2f * s) + 1.2f * s, my, 1.2f * s);
        var togo = (next - v.TotalValue).ToString("N0", L.Culture) + " TO GO";
        Tracked(g, togo, fTiny, Gold, right - TrackedWidth(togo, fTiny, 1.2f * s), my, 1.2f * s);
        var ly = y + 91 * s;
        using (var track = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
        {
            g.FillRectangle(track, x2, ly - 0.5f * s, colW, Math.Max(1f, s));
        }
        var fillW = Math.Max(2 * s, colW * frac);
        using (var fillBrush = new LinearGradientBrush(new RectangleF(x2 - 1, ly - 2 * s, fillW + 2, 4 * s), Color.FromArgb(40, Gold), Gold, LinearGradientMode.Horizontal))
        {
            g.FillRectangle(fillBrush, x2, ly - 1 * s, fillW, 2 * s);
        }
        Diamond(g, x2 + fillW, ly, 5 * s, GoldPale, filled: true, s, glow: true);
        y += dial + 16 * s;

        // ---- Latest matches as small bars (best in gold, newest highlighted) + session and treasures.
        var recent = v.Recent ?? Array.Empty<int>();
        var bw = 5f * s;
        var bgap = 4f * s;
        if (recent.Count > 0)
        {
            var max = Math.Max(1, recent.Max());
            var bestIdx = recent.ToList().LastIndexOf(recent.Max());
            for (var i = 0; i < recent.Count; i++)
            {
                var bh = Math.Max(2 * s, histH * recent[i] / max);
                var color = i == recent.Count - 1 ? st.SecColor == Gold ? Ink : st.SecColor
                    : i == bestIdx ? Gold
                    : Color.FromArgb(56, 255, 255, 255);
                using var p = Round(new RectangleF(x + i * (bw + bgap), y + histH - bh, bw, bh), 2 * s);
                using var b = new SolidBrush(color);
                g.FillPath(b, p);
            }
        }
        var ft = 0.08f * 10f * s;
        var fy = y + histH - fFoot.GetHeight() + 1 * s;
        var trW = TrackedWidth(v.Treasures, fFootValue, ft);
        Tracked(g, v.Treasures, fFootValue, VioletPale, right - trW, fy, ft);
        Sparkle(g, right - trW - 9 * s, fy + fFoot.GetHeight() * 0.52f, 4.5f * s, VioletPale);
        var sesW = TrackedWidth(v.Session, fFootValue, ft);
        var sx = right - trW - 9 * s - 18 * s - sesW;
        Tracked(g, v.Session, fFootValue, Ink, sx, fy, ft);
        var sl = "SESSION ";
        Tracked(g, sl, fFoot, Muted, sx - TrackedWidth(sl, fFoot, ft) - ft, fy, ft);
        return bmp;
    }

    // ---- 2a · Compact (figures only, text shadow, no card) ------------------------------------------

    public static Bitmap Minimal(OverlayView v, bool locked, float s, out Rectangle lockRect)
    {
        var st = Resolve(v, locked);
        var padX = 18f * s;
        var padY = 16f * s;

        using var fStatus = F(Mono, 9.5f * s);
        using var fTotal = F(DisplayL, 58f * s);
        using var fLabel = F(Mono, 9.5f * s);
        using var fValue = F(DisplayL, 18f * s);

        var rows = new (string Label, string Value, Color Color, bool Star)[]
        {
            (st.SecLabel, st.Sec, st.SecColor, false),
            ("SESSION", v.Session, InkBright, false),
            ("TREASURES", v.Treasures, VioletPale, true),
        };
        var totalTrack = -0.04f * 58f * s;
        var labelTrack = 0.2f * 9.5f * s;
        var labelColW = rows.Max(r => TrackedWidth(r.Label, fLabel, labelTrack));
        var valueColW = rows.Max(r => Width(r.Value, fValue) + (r.Star ? 14 * s : 0));
        var gridW = labelColW + 18 * s + valueColW;
        var totalW = TrackedWidth(v.Total, fTotal, totalTrack);
        var statusW = 13 * s + TrackedWidth(st.Status, fStatus, 2.1f * s);
        var contentW = Math.Max(Math.Max(gridW, totalW), Math.Max(statusW, 120 * s));

        var statusH = fStatus.GetHeight();
        var totalH = 58f * s * 0.95f;
        var rowH = 18f * s;
        var gridH = rows.Length * rowH + (rows.Length - 1) * 4 * s;
        var w = (int)Math.Ceiling(contentW + 2 * padX + 8 * s);
        var h = (int)Math.Ceiling(padY + statusH + 4 * s + totalH + 8 * s + 1 + 10 * s + gridH + padY);

        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Prepare(bmp);
        lockRect = Rectangle.Empty;

        if (!locked)
        {
            // Unlocked: dark backdrop with a dashed gold border, to grab and move it.
            var back = new RectangleF(1, 1, w - 2, h - 2);
            using var p = Round(back, 12 * s);
            using var fill = new SolidBrush(Color.FromArgb(179, 10, 10, 12));
            using var edge = new Pen(Color.FromArgb(115, Gold), Math.Max(1f, s)) { DashPattern = new[] { 3f, 3f } };
            g.FillPath(fill, p);
            g.DrawPath(edge, p);
            lockRect = new Rectangle(0, 0, w, (int)(padY + statusH + 6 * s)); // the status line locks it again
        }

        var x = padX;
        var y = padY;

        // ◆ STATUS
        Diamond(g, x + 3.5f * s, y + statusH / 2, 5 * s, st.StatusColor, filled: true, s);
        Shadowed(g, st.Status, fStatus, st.StatusColor, x + 13 * s, y, 2.1f * s, s * 0.5f);
        y += statusH + 4 * s;

        // Total.
        var ascent = Ascent(fTotal);
        Shadowed(g, v.Total, fTotal, st.TotalColor, x - 2 * s, y + totalH * 0.88f - ascent, totalTrack, s);
        y += totalH + 8 * s;

        // Road to the next milestone: dim track, gold up to where you are, diamond at the head.
        var next = Milestones.Next(v.TotalValue);
        var prev = Milestones.Previous(v.TotalValue);
        var frac = Math.Clamp((v.TotalValue - prev) / (float)Math.Max(1, next - prev), 0f, 1f);
        var ruleW = 120 * s;
        using (var shade = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            g.FillRectangle(shade, x, y + 1 * s, ruleW, Math.Max(1f, s));
        }
        using (var track = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
        {
            g.FillRectangle(track, x, y, ruleW, Math.Max(1f, s));
        }
        var fillW = Math.Max(2 * s, ruleW * frac);
        using (var rule = new SolidBrush(Gold))
        {
            g.FillRectangle(rule, x, y - 0.5f * s, fillW, 2 * s);
        }
        Diamond(g, x + fillW, y + 0.5f * s, 5 * s, GoldPale, filled: true, s, glow: true);
        y += 1 + 10 * s;

        // Label / value grid.
        foreach (var r in rows)
        {
            var baseline = y + rowH * 0.82f;
            Shadowed(g, r.Label, fLabel, Color.FromArgb(191, 255, 255, 255), x, baseline - Ascent(fLabel), labelTrack, s * 0.5f);
            var vx = x + labelColW + 18 * s;
            if (r.Star)
            {
                Sparkle(g, vx + 5 * s, baseline - Ascent(fValue) * 0.42f, 5 * s, r.Color);
                vx += 14 * s;
            }
            Shadowed(g, r.Value, fValue, r.Color, vx, baseline - Ascent(fValue), 0, s * 0.6f);
            y += rowH + 4 * s;
        }
        return bmp;
    }
}
