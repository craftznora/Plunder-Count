using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.Versioning;

namespace PlunderCount;

/// <summary>
/// Draws the overlay into a 32-bit bitmap with real per-pixel transparency (GDI+): anti-aliased glass card,
/// soft shadow, smooth corners. NativeOverlay puts the bitmap on screen with UpdateLayeredWindow.
/// </summary>
[SupportedOSPlatform("windows")]
public static class OverlayRenderer
{
    // ---- Palette ------------------------------------------------------------------------------
    private static readonly Color Ink = Color.FromArgb(242, 243, 245);
    private static readonly Color Muted = Color.FromArgb(138, 143, 153);
    private static readonly Color Dim = Color.FromArgb(94, 99, 109);
    private static readonly Color Sand = Color.FromArgb(214, 183, 124);
    private static readonly Color Green = Color.FromArgb(140, 197, 160);
    private static readonly Color Violet = Color.FromArgb(182, 162, 217);

    private static readonly Dictionary<string, FontFamily> Families = new();

    /// <summary>First installed family of the list (Windows 10 has Bahnschrift and Segoe UI; the last resort always exists).</summary>
    private static FontFamily Family(params string[] names)
    {
        var key = string.Join("|", names);
        if (Families.TryGetValue(key, out var cached))
        {
            return cached;
        }
        foreach (var n in names)
        {
            try
            {
                var f = new FontFamily(n);
                Families[key] = f;
                return f;
            }
            catch (ArgumentException)
            {
                // not installed
            }
        }
        Families[key] = FontFamily.GenericSansSerif;
        return FontFamily.GenericSansSerif;
    }

    private static FontFamily Numbers => Family("Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold", "Segoe UI");
    private static FontFamily NumbersLight => Family("Bahnschrift", "Segoe UI");
    private static FontFamily UiSemi => Family("Segoe UI Semibold", "Segoe UI");
    private static FontFamily Ui => Family("Segoe UI");

    private static Font F(FontFamily fam, float px, FontStyle style = FontStyle.Regular)
    {
        var st = fam.IsStyleAvailable(style) ? style : FontStyle.Regular;
        return new Font(fam, px, st, GraphicsUnit.Pixel);
    }

    private static readonly StringFormat Typo = CreateTypo();

