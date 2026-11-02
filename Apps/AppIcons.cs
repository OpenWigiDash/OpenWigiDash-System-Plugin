using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>
/// Ícone do app pelo shell (<c>IShellItemImageFactory</c>), o mesmo que o Iniciar mostra: funciona para atalho,
/// exe e app da Store, e segue o exe atual depois de uma atualização. Guarda uma cópia em PNG para mostrar algo
/// enquanto o shell não responde ou se o app sair do Iniciar.
/// </summary>
internal static class AppIcons
{
    public const int TileSize = 256;
    public const int ListSize = 32;

    private static readonly ConcurrentDictionary<(string Id, int Size), Bitmap> Memory = new();

    private static string CacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenWigiDash", "WidgetData", "System", "icons");

    /// <summary>Ícone em memória, sem ir ao shell (para o quadro, que não pode esperar).</summary>
    public static Bitmap? Peek(string id, int size = TileSize) => Memory.TryGetValue((id, size), out var bitmap) ? bitmap : null;

    /// <summary>
    /// Lê o ícone de novo do shell e troca o da memória. Se o shell falhar, usa o PNG guardado.
    /// <paramref name="refresh"/> = <c>false</c> devolve o da memória quando já existe.
    /// </summary>
    public static async Task<Bitmap?> LoadAsync(string id, int size = TileSize, bool refresh = false)
    {
        if (!refresh && Peek(id, size) is { } cached)
            return cached;

        var fresh = await Sta.RunAsync(() => FromShell(id, size)).ConfigureAwait(false);
        if (fresh is not null)
        {
            if (size == TileSize)
                TrySave(id, fresh);
        }
        else
        {
            fresh = Peek(id, size) ?? (size == TileSize ? TryLoadSaved(id) : null);
            if (fresh is null)
                return null;
        }

        // O antigo não é descartado: um quadro em andamento pode estar desenhando com ele. O GC solta.
        Memory[(id, size)] = fresh;
        return fresh;
    }

    private static readonly ConcurrentDictionary<string, string?> ListUrls = new(StringComparer.OrdinalIgnoreCase);

    private static string ListCacheDirectory { get; } = Path.Combine(CacheDirectory, "list");

    /// <summary>Ícone pequeno da lista, se já está em memória (sem esperar nada).</summary>
    public static bool TryGetDataUrl(string id, out string? url) => ListUrls.TryGetValue(id, out url);

    /// <summary>
    /// Ícone pequeno em PNG data URL, para a lista da tela de configurações. Vem da memória, senão do PNG guardado em
    /// disco (instantâneo depois da primeira vez, mesmo com o Manager reiniciado), senão do shell, e aí é guardado.
    /// </summary>
    public static Task<string?> GetDataUrlAsync(string id)
    {
        if (ListUrls.TryGetValue(id, out var cached))
            return Task.FromResult(cached);
        // Um pedido por app: a lista redesenha várias vezes enquanto os ícones chegam.
        return InFlight.GetOrAdd(id, key => LoadDataUrlAsync(key).ContinueWith(t =>
        {
            InFlight.TryRemove(key, out _);
            return t.IsCompletedSuccessfully ? t.Result : null;
        }, TaskScheduler.Default));
    }

    private static readonly ConcurrentDictionary<string, Task<string?>> InFlight = new(StringComparer.OrdinalIgnoreCase);

    private static async Task<string?> LoadDataUrlAsync(string id)
    {

        var path = Path.Combine(ListCacheDirectory, CacheName(id));
        try
        {
            if (File.Exists(path))
            {
                var saved = "data:image/png;base64," + Convert.ToBase64String(await File.ReadAllBytesAsync(path).ConfigureAwait(false));
                ListUrls[id] = saved;
                return saved;
            }
        }
        catch (IOException)
        {
            // Lê do shell.
        }

        var png = await Sta.RunAsync(() =>
        {
            using var bitmap = FromShell(id, ListSize);
            if (bitmap is null)
                return null;
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }).ConfigureAwait(false);

        string? url = null;
        if (png is not null)
        {
            url = "data:image/png;base64," + Convert.ToBase64String(png);
            try
            {
                Directory.CreateDirectory(ListCacheDirectory);
                await File.WriteAllBytesAsync(path, png).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Sem cache em disco a lista só fica mais lenta na próxima vez.
            }
        }
        ListUrls[id] = url;
        return url;
    }

