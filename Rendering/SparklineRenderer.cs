using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Metrics;

namespace OpenWigiDash.SystemPlugin.Rendering;

/// <summary>O que o tile do System Info mostra.</summary>
/// <param name="Values">Histórico na unidade mostrada, do mais velho ao mais novo; <c>null</c> = sem leitura naquele ponto.</param>
/// <param name="Max">Topo do gráfico; <c>null</c> = automático pelo maior valor visível.</param>
internal sealed record SparkVisual(
    SystemInfoConfig Config,
    string Title,
    string Value,
    string Unit,
    IReadOnlyList<double?> Values,
    double Min,
    double? Max,
    bool Alert = false);

/// <summary>
/// Desenha o tile como o System Vitals do Stream Deck: título em cima, valor grande no meio com a unidade em
/// sobrescrito e o gráfico ocupando o tile por trás. As medidas do System Vitals são de um quadrado de 512 px; aqui
/// elas viram proporção da altura do tile.
/// </summary>
internal static class SparklineRenderer
{
    /// <summary>Lado do quadro do System Vitals; as medidas salvas (linha, contorno) são nessa escala.</summary>
    private const float Reference = 512f;

    private static readonly Lazy<FontFamily> Rajdhani = new(LoadFont);

    public static WidgetFrame Render(PixelSize size, SparkVisual visual)
    {
        using var bitmap = Draw(size, visual);
        return TileRenderer.ToFrame(bitmap);
    }

    internal static Bitmap Draw(PixelSize size, SparkVisual visual)
    {
        var config = visual.Config;
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        var background = TileColor.Parse(visual.Alert ? config.AlertColor : config.Background) ?? Color.Black;
        g.Clear(background);

        var chart = TileColor.Parse(config.ChartColor) ?? Color.Magenta;
        DrawChart(g, size, config, chart, visual);

        var h = (float)size.Height;
        if (!string.IsNullOrWhiteSpace(visual.Title))
        {
            var em = h * (72f / Reference) * 1.45f * config.TitleSize / 100f;
            DrawText(g, size, visual.Title, null, false, em, h * 0.20f, TileColor.Parse(config.TitleColor) ?? Color.White,
                config.TitleOutline ? background : null, h * 10f / Reference * 1.4f);
        }

        var valueEm = h * (200f / Reference) * 1.3f * config.ValueSize / 100f;
        var unit = config.ShowUnit ? visual.Unit : "";
        DrawText(g, size, visual.Value, unit, config.UnitSuperscript && unit != "°", valueEm, h * 0.56f,
            TileColor.Parse(config.ValueColor) ?? Color.White, config.ValueOutline ? background : null, h * 20f / Reference * 1.4f);
        return bitmap;
    }

    /// <summary>Quantos pontos cabem no gráfico: um a cada 4 px, entre 20 e a capacidade da série.</summary>
    internal static int VisiblePoints(int width) => Math.Clamp(width / 4, 20, MetricSeries.Capacity);

