using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>
/// Ícone escolhido pela pessoa no lugar do ícone do app: uma imagem (.png, .jpg, .bmp, .gif) ou o ícone de um
/// .ico, .exe ou .dll. Tudo vira PNG ao importar, então o widget só lê PNG depois.
/// </summary>
internal static class CustomIconFile
{
    /// <summary>Extensões aceitas no seletor de arquivo da tela de configurações.</summary>
    public const string Accept = ".png,.jpg,.jpeg,.bmp,.gif,.ico,.exe,.dll";

    /// <summary>Maior lado guardado. O tile maior tem menos que isso; uma foto de 4000 px não precisa ir inteira.</summary>
    public const int MaxSide = 512;

    private static readonly string[] IconFiles = [".ico", ".exe", ".dll"];

    public static bool IsIconFile(string path) =>
        IconFiles.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Lê o arquivo e devolve o ícone em 32 bits com alfa. Lança <see cref="InvalidDataException"/> se não der.</summary>
    public static Bitmap Load(string path)
    {
        using var source = IsIconFile(path) ? FromIconFile(path) : FromImage(path);
        return Shrink(source);
    }

    /// <summary>Converte para PNG em <paramref name="target"/>.</summary>
    public static void SaveAsPng(string sourcePath, string target)
    {
        using var bitmap = Load(sourcePath);
        bitmap.Save(target, ImageFormat.Png);
    }

    /// <summary>
    /// O primeiro ícone do arquivo em 256 px. O Windows escolhe o maior tamanho que o arquivo tem e escala; um exe que
    /// só tem 48 px fica borrado, mas grande.
    /// </summary>
    private static Bitmap FromIconFile(string path)
    {
        var handles = new IntPtr[1];
        var count = Native.PrivateExtractIcons(path, 0, 256, 256, handles, null, 1, 0);
        if (count == 0 || count == uint.MaxValue || handles[0] == IntPtr.Zero)
            throw new InvalidDataException("O arquivo não tem ícone.");
        try
        {
            using var icon = Icon.FromHandle(handles[0]);
            return icon.ToBitmap();
        }
        finally
        {
            Native.DestroyIcon(handles[0]);
        }
    }

    private static Bitmap FromImage(string path)
    {
        try
        {
            // Copia para não segurar o arquivo aberto.
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var loaded = new Bitmap(stream);
            var copy = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy))
                g.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
            return copy;
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("O arquivo não é uma imagem.", ex);
        }
    }

    private static Bitmap Shrink(Bitmap source)
    {
        var side = Math.Max(source.Width, source.Height);
        var scale = side > MaxSide ? (float)MaxSide / side : 1f;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceCopy;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return result;
    }
}
