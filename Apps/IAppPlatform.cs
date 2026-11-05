using System.Drawing;

namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>
/// O que a instância pede ao Windows. Existe para os testes trocarem por um falso; no plugin é sempre
/// <see cref="WindowsAppPlatform"/>.
/// </summary>
internal interface IAppPlatform
{
    /// <summary>Abre o app. Com <paramref name="arguments"/>, abre o exe ou ativa o app da Store com eles.</summary>
    void Launch(AppChoice app, AppTarget? target, string? arguments);
    bool BringToFront(AppState state);
    void Quit(AppState state, LongPressAction action);
    Bitmap? PeekIcon(string id);
    Task<Bitmap?> LoadIconAsync(string id, bool refresh);
}

internal sealed class WindowsAppPlatform : IAppPlatform
{
    public void Launch(AppChoice app, AppTarget? target, string? arguments) => AppActions.Launch(app, target, arguments);
    public bool BringToFront(AppState state) => AppActions.BringToFront(state);
    public void Quit(AppState state, LongPressAction action) => AppActions.Quit(state, action);
    public Bitmap? PeekIcon(string id) => AppIcons.Peek(id);
    public Task<Bitmap?> LoadIconAsync(string id, bool refresh) => AppIcons.LoadAsync(id, AppIcons.TileSize, refresh);
}
