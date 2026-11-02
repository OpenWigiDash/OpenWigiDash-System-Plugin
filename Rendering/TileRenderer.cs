using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenWigi.Sdk;

namespace OpenWigiDash.SystemPlugin.Rendering;

/// <summary>O que o tile do Open Application mostra.</summary>
/// <param name="Icon">Ícone do app, ou <c>null</c> para o desenho padrão (grade de pontos).</param>
/// <param name="Title">Texto no rodapé, com contorno; vazio = sem texto.</param>
/// <param name="Running">Desenha o indicador de app aberto no canto inferior direito.</param>
/// <param name="Dimmed">App não encontrado (desinstalado): ícone apagado.</param>
/// <param name="Detail">Aviso pequeno no lugar do título (ex.: "Escolha um app").</param>
public sealed record TileVisual(Bitmap? Icon, string? Title, bool Running, bool Dimmed = false, string? Detail = null)
{
    public IconFit Fit { get; init; } = IconFit.Fill;

    /// <summary>Caractere da fonte de ícones do Windows, desenhado quando não há <see cref="Icon"/>.</summary>
    public string? Glyph { get; init; }

    /// <summary>Bolinhas de posição no topo (carrossel); 0 = nenhuma.</summary>
    public int DotCount { get; init; }

    /// <summary>Bolinha preenchida; -1 = nenhuma (o padrão atual não está na lista).</summary>
    public int DotIndex { get; init; } = -1;

    /// <summary>Cor de fundo escolhida; <c>null</c> = preto (ou a cor da placa do ícone, no Open Application).</summary>
    public Color? Background { get; init; }

    /// <summary>Selo no canto inferior esquerdo (ex.: alto-falante azul = saída, microfone laranja = entrada).</summary>
    public TileBadge? Badge { get; init; }
}

/// <summary>Quadradinho colorido com um glifo da fonte de ícones do Windows.</summary>
public sealed record TileBadge(string Glyph, Color Color);

