using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenWigi.Sdk;

namespace OpenWigiDash.SystemPlugin.Rendering;

/// <summary>O que o tile do Open Application mostra.</summary>
/// <param name="Icon">Ícone do app, ou <c>null</c> para o desenho padrão (grade de pontos).</param>
/// <param name="Title">Texto no rodapé, com contorno; vazio = sem texto.</param>
/// <param name="Running">Desenha o indicador de app aberto no canto inferior direito.</param>
/// <param name="Dimmed">App não encontrado (desinstalado): ícone apagado.</param>
/// <param name="Detail">Aviso pequeno no lugar do título (ex.: "Escolha um app").</param>
public sealed record TileVisual(Bitmap? Icon, string? Title, bool Running, bool Dimmed = false, string? Detail = null);

/// <summary>
/// Desenha o tile na área de conteúdo (o host pinta a moldura e o canto). Visual do Stream Deck: fundo preto, ícone
/// grande, título com contorno embaixo e uma bolinha verde com anel escuro quando o app está aberto.
/// </summary>
public static class TileRenderer
{
    public static readonly Color Background = Color.Black;
    public static readonly Color RunningColor = Color.FromArgb(35, 165, 90);
    public static readonly Color RingColor = Color.FromArgb(20, 20, 20);

    public static WidgetFrame Render(PixelSize size, WidgetTheme theme, TileVisual visual)
    {
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Background);

            var side = Math.Min(size.Width, size.Height);
            var iconSide = side * 0.78f;
            var iconRect = new RectangleF((size.Width - iconSide) / 2f, (size.Height - iconSide) / 2f, iconSide, iconSide);

            if (visual.Icon is { } icon)
                DrawIcon(g, icon, iconRect, visual.Dimmed);
            else
                DrawDotGrid(g, iconRect, visual.Dimmed);

            var text = !string.IsNullOrWhiteSpace(visual.Detail) ? visual.Detail : visual.Title;
            if (!string.IsNullOrWhiteSpace(text))
                DrawTitle(g, size, theme, text!, small: !string.IsNullOrWhiteSpace(visual.Detail));

            if (visual.Running)
                DrawRunningDot(g, size);
        }
        return ToFrame(bitmap);
    }

    private static void DrawIcon(Graphics g, Bitmap icon, RectangleF area, bool dimmed)
    {
        // Mantém a proporção (alguns ícones da Store não são quadrados).
        float scale;
        Rectangle source;
        lock (icon)
        {
            source = new Rectangle(0, 0, icon.Width, icon.Height);
            scale = Math.Min(area.Width / icon.Width, area.Height / icon.Height);
            var w = icon.Width * scale;
            var h = icon.Height * scale;
            var target = new RectangleF(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
            if (!dimmed)
            {
                g.DrawImage(icon, target, source, GraphicsUnit.Pixel);
                return;
            }
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.35f });
            g.DrawImage(icon, Rectangle.Round(target), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
        }
    }

    /// <summary>A grade 3x3 de anéis que o Stream Deck usa quando não há app escolhido.</summary>
    private static void DrawDotGrid(Graphics g, RectangleF area, bool dimmed)
    {
        var cell = area.Width * 0.46f / 3f;
        var gap = cell * 0.35f;
        var total = cell * 3 + gap * 2;
        var x0 = area.X + (area.Width - total) / 2;
        var y0 = area.Y + (area.Height - total) / 2;
        using var pen = new Pen(Color.FromArgb(dimmed ? 110 : 255, 255, 255, 255), Math.Max(1.5f, cell * 0.22f));
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
                g.DrawEllipse(pen, x0 + col * (cell + gap), y0 + row * (cell + gap), cell, cell);
        }
    }

    private static void DrawTitle(Graphics g, PixelSize size, WidgetTheme theme, string text, bool small)
    {
        var height = Math.Max(11f, size.Height * (small ? 0.13f : 0.16f));
        using var family = ResolveFamily(theme.SecondaryFontFamily);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Far,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        var bounds = new RectangleF(2, 0, size.Width - 4, size.Height - Math.Max(2, size.Height * 0.04f));

        // Diminui até caber na largura, como o título do Stream Deck.
        var emSize = height;
        using (var probe = new Font(family, emSize, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            var measured = g.MeasureString(text, probe).Width;
            if (measured > bounds.Width && measured > 0)
                emSize = Math.Max(9f, emSize * bounds.Width / measured);
        }

        using var path = new GraphicsPath();
        path.AddString(text, family, (int)FontStyle.Bold, emSize, bounds, format);
        using var outline = new Pen(Color.Black, Math.Max(2f, emSize * 0.22f)) { LineJoin = LineJoin.Round };
        g.DrawPath(outline, path);
        using var fill = new SolidBrush(ToColor(theme.PrimaryForeground));
        g.FillPath(fill, path);
    }

    private static void DrawRunningDot(Graphics g, PixelSize size)
    {
        var side = Math.Min(size.Width, size.Height);
        var d = Math.Max(10f, side * 0.24f);
        var margin = Math.Max(3f, side * 0.06f);
        var rect = new RectangleF(size.Width - d - margin, size.Height - d - margin, d, d);
        using var ring = new SolidBrush(RingColor);
        g.FillEllipse(ring, rect);
        var inset = d * 0.2f;
        using var dot = new SolidBrush(RunningColor);
        g.FillEllipse(dot, RectangleF.Inflate(rect, -inset, -inset));
    }

    private static FontFamily ResolveFamily(string name)
    {
        try
        {
            return new FontFamily(name);
        }
        catch (ArgumentException)
        {
            return new FontFamily(GenericFontFamilies.SansSerif);
        }
    }

    private static Color ToColor(WidgetColor c) => Color.FromArgb(c.R, c.G, c.B);

    /// <summary>Bitmap para quadro Bgra32. Igual ao dos plugins Discord e Stream Deck Mirror (candidato ao SDK).</summary>
    internal static WidgetFrame ToFrame(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new WidgetFrame(new PixelSize(bitmap.Width, bitmap.Height), FramePixelFormat.Bgra32, pixels, data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
