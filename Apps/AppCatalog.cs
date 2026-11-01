using System.Runtime.InteropServices;

namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>
/// A lista "Todos os apps" do Iniciar, lida de <c>shell:AppsFolder</c>: traz os apps comuns (pelos atalhos do
/// Iniciar) e os da Store/UWP juntos, com o nome que o Windows mostra.
/// </summary>
internal static class AppCatalog
{
    private const string AppsFolder = "shell:AppsFolder";

    private static readonly object Gate = new();
    private static Task<IReadOnlyList<AppChoice>>? _list;
    private static Task? _icons;

    /// <summary>
    /// A lista, lida uma vez por sessão do Manager e dividida por todas as instâncias. <paramref name="refresh"/>
    /// lê de novo (botão da tela de configurações).
    /// </summary>
    public static Task<IReadOnlyList<AppChoice>> GetListAsync(bool refresh = false)
    {
        lock (Gate)
        {
            if (refresh || _list is null || _list.IsFaulted)
                _list = Sta.RunAsync(List);
            return _list;
        }
    }

    /// <summary>
    /// Lê a lista e os ícones pequenos dela em segundo plano, um por vez (os ícones dos tiles passam na frente entre
    /// um e outro). Depois da primeira vez os ícones vêm do disco e a lista abre pronta.
    /// </summary>
    public static Task WarmAsync()
    {
        lock (Gate)
        {
            if (_icons is { IsCompleted: false })
                return _icons;
            return _icons = Task.Run(async () =>
            {
                var apps = await GetListAsync().ConfigureAwait(false);
                foreach (var app in apps)
                {
                    try
                    {
                        await AppIcons.GetDataUrlAsync(app.Id).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Sem ícone esta linha mostra só o nome.
                    }
                }
            });
        }
    }

    /// <summary>Todos os apps, em ordem alfabética. Rode numa thread STA (<see cref="Sta"/>).</summary>
    public static IReadOnlyList<AppChoice> List()
    {
        var apps = new List<AppChoice>();
        if (Native.SHCreateItemFromParsingName(AppsFolder, IntPtr.Zero, Native.IID_IShellItem, out var folderObj) != 0)
            return apps;
        var folder = (Native.IShellItem)folderObj;
        try
        {
            if (folder.BindToHandler(IntPtr.Zero, Native.BHID_EnumItems, Native.IID_IEnumShellItems, out var enumObj) != 0)
                return apps;
            var items = (Native.IEnumShellItems)enumObj;
            try
            {
                while (items.Next(1, out var item, out var fetched) == 0 && fetched == 1)
                {
                    try
                    {
                        if (item.GetDisplayName(Native.Sigdn.ParentRelativeParsing, out var id) == 0
                            && item.GetDisplayName(Native.Sigdn.NormalDisplay, out var name) == 0
                            && !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                        {
                            apps.Add(new AppChoice(id, name));
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(item);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(items);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(folder);
        }

        return apps
            .DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Pergunta ao shell o que o app é hoje (AUMID, exe atual do atalho). <c>null</c> se ele saiu do Iniciar
    /// (desinstalado). Rode numa thread STA.
    /// </summary>
    public static AppTarget? Resolve(AppChoice choice)
    {
        if (Native.SHCreateItemFromParsingName($@"{AppsFolder}\{choice.Id}", IntPtr.Zero, Native.IID_IShellItem2, out var obj) != 0)
            return null;
        var item = (Native.IShellItem2)obj;
        try
        {
            var name = item.GetDisplayName(Native.Sigdn.NormalDisplay, out var display) == 0 && !string.IsNullOrWhiteSpace(display)
                ? display
                : choice.Name;
            return AppTarget.Build(choice.Id, name,
                GetString(item, Native.PKEY_AppUserModel_ID),
                GetString(item, Native.PKEY_Link_TargetParsingPath),
                GetString(item, Native.PKEY_Link_Arguments),
                KnownFolderPath);
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    private static string? GetString(Native.IShellItem2 item, Native.PropertyKey key) =>
        item.GetString(key, out var value) == 0 && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string? KnownFolderPath(Guid id) =>
        Native.SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var path) == 0 ? path : null;
}