    private static StringFormat CreateTypo()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        return f;
    }

    private static Graphics Measurer { get; } = Graphics.FromImage(new Bitmap(1, 1));

    private static float Width(string text, Font font) => text.Length == 0 ? 0 : Measurer.MeasureString(text, font, PointF.Empty, Typo).Width;

    /// <summary>Width of small caps text drawn with letter spacing.</summary>
    private static float TrackedWidth(string text, Font font, float tracking)
    {
        float w = 0;
        foreach (var c in text)
        {
            w += Width(c.ToString(), font) + tracking;
        }
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
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // grayscale: ClearType needs an opaque background
        g.Clear(Color.Transparent);
        return g;
    }

    /// <summary>Soft drop shadow made of stacked, increasingly faint rounded rectangles.</summary>
    private static void Shadow(Graphics g, RectangleF card, float radius, float spread, float strength = 1f)
    {
        var steps = Math.Max(4, (int)spread);
        for (var i = steps; i >= 1; i--)
        {
            var t = (float)i / steps;
            var grow = spread * t;
            var alpha = (int)Math.Round(strength * 34 * Math.Pow(1 - t, 2.2));
            if (alpha <= 0) continue;
            var r = new RectangleF(card.X - grow, card.Y - grow + spread * 0.35f, card.Width + 2 * grow, card.Height + 2 * grow);
            using var path = Round(r, radius + grow);
            using var b = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            g.FillPath(b, path);
        }
    }

    /// <summary>Padlock icon, open or closed.</summary>
    private static void Padlock(Graphics g, RectangleF box, bool open, Color color)
    {
        var s = box.Height;
        var bw = s * 0.62f;
        var bh = s * 0.46f;
        var bx = box.X + (box.Width - bw) / 2;
        var by = box.Bottom - bh;
        using var pen = new Pen(color, Math.Max(1.2f, s * 0.11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var body = new SolidBrush(color);
        using (var p = Round(new RectangleF(bx, by, bw, bh), s * 0.1f))
        {
            g.FillPath(body, p);
        }
        var sw = bw * 0.62f;
        var sx = bx + (bw - sw) / 2 + (open ? bw * 0.32f : 0);
        var top = box.Y + s * 0.04f;
        g.DrawArc(pen, sx, top, sw, sw, 180, 180);
        g.DrawLine(pen, sx + sw, top + sw / 2, sx + sw, by);
        if (!open)
        {
            g.DrawLine(pen, sx, top + sw / 2, sx, by);
        }
        else
        {
            g.DrawLine(pen, sx, top + sw / 2, sx, top + sw / 2 + s * 0.08f);
        }
    }

    private static void Pill(Graphics g, string text, Font font, Color color, float right, float centerY, float scale, out float left)
    {
        var tw = TrackedWidth(text, font, 1.2f * scale);
        var dot = 6 * scale;
        var padX = 9 * scale;
        var h = 20 * scale;
        var w = padX + dot + 6 * scale + tw + padX;
        var r = new RectangleF(right - w, centerY - h / 2, w, h);
        using (var p = Round(r, h / 2))
        using (var fill = new SolidBrush(Color.FromArgb(30, color)))
        using (var edge = new Pen(Color.FromArgb(60, color), 1f))
        {
            g.FillPath(fill, p);
            g.DrawPath(edge, p);
        }
        using (var glow = new SolidBrush(Color.FromArgb(60, color)))
        {
            g.FillEllipse(glow, r.X + padX - 2 * scale, centerY - dot / 2 - 2 * scale, dot + 4 * scale, dot + 4 * scale);
        }
        using (var d = new SolidBrush(color))
        {
            g.FillEllipse(d, r.X + padX, centerY - dot / 2, dot, dot);
        }
        var th = font.GetHeight();
        Tracked(g, text, font, color, r.X + padX + dot + 6 * scale, centerY - th / 2 + 0.5f * scale, 1.2f * scale);
        left = r.X;
    }

    // ---- Glass panel (default) -------------------------------------------------------------------
    //
    //   ┌──────────────────────────────────────────────┐
    //   │ ▪ PLUNDER COUNT                     ● LIVE 🔓 │
    //   │                                              │
    //   │ 3,934            │ 14                        │
    //   │ TOTAL CHESTS     │ CURRENT MATCH             │
    //   │                                              │
    //   │ [SESSION 83]  [AVG 13]  [TREASURES ★ 36]     │
    //   └──────────────────────────────────────────────┘

    /// <param name="lockRect">Where the padlock is (window coordinates), empty when it is not drawn.</param>
    public static Bitmap Glass(OverlayView v, bool locked, float s, out Rectangle lockRect)
    {
        var margin = 18f * s;      // room for the shadow
        var pad = 18f * s;

        using var fHeader = F(UiSemi, 10.5f * s);
        using var fBig = F(Numbers, 50f * s);
        using var fMid = F(Numbers, 32f * s);
        using var fCap = F(UiSemi, 9.5f * s);
        using var fChipLabel = F(UiSemi, 9f * s);
        using var fChipValue = F(Numbers, 17f * s);
        using var fPill = F(UiSemi, 9.5f * s);

        const string title = "PLUNDER COUNT";
        var current = v.Current ?? "—";
        var totalW = Width(v.Total, fBig);
        var curW = Width(current, fMid);
        var capRight = v.CurrentCaption.ToUpperInvariant();
        var leftColW = Math.Max(totalW, TrackedWidth("TOTAL CHESTS", fCap, 1.6f * s));
        var rightColW = Math.Max(curW, TrackedWidth(capRight, fCap, 1.6f * s));
        var gap = 44f * s;

        var chips = new List<(string Label, string Value, Color Color)>
        {
            ("SESSION", v.Session, Ink),
            ("LAST", v.Last ?? "—", Ink),
            ("TREASURES", "★ " + v.Treasures, Violet),
        };
        var chipGap = 8f * s;
        var chipW = chips.Max(c => Math.Max(TrackedWidth(c.Label, fChipLabel, 1.3f * s), Width(c.Value, fChipValue))) + 24f * s;
        var chipsW = chips.Count * chipW + (chips.Count - 1) * chipGap;

        var innerW = Math.Max(Math.Max(leftColW + gap + rightColW, chipsW), 270f * s);
        // Equal chips that fill the card.
        chipW = (innerW - (chips.Count - 1) * chipGap) / chips.Count;

        var headerH = 20f * s;
        var bigH = fBig.GetHeight();
        var capH = fCap.GetHeight();
        var chipH = 48f * s;
        var cardW = innerW + 2 * pad;
        var cardH = pad + headerH + 14f * s + bigH * 0.86f + 6f * s + capH + 18f * s + chipH + pad;

        var w = (int)Math.Ceiling(cardW + 2 * margin);
        var h = (int)Math.Ceiling(cardH + 2 * margin);
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Prepare(bmp);

        var card = new RectangleF(margin, margin, cardW, cardH);
        var radius = 16f * s;
        Shadow(g, card, radius, 16f * s);

        using (var path = Round(card, radius))
        {
            using (var fill = new LinearGradientBrush(card, Color.FromArgb(242, 26, 28, 33), Color.FromArgb(248, 12, 13, 16), LinearGradientMode.Vertical))
            {
                g.FillPath(fill, path);
            }
            // Sheen: light falling on the top of the glass.
            var sheenRect = new RectangleF(card.X, card.Y, card.Width, card.Height * 0.45f);
            using (var sheen = new LinearGradientBrush(new RectangleF(sheenRect.X, sheenRect.Y - 1, sheenRect.Width, sheenRect.Height + 2), Color.FromArgb(13, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
            {
                var clip = g.Clip;
                g.SetClip(path);
                g.FillRectangle(sheen, sheenRect);
                g.Clip = clip;
            }
            // Edge: brighter on top, like a bevel.
            using var edgeBrush = new LinearGradientBrush(new RectangleF(card.X, card.Y - 1, card.Width, card.Height + 2), Color.FromArgb(58, 255, 255, 255), Color.FromArgb(14, 255, 255, 255), LinearGradientMode.Vertical);
            using var edge = new Pen(edgeBrush, Math.Max(1f, s));
            g.DrawPath(edge, path);
        }

        var x = card.X + pad;
        var right = card.Right - pad;
        var y = card.Y + pad;

        // Header: accent mark, spaced title, status pill, padlock.
        var mid = y + headerH / 2;
        using (var mark = Round(new RectangleF(x, mid - 5 * s, 10 * s, 10 * s), 3 * s))
        using (var mb = new LinearGradientBrush(new RectangleF(x, mid - 5 * s, 10 * s, 10 * s), Color.FromArgb(232, 204, 150), Color.FromArgb(178, 146, 92), LinearGradientMode.ForwardDiagonal))
        {
            g.FillPath(mb, mark);
        }
        Tracked(g, title, fHeader, Muted, x + 18 * s, mid - fHeader.GetHeight() / 2, 2.2f * s);

        lockRect = Rectangle.Empty;
        var pillRight = right;
        if (!locked)
        {
            var lb = new RectangleF(right - 12 * s, mid - 7 * s, 12 * s, 14 * s);
            Padlock(g, lb, true, Color.FromArgb(200, Ink));
            lockRect = Rectangle.Round(RectangleF.Inflate(lb, 8 * s, 6 * s));
            pillRight = lb.X - 10 * s;
        }
        if (v.Badge != null)
        {
            var liveCount = v.Badge.Contains("LIVE", StringComparison.OrdinalIgnoreCase);
            Pill(g, liveCount ? "LIVE" : "IN MATCH", fPill, liveCount ? Green : Sand, pillRight, mid, s, out _);
        }
        y += headerH + 14f * s;

        // Figures: total | current or last match.
        using (var white = new SolidBrush(Ink))
        {
            g.DrawString(v.Total, fBig, white, x - 2 * s, y - bigH * 0.08f, Typo);
        }
        var x2 = x + leftColW + gap;
        var curColor = v.Current == null ? Dim : v.CurrentLive ? Green : Color.FromArgb(206, 209, 215);
        using (var cb = new SolidBrush(curColor))
        {
            var midH = fMid.GetHeight();
            g.DrawString(current, fMid, cb, x2 - 1 * s, y + bigH * 0.86f - midH * 0.9f, Typo);
        }
        using (var sep = new LinearGradientBrush(new RectangleF(0, y, 1, bigH + capH), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(34, 255, 255, 255), LinearGradientMode.Vertical))
        {
            g.FillRectangle(sep, x + leftColW + gap / 2, y + bigH * 0.1f, Math.Max(1f, s), bigH * 0.76f + capH);
        }
        y += bigH * 0.86f + 6f * s;
        Tracked(g, "TOTAL CHESTS", fCap, Dim, x, y, 1.6f * s);
        Tracked(g, capRight, fCap, Dim, x2, y, 1.6f * s);
        y += capH + 18f * s;

        // Chips.
        for (var i = 0; i < chips.Count; i++)
        {
            var (label, value, color) = chips[i];
            var r = new RectangleF(x + i * (chipW + chipGap), y, chipW, chipH);
            using (var p = Round(r, 11 * s))
            using (var fill = new SolidBrush(Color.FromArgb(12, 255, 255, 255)))
            using (var edge = new Pen(Color.FromArgb(18, 255, 255, 255), 1f))
            {
                g.FillPath(fill, p);
                g.DrawPath(edge, p);
            }
            Tracked(g, label, fChipLabel, Muted, r.X + 12 * s, r.Y + 9 * s, 1.3f * s);
            using var vb = new SolidBrush(color);
            g.DrawString(value, fChipValue, vb, r.X + 11 * s, r.Y + 21 * s, Typo);
        }

        return bmp;
    }

    // ---- Minimal: figures only, with a soft shadow, right-aligned (under the minimap) -------------

    public static Bitmap Minimal(OverlayView v, bool locked, float s, out Rectangle lockRect)
    {
        var margin = 10f * s;
        using var fBig = F(Numbers, 46f * s);
        using var fRow = F(Numbers, 18f * s);
        using var fLabel = F(UiSemi, 10f * s);

        var liveValue = v.CurrentLive && v.Current is { } c && c != "…";
        var rows = new List<(string Label, string Value, Color Color, bool Dot)>();
        if (liveValue)
        {
            rows.Add(("LIVE", v.Current!, Green, true));
        }
        else if (v.CurrentLive)
        {
            rows.Add(("IN MATCH", "…", Sand, true));
        }
        else
        {
            rows.Add(("LAST", v.Last ?? "—", Ink, false));
        }
        rows.Add(("SESSION", v.Session, Ink, false));
        rows.Add(("TREASURES", "★ " + v.Treasures, Violet, false));

        var bigW = Width(v.Total, fBig);
        var rowW = rows.Max(r => TrackedWidth(r.Label, fLabel, 1.5f * s) + 10 * s + Width(r.Value, fRow) + (r.Dot ? 12 * s : 0));
        var lockW = locked ? 0 : 26f * s;
        var contentW = Math.Max(bigW + 14 * s, rowW);
        var bigH = fBig.GetHeight();
        var rowH = fRow.GetHeight() + 3 * s;
        var w = (int)Math.Ceiling(lockW + contentW + 2 * margin + 8 * s);
        var h = (int)Math.Ceiling(bigH * 0.92f + rows.Count * rowH + 2 * margin + 4 * s);

        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Prepare(bmp);
        lockRect = Rectangle.Empty;

        if (!locked)
        {
            // A backdrop to grab it while unlocked; once locked only the figures remain and the mouse goes through.
            var back = new RectangleF(1, 1, w - 2, h - 2);
            using var p = Round(back, 12 * s);
            using var fill = new SolidBrush(Color.FromArgb(200, 14, 15, 19));
            using var edge = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
            g.FillPath(fill, p);
            g.DrawPath(edge, p);
            var lb = new RectangleF(margin + 2 * s, h / 2f - 7 * s, 12 * s, 14 * s);
            Padlock(g, lb, true, Color.FromArgb(200, Ink));
            lockRect = new Rectangle(0, 0, (int)(lockW + margin), h);
        }

        var right = w - margin - 4 * s;
        var y = margin;

        // Total with a slim accent bar on its left.
        var totalColor = liveValue ? Sand : Ink;
        GlowText(g, v.Total, fBig, totalColor, right - bigW, y - bigH * 0.06f, s);
        using (var bar = Round(new RectangleF(right - bigW - 12 * s, y + bigH * 0.2f, 3.5f * s, bigH * 0.56f), 1.75f * s))
        using (var bb = new SolidBrush(Sand))
        {
            g.FillPath(bb, bar);
        }
        y += bigH * 0.92f + 4 * s;

        foreach (var (label, value, color, dot) in rows)
        {
            var vw = Width(value, fRow);
            GlowText(g, value, fRow, color, right - vw, y, s);
            var lw = TrackedWidth(label, fLabel, 1.5f * s);
            var lx = right - vw - 10 * s - lw;
            var ly = y + (fRow.GetHeight() - fLabel.GetHeight()) / 2 + 1 * s;
            // Label shadow: a 1 px offset is enough at this size.
            Tracked(g, label, fLabel, Color.FromArgb(150, 0, 0, 0), lx + 1 * s, ly + 1 * s, 1.5f * s);
            Tracked(g, label, fLabel, Color.FromArgb(220, 200, 204, 212), lx, ly, 1.5f * s);
            if (dot)
            {
                var d = 7 * s;
                using var db = new SolidBrush(color);
                using var halo = new SolidBrush(Color.FromArgb(70, color));
                var dx = lx - 12 * s;
                var dy = y + fRow.GetHeight() / 2 - d / 2;
                g.FillEllipse(halo, dx - 2 * s, dy - 2 * s, d + 4 * s, d + 4 * s);
                g.FillEllipse(db, dx, dy, d, d);
            }
            y += rowH;
        }
        return bmp;
    }

    /// <summary>Text with a soft dark halo, readable on any part of the game without a hard outline.</summary>
    private static void GlowText(Graphics g, string text, Font font, Color color, float x, float y, float s)
    {
        using var path = new GraphicsPath();
        path.AddString(text, font.FontFamily, (int)font.Style, font.Size, new PointF(x, y), Typo);
        for (var i = 5; i >= 1; i--)
        {
            using var pen = new Pen(Color.FromArgb(22 + (5 - i) * 10, 0, 0, 0), i * 1.6f * s) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
        }
        using var b = new SolidBrush(color);
        g.FillPath(b, path);
    }
}