    /// <summary>Topo da escala: o fixo, ou o maior valor visível com folga de 10% (no mínimo 1).</summary>
    internal static double ScaleMax(double min, double? max, IEnumerable<double?> visible)
    {
        if (max is { } fixedMax && fixedMax > min)
            return fixedMax;
        var peak = visible.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0).Max();
        return Math.Max(min + 1, peak * 1.1);
    }

    /// <summary>Pontos do gráfico, o mais novo na borda direita. Cada trecho sem buraco vira uma lista.</summary>
    internal static List<List<PointF>> Segments(PixelSize size, IReadOnlyList<double?> values, double min, double max, int baseline)
    {
        var count = VisiblePoints(size.Width);
        var start = Math.Max(0, values.Count - count);
        var step = size.Width / (float)(count - 1);
        var floor = size.Height * (1 - Math.Clamp(baseline, 0, 50) / 100f);
        var top = size.Height * 0.02f;
        var segments = new List<List<PointF>>();
        List<PointF>? current = null;
        for (var i = start; i < values.Count; i++)
        {
            if (values[i] is not { } v || double.IsNaN(v))
            {
                current = null;
                continue;
            }
            var t = (float)Math.Clamp((v - min) / (max - min), 0, 1);
            var x = size.Width - (values.Count - 1 - i) * step;
            var y = floor - t * (floor - top);
            if (current is null)
                segments.Add(current = []);
            current.Add(new PointF(x, y));
        }
        return segments;
    }

    private static void DrawChart(Graphics g, PixelSize size, SystemInfoConfig config, Color color, SparkVisual visual)
    {
        var count = VisiblePoints(size.Width);
        var visible = visual.Values.Skip(Math.Max(0, visual.Values.Count - count)).ToList();
        var max = ScaleMax(visual.Min, visual.Max, visible);
        var segments = Segments(size, visual.Values, visual.Min, max, config.Baseline);
        var h = (float)size.Height;
        var lineWidth = Math.Max(1f, h * config.LineWidth / Reference);

        foreach (var points in segments)
        {
            // Um ponto só ainda não forma linha: estende até a borda para aparecer.
            List<PointF> pts = points.Count == 1 ? [new PointF(Math.Max(0, points[0].X - 4), points[0].Y), points[0]] : points;
            using var line = new GraphicsPath();
            line.AddCurve(pts.ToArray(), 0.4f);
            using var area = (GraphicsPath)line.Clone();
            area.AddLine(pts[^1], new PointF(pts[^1].X, h));
            area.AddLine(new PointF(pts[^1].X, h), new PointF(pts[0].X, h));
            area.CloseFigure();

            switch (config.Style)
            {
                case ChartStyle.SoftFill:
                case ChartStyle.Fill:
                    FillGradient(g, area, color, config, h);
                    if (config.Style == ChartStyle.SoftFill)
                        StrokeLine(g, line, color, lineWidth);
                    break;
                case ChartStyle.Line:
                    StrokeLine(g, line, color, lineWidth);
                    break;
                case ChartStyle.LinePoints:
                    StrokeLine(g, line, color, lineWidth);
                    var r = Math.Max(1.5f, lineWidth * 1.6f);
                    using (var dot = new SolidBrush(color))
                    {
                        var every = Math.Max(1, (int)Math.Ceiling(r * 2.6f / (size.Width / (float)(count - 1))));
                        for (var i = pts.Count - 1; i >= 0; i -= every)
                            g.FillEllipse(dot, pts[i].X - r, pts[i].Y - r, r * 2, r * 2);
                    }
                    break;
                case ChartStyle.Hatch:
                    Hatch(g, area, size, color, config);
                    break;
                case ChartStyle.Matrix:
                    Matrix(g, area, size, color, config);
                    break;
            }
        }
    }

    private static void FillGradient(Graphics g, GraphicsPath area, Color color, SystemInfoConfig config, float h)
    {
        var top = Alpha(config.OpacityTop);
        var bottom = Alpha(config.OpacityBottom);
        using var brush = new LinearGradientBrush(new RectangleF(0, -1, 1, h + 2), Color.FromArgb(top, color),
            Color.FromArgb(bottom, color), LinearGradientMode.Vertical);
        g.FillPath(brush, area);
    }

    private static void StrokeLine(Graphics g, GraphicsPath line, Color color, float width)
    {
        using var pen = new Pen(color, width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawPath(pen, line);
    }

    private static void Hatch(Graphics g, GraphicsPath area, PixelSize size, Color color, SystemInfoConfig config)
    {
        var state = g.Save();
        g.SetClip(area);
        var h = (float)size.Height;
        var stripe = Math.Max(1f, h * 8f / Reference * 1.5f);
        var gap = Math.Max(2f, h * 16f / Reference * 1.5f);
        g.TranslateTransform(size.Width / 2f, h / 2f);
        g.RotateTransform(45);
        var reach = (size.Width + h) * 1.5f;
        using var brush = new SolidBrush(Color.FromArgb(Alpha(Math.Max(config.OpacityTop, 60)), color));
        for (var x = -reach; x < reach; x += stripe + gap)
            g.FillRectangle(brush, x, -reach, stripe, reach * 2);
        g.Restore(state);
    }

    private static void Matrix(Graphics g, GraphicsPath area, PixelSize size, Color color, SystemInfoConfig config)
    {
        var state = g.Save();
        g.SetClip(area);
        var h = (float)size.Height;
        var spacing = Math.Max(3f, h * 38f / Reference * 1.2f);
        var r = Math.Max(1f, h * 4f / Reference * 1.5f);
        using var brush = new SolidBrush(Color.FromArgb(Alpha(Math.Max(config.OpacityTop, 60)), color));
        for (var y = h - spacing / 2; y > -spacing; y -= spacing)
        {
            for (var x = size.Width - spacing / 2; x > -spacing; x -= spacing)
                g.FillEllipse(brush, x - r, y - r, r * 2, r * 2);
        }
        g.Restore(state);
    }

    private static int Alpha(int percent) => (int)Math.Round(Math.Clamp(percent, 0, 100) * 2.55);

    /// <summary>
    /// Texto centrado na horizontal com o centro vertical em <paramref name="centerY"/>. A unidade vai à direita, em
    /// sobrescrito (metade do tamanho, alinhada ao topo) ou do mesmo tamanho. Diminui até caber na largura.
    /// </summary>
    private static void DrawText(Graphics g, PixelSize size, string text, string? unit, bool superscript, float em,
        float centerY, Color fill, Color? outline, float outlineWidth)
    {
        var family = Rajdhani.Value;
        var maxWidth = size.Width * 0.94f;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var main = new GraphicsPath();
            main.AddString(text, family, (int)FontStyle.Bold, em, PointF.Empty, StringFormat.GenericTypographic);
            var mb = main.GetBounds();
            using var tail = new GraphicsPath();
            var tb = RectangleF.Empty;
            var unitEm = superscript ? em * 0.5f : em;
            if (!string.IsNullOrEmpty(unit))
            {
                tail.AddString(unit, family, (int)FontStyle.Bold, unitEm, PointF.Empty, StringFormat.GenericTypographic);
                tb = tail.GetBounds();
            }
            var gap = tb.Width > 0 ? em * 0.04f : 0;
            var total = mb.Width + gap + tb.Width;
            if (total > maxWidth && attempt < 5 && total > 0)
            {
                em *= maxWidth / total * 0.98f;
                continue;
            }

            var left = (size.Width - total) / 2f;
            using (var m = new Matrix())
            {
                m.Translate(left - mb.X, centerY - (mb.Y + mb.Height / 2f));
                main.Transform(m);
            }
            if (tb.Width > 0)
            {
                var placed = main.GetBounds();
                // Sobrescrito: topo da unidade no topo do número. Normal: mesma base (o "°" já fica em cima sozinho).
                var dy = superscript ? placed.Y - tb.Y : placed.Bottom - tb.Bottom;
                if (unit is "°")
                    dy = placed.Y - tb.Y;
                using var m = new Matrix();
                m.Translate(placed.Right + gap - tb.X, dy);
                tail.Transform(m);
            }

            if (outline is { } o && outlineWidth > 0)
            {
                using var pen = new Pen(o, outlineWidth) { LineJoin = LineJoin.Round };
                g.DrawPath(pen, main);
                if (tb.Width > 0)
                    g.DrawPath(pen, tail);
            }
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, main);
            if (tb.Width > 0)
                g.FillPath(brush, tail);
            return;
        }
    }

    /// <summary>Rajdhani Bold (a fonte do System Vitals, licença OFL) que vai na pasta do plugin; sem ela, Segoe UI.</summary>
    private static FontFamily LoadFont()
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(SparklineRenderer).Assembly.Location) ?? AppContext.BaseDirectory;
            var path = Path.Combine(dir, "Fonts", "Rajdhani-Bold.ttf");
            if (File.Exists(path))
            {
                var collection = new PrivateFontCollection();
                collection.AddFontFile(path);
                if (collection.Families.Length > 0)
                    return collection.Families[0];
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException)
        {
        }
        try
        {
            return new FontFamily("Segoe UI");
        }
        catch (ArgumentException)
        {
            return new FontFamily(GenericFontFamilies.SansSerif);
        }
    }
}