/// <summary>Quanto do tile o ícone do app ocupa.</summary>
public enum IconFit
{
    /// <summary>Com margem em volta (78% do lado menor), como o desenho padrão.</summary>
    Padded,
    /// <summary>Corta a borda transparente do ícone e encaixa no tile inteiro, sem cortar o desenho.</summary>
    Fill,
    /// <summary>Corta a borda transparente e cobre o tile todo; no Mini (mais largo que alto) corta em cima e embaixo.</summary>
    Cover,
}

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
            g.Clear(visual.Background ?? Background);

            var side = Math.Min(size.Width, size.Height);
            var iconSide = side * 0.78f;
            var iconRect = new RectangleF((size.Width - iconSide) / 2f, (size.Height - iconSide) / 2f, iconSide, iconSide);

            if (visual.Icon is { } icon)
            {
                // Ícone com placa de cor sólida (Xbox, apps da Store): o tile inteiro fica dessa cor, como no Stream Deck.
                Color? plate;
                lock (icon)
                    plate = Analyze(icon).Plate;
                if (visual.Background is null && visual.Fit != IconFit.Padded && !visual.Dimmed && plate is { } color)
                    g.Clear(color);
                if (visual.Fit == IconFit.Padded)
                    DrawIcon(g, icon, iconRect, visual.Dimmed, cover: false, trim: false);
                else
                    DrawIcon(g, icon, new RectangleF(0, 0, size.Width, size.Height), visual.Dimmed,
                        cover: visual.Fit == IconFit.Cover, trim: true);
            }
            else if (visual.Glyph is { } glyph)
                DrawGlyph(g, size, theme, glyph, visual.Dimmed,
                    hasText: !string.IsNullOrWhiteSpace(visual.Title) || !string.IsNullOrWhiteSpace(visual.Detail));
            else
                DrawDotGrid(g, iconRect, visual.Dimmed);

            if (visual.DotCount > 1)
                DrawPositionDots(g, size, visual.DotCount, visual.DotIndex);

            var text = !string.IsNullOrWhiteSpace(visual.Detail) ? visual.Detail : visual.Title;
            if (!string.IsNullOrWhiteSpace(text))
                DrawTitle(g, size, theme, text!, small: !string.IsNullOrWhiteSpace(visual.Detail));

            if (visual.Badge is { } badge)
                DrawBadge(g, size, badge);

            if (visual.Running)
                DrawRunningDot(g, size);
        }
        return ToFrame(bitmap);
    }

    private static void DrawIcon(Graphics g, Bitmap icon, RectangleF area, bool dimmed, bool cover, bool trim)
    {
        lock (icon)
        {
            RectangleF source = trim ? Analyze(icon).Bounds : new RectangleF(0, 0, icon.Width, icon.Height);
            // Mantém a proporção (alguns ícones da Store não são quadrados).
            var scale = cover
                ? Math.Max(area.Width / source.Width, area.Height / source.Height)
                : Math.Min(area.Width / source.Width, area.Height / source.Height);
            if (cover)
            {
                // Recorta da fonte só o que cabe no tile, centrado.
                var w = area.Width / scale;
                var h = area.Height / scale;
                source = new RectangleF(source.X + (source.Width - w) / 2, source.Y + (source.Height - h) / 2, w, h);
            }
            var tw = source.Width * scale;
            var th = source.Height * scale;
            var target = new RectangleF(area.X + (area.Width - tw) / 2, area.Y + (area.Height - th) / 2, tw, th);

            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY); // sem borda clara no limite da imagem ao escalar
            if (dimmed)
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.35f });
            g.DrawImage(icon, PointsOf(target), source, GraphicsUnit.Pixel, attributes);
        }
    }

    private static PointF[] PointsOf(RectangleF r) => [new(r.Left, r.Top), new(r.Right, r.Top), new(r.Left, r.Bottom)];

    /// <summary>Onde está o desenho do ícone e, se ele tem uma placa de cor sólida, que cor é.</summary>
    internal sealed record IconInfo(Rectangle Bounds, Color? Plate);

    private static readonly ConditionalWeakTable<Bitmap, IconInfo> Infos = new();

    /// <summary>
    /// O shell entrega o ícone com margem transparente; sem ela o ícone enche o tile. Calculado uma vez por bitmap
    /// (chame com o lock do bitmap).
    /// </summary>
    internal static IconInfo Analyze(Bitmap icon) => Infos.GetValue(icon, static bitmap => Measure(bitmap));

    internal static Rectangle ContentBounds(Bitmap icon)
    {
        lock (icon)
            return Analyze(icon).Bounds;
    }

    private static IconInfo Measure(Bitmap bitmap)
    {
        var full = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(full, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels;
        int stride;
        try
        {
            stride = data.Stride;
            pixels = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (pixels[y * stride + x * 4 + 3] <= 16)
                    continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        if (right < 0)
            return new IconInfo(full, null);
        var bounds = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        return new IconInfo(bounds, DetectPlate(pixels, stride, bounds));
    }

    /// <summary>
    /// Olha o meio das quatro bordas do desenho (os cantos podem ser arredondados). Se quase tudo for opaco e da mesma
    /// cor, o ícone é uma placa (Xbox: quadrado verde) e devolve essa cor.
    /// </summary>
    private static Color? DetectPlate(byte[] pixels, int stride, Rectangle b)
    {
        if (b.Width < 16 || b.Height < 16)
            return null;
        var samples = new List<(int R, int G, int B)>();
        var opaque = 0;
        var total = 0;
        void Sample(int x, int y)
        {
            total++;
            var i = y * stride + x * 4;
            if (pixels[i + 3] < 240)
                return;
            opaque++;
            samples.Add((pixels[i + 2], pixels[i + 1], pixels[i]));
        }
        var inset = 2;
        for (var x = b.Left + b.Width * 15 / 100; x < b.Right - b.Width * 15 / 100; x++)
        {
            Sample(x, b.Top + inset);
            Sample(x, b.Bottom - 1 - inset);
        }
        for (var y = b.Top + b.Height * 15 / 100; y < b.Bottom - b.Height * 15 / 100; y++)
        {
            Sample(b.Left + inset, y);
            Sample(b.Right - 1 - inset, y);
        }
        if (total == 0 || opaque < total * 9 / 10)
            return null;

        static int Median(List<int> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }
        var r = Median(samples.Select(p => p.R).ToList());
        var g = Median(samples.Select(p => p.G).ToList());
        var bl = Median(samples.Select(p => p.B).ToList());
        var close = samples.Count(p => Math.Abs(p.R - r) <= 24 && Math.Abs(p.G - g) <= 24 && Math.Abs(p.B - bl) <= 24);
        return close >= samples.Count * 9 / 10 ? Color.FromArgb(r, g, bl) : null;
    }

    private static readonly string IconFont = PickIconFont();

    private static string PickIconFont()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }

    /// <summary>
    /// Desenha o glifo pelo contorno medido, não pelo tamanho da fonte: a caixa da fonte tem muita sobra em volta do
    /// desenho, e o ícone ficava pequeno no tile.
    /// </summary>
    private static void DrawGlyph(Graphics g, PixelSize size, WidgetTheme theme, string glyph, bool dimmed, bool hasText)
    {
        var side = Math.Min(size.Width, size.Height);
        var top = side * 0.14f; // espaço das bolinhas de posição
        var bottom = hasText ? size.Height * 0.70f : size.Height - side * 0.10f;
        var box = new RectangleF(0, top, size.Width, Math.Max(1f, bottom - top));
        var target = Math.Min(box.Height, side * 0.80f);

        var color = ToColor(theme.PrimaryForeground);
        using var brush = new SolidBrush(dimmed ? Color.FromArgb(110, color) : color);
        FillGlyph(g, glyph, box, target, brush);
    }

    /// <summary>Glifo centrado em <paramref name="box"/>, com o maior lado do desenho igual a <paramref name="target"/>.</summary>
    private static void FillGlyph(Graphics g, string glyph, RectangleF box, float target, Brush brush)
    {
        using var path = new GraphicsPath();
        using (var family = new FontFamily(IconFont))
            path.AddString(glyph, family, (int)FontStyle.Regular, 100f, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        var scale = target / Math.Max(bounds.Width, bounds.Height);
        using var matrix = new Matrix();
        matrix.Translate(box.X + box.Width / 2f, box.Y + box.Height / 2f);
        matrix.Scale(scale, scale);
        matrix.Translate(-(bounds.X + bounds.Width / 2f), -(bounds.Y + bounds.Height / 2f));
        path.Transform(matrix);
        g.FillPath(brush, path);
    }

    private static void DrawBadge(Graphics g, PixelSize size, TileBadge badge)
    {
        var side = Math.Min(size.Width, size.Height);
        var d = Math.Max(16f, side * 0.30f); // 15% maior que o primeiro desenho, a pedido
        var margin = Math.Max(3f, side * 0.06f);
        var rect = new RectangleF(margin, size.Height - d - margin, d, d);
        using (var shape = RoundedRect(rect, d * 0.25f))
        using (var fill = new SolidBrush(badge.Color))
            g.FillPath(fill, shape);
        FillGlyph(g, badge.Glyph, rect, d * 0.62f, Brushes.White);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var dia = radius * 2;
        path.AddArc(r.X, r.Y, dia, dia, 180, 90);
        path.AddArc(r.Right - dia, r.Y, dia, dia, 270, 90);
        path.AddArc(r.Right - dia, r.Bottom - dia, dia, dia, 0, 90);
        path.AddArc(r.X, r.Bottom - dia, dia, dia, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Uma bolinha por item do carrossel, no topo; a do dispositivo atual preenchida.</summary>
    private static void DrawPositionDots(Graphics g, PixelSize size, int count, int index)
    {
        var side = Math.Min(size.Width, size.Height);
        var d = Math.Max(5f, side * 0.07f);
        var gap = d * 0.7f;
        var total = count * d + (count - 1) * gap;
        if (total > size.Width - 8)
            return;
        var x = (size.Width - total) / 2;
        var y = Math.Max(3f, side * 0.05f);
        using var pen = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, d * 0.18f));
        using var fill = new SolidBrush(Color.White);
        for (var i = 0; i < count; i++)
        {
            var rect = new RectangleF(x + i * (d + gap), y, d, d);
            if (i == index)
                g.FillEllipse(fill, rect);
            else
                g.DrawEllipse(pen, rect);
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