    /// <summary>
    /// Ícone que o Explorer mostra para um arquivo, pasta ou atalho (.exe, .bat, .lnk...), sem cache. Para o Execute,
    /// que aponta para um caminho em vez de um app do Iniciar.
    /// </summary>
    public static Task<Bitmap?> LoadFileIconAsync(string path, int size = TileSize) =>
        Sta.RunAsync(() => FromParsingName(path, size));

    private static Bitmap? FromShell(string id, int size) => FromParsingName($@"shell:AppsFolder\{id}", size);

    private static Bitmap? FromParsingName(string name, int size)
    {
        if (Native.SHCreateItemFromParsingName(name, IntPtr.Zero, Native.IID_IShellItemImageFactory, out var obj) != 0)
            return null;
        var factory = (Native.IShellItemImageFactory)obj;
        try
        {
            if (factory.GetImage(new Native.NativeSize(size, size), Native.Siigbf.BiggerSizeOk | Native.Siigbf.IconOnly, out var hbitmap) != 0
                || hbitmap == IntPtr.Zero)
                return null;
            try
            {
                return FromHBitmap(hbitmap);
            }
            finally
            {
                Native.DeleteObject(hbitmap);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>
    /// HBITMAP de 32 bits com alfa para <see cref="Bitmap"/>. <c>Image.FromHbitmap</c> perde o alfa (fundo preto
    /// em volta do ícone), então copiamos os pixels com <c>GetDIBits</c>, de cima para baixo.
    /// </summary>
    private static unsafe Bitmap? FromHBitmap(IntPtr hbitmap)
    {
        if (Native.GetObject(hbitmap, sizeof(Native.NativeBitmap), out var info) == 0 || info.bmWidth <= 0 || info.bmHeight <= 0)
            return null;
        var width = info.bmWidth;
        var height = info.bmHeight;
        var pixels = new byte[width * height * 4];
        var header = new Native.BitmapInfoHeader
        {
            biSize = (uint)sizeof(Native.BitmapInfoHeader),
            biWidth = width,
            biHeight = -height, // negativo = linhas de cima para baixo
            biPlanes = 1,
            biBitCount = 32,
        };
        var hdc = Native.GetDC(IntPtr.Zero);
        try
        {
            fixed (byte* p = pixels)
            {
                if (Native.GetDIBits(hdc, hbitmap, 0, (uint)height, p, ref header, 0) == 0)
                    return null;
            }
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, hdc);
        }

        // Ícone antigo sem canal alfa vem com alfa 0 em tudo: aí ele é opaco.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                hasAlpha = true;
                break;
            }
        }
        if (!hasAlpha)
        {
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
        }

        // O shell entrega alfa pré-multiplicado.
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (var y = 0; y < height; y++)
                System.Runtime.InteropServices.Marshal.Copy(pixels, y * width * 4, data.Scan0 + y * data.Stride, width * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    private static string CacheName(string id) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToLowerInvariant())))[..32] + ".png";

    private static string CachePath(string id) => Path.Combine(CacheDirectory, CacheName(id));

    private static void TrySave(string id, Bitmap bitmap)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            lock (bitmap)
                bitmap.Save(CachePath(id), ImageFormat.Png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            // Sem cópia em disco o widget ainda funciona; só não tem ícone se o shell falhar.
        }
    }

    private static Bitmap? TryLoadSaved(string id)
    {
        try
        {
            var path = CachePath(id);
            if (!File.Exists(path))
                return null;
            // Copia para não segurar o arquivo aberto.
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var loaded = new Bitmap(stream);
            return new Bitmap(loaded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
