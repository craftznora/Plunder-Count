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
            using var halo = new SolidBrush(Color.FromArgb(50, color));
            g.FillEllipse(halo, cx - h * 2.2f, cy - h * 2.2f, h * 4.4f, h * 4.4f);
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
        for (var i = 4; i >= 1; i--)
        {
            var r = d / 2 + i * d * 0.45f;
            using var halo = new SolidBrush(Color.FromArgb(18, color));
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

    // ---- 2a · Glass card (default) ------------------------------------------------------------------
    //
    //   ◇ PLUNDER · COUNT                         ● LIVE
    //   642 chests                                  12
    //                                       THIS MATCH
    //   ▬▬▬▬▬▬▬▬▬▬▬▬ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─   (ticks: this match vs best match)
    //   SESSION 12        LAST 19        TREASURES ✦ 22

    /// <param name="lockRect">Click target that locks the overlay (window coordinates); empty when locked.</param>
    public static Bitmap Glass(OverlayView v, bool locked, float s, out Rectangle lockRect)
    {
        var st = Resolve(v, locked);
        var margin = 30f * s;          // room for the shadow
        var padX = 20f * s;
        var padTop = 16f * s;
        var padBottom = 14f * s;
        var cardW = 340f * s;

        using var fHead = F(Mono, 9.5f * s);
        using var fTotal = F(DisplayXL, 72f * s);
        using var fUnit = F(DisplayL, 13f * s);
        using var fSec = F(DisplayXL, 30f * s);
        using var fSecLabel = F(Mono, 9f * s);
        using var fFoot = F(Mono, 10.5f * s);
        using var fFootValue = F(MonoM, 10.5f * s);

        var headH = 12f * s;
        var totalAscent = Ascent(fTotal);
        var mainH = 72f * s * 0.9f;   // line-height .9
        var ticksH = 3f * s;
        var footH = fFoot.GetHeight();
        var cardH = padTop + headH + 10 * s + mainH + 12 * s + ticksH + 12 * s + footH + padBottom;

        var w = (int)Math.Ceiling(cardW + 2 * margin);
        var h = (int)Math.Ceiling(cardH + 2 * margin);
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Prepare(bmp);

        var card = new RectangleF(margin, margin, cardW, cardH);
        var radius = 14f * s;
        Shadow(g, card, radius, s);

        using (var path = Round(card, radius))
        {
            // No backdrop blur behind a window: the glass is denser than in the mockup so it stays readable on any map.
            using (var fill = new LinearGradientBrush(new RectangleF(card.X, card.Y - 1, card.Width, card.Height + 2), Color.FromArgb(214, 18, 18, 22), Color.FromArgb(232, 10, 10, 12), LinearGradientMode.Vertical))
            {
                g.FillPath(fill, path);
            }
            using var edgeBrush = new LinearGradientBrush(new RectangleF(card.X, card.Y - 1, card.Width, card.Height + 2), Color.FromArgb(31, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), LinearGradientMode.Vertical);
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
        y += headH + 10 * s;

        // Total + "chests" on the same baseline, current / last match on the right.
        var baseline = y + mainH * 0.86f;
        var totalTrack = -0.05f * 72f * s;
        Tracked(g, v.Total, fTotal, Ink, x - 3 * s, baseline - totalAscent, totalTrack);
        var totalW = TrackedWidth(v.Total, fTotal, totalTrack);
        Tracked(g, "chests", fUnit, Muted, x - 3 * s + totalW + 8 * s, baseline - Ascent(fUnit), 0);

        var secTrack = -0.03f * 30f * s;
        var secW = TrackedWidth(st.Sec, fSec, secTrack);
        var labelW = TrackedWidth(st.SecLabel, fSecLabel, 1.8f * s);
        var labelTop = baseline - 6 * s - fSecLabel.GetHeight() + 4 * s;
        Tracked(g, st.SecLabel, fSecLabel, Dim, right - labelW, labelTop, 1.8f * s);
        var secBaseline = labelTop - 5 * s;
        Tracked(g, st.Sec, fSec, st.SecColor, right - secW, secBaseline - Ascent(fSec) * 0.92f, secTrack);
        y += mainH + 12 * s;

        // 26 ticks: this match (or the last one) against the best match.
        var gap = 3f * s;
        var tw = (cardW - 2 * padX - 25 * gap) / 26;
        using (var on = new SolidBrush(st.TickColor))
        using (var off = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
        {
            for (var i = 0; i < 26; i++)
            {
                using var p = Round(new RectangleF(x + i * (tw + gap), y, tw, ticksH), 1 * s);
                g.FillPath(i < st.Ticks ? on : off, p);
            }
        }
        y += ticksH + 12 * s;

        // SESSION 12 · LAST 19 · TREASURES ✦ 22, spread across the card.
        var ft = 0.08f * 10.5f * s;
        var items = new (string Label, string Value, Color Color, bool Star)[]
        {
            ("SESSION", v.Session, Ink, false),
            ("LAST", v.Last ?? "—", Ink, false),
            ("TREASURES", v.Treasures, VioletPale, true),
        };
        var widths = items.Select(it => TrackedWidth(it.Label + " ", fFoot, ft) + (it.Star ? 13 * s : 0) + TrackedWidth(it.Value, fFootValue, ft)).ToArray();
        var free = (cardW - 2 * padX - widths.Sum()) / (items.Length - 1);
        var fx = x;
        for (var i = 0; i < items.Length; i++)
        {
            var it = items[i];
            Tracked(g, it.Label + " ", fFoot, Muted, fx, y, ft);
            var vx = fx + TrackedWidth(it.Label + " ", fFoot, ft);
            if (it.Star)
            {
                Sparkle(g, vx + 4.5f * s, y + fFoot.GetHeight() * 0.52f, 4.5f * s, it.Color);
                vx += 13 * s;
            }
            Tracked(g, it.Value, fFootValue, it.Color, vx, y, ft);
            fx += widths[i] + free;
        }
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

        // Gold rule fading out.
        using (var rule = new LinearGradientBrush(new RectangleF(x - 1, y, 121 * s, 1), Gold, Color.FromArgb(0, Gold), LinearGradientMode.Horizontal))
        {
            g.FillRectangle(rule, x, y, 120 * s, Math.Max(1f, s));
        }
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
