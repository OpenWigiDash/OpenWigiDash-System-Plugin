using System.Drawing;
using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Apps;
using OpenWigiDash.SystemPlugin.Widgets;

namespace SystemPlugin.Tests;

public class OpenApplicationTests
{
    private static readonly AppChoice Steam = new(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Steam\steam.exe", "Steam");

    private sealed class FakePlatform : IAppPlatform
    {
        public List<string> Calls { get; } = [];
        public bool HasWindow { get; set; } = true;

        public void Launch(AppChoice app) => Calls.Add("launch " + app.Name);
        public bool BringToFront(AppState state)
        {
            Calls.Add("front");
            return HasWindow;
        }
        public void Quit(AppState state, LongPressAction action) => Calls.Add("quit " + action);
        public Bitmap? PeekIcon(string id) => null;
        public Task<Bitmap?> LoadIconAsync(string id, bool refresh) => Task.FromResult<Bitmap?>(null);
    }

    private static (OpenApplicationInstance Instance, AppWatch Watch, FakePlatform Platform, FakeContext Context) Create()
    {
        var context = new FakeContext(WidgetSize.Small);
        var watch = new AppWatch(new AppMonitor());
        var platform = new FakePlatform();
        return (new OpenApplicationInstance(context, watch, platform), watch, platform, context);
    }

    private static AppState Running() =>
        new(true, [new ProcessInfo(1, "steam", @"C:\Program Files (x86)\Steam\steam.exe", null)], [new WindowInfo(5, 1, null, false)]);

    private static TouchEvent Tap => new(TouchKind.Tap, 10, 10, TimeSpan.FromMilliseconds(80));
    private static TouchEvent Hold => new(TouchKind.LongPress, 10, 10, TimeSpan.FromMilliseconds(600), 1);

    [Fact]
    public async Task TapLaunchesWhenClosed()
    {
        var (instance, _, platform, _) = Create();
        await instance.SetAppAsync(Steam);

        await instance.OnTouchAsync(Tap, default);

        Assert.Equal(new[] { "launch Steam" }, platform.Calls);
    }

    [Fact]
    public async Task TapBringsToFrontWhenRunning()
    {
        var (instance, watch, platform, _) = Create();
        await instance.SetAppAsync(Steam);
        watch.State = Running();

        await instance.OnTouchAsync(Tap, default);

        Assert.Equal(new[] { "front" }, platform.Calls);
    }

    [Fact]
    public async Task TapLaunchesAgainWhenOnlyInTray()
    {
        var (instance, watch, platform, _) = Create();
        await instance.SetAppAsync(Steam);
        watch.State = Running();
        platform.HasWindow = false;

        await instance.OnTouchAsync(Tap, default);

        Assert.Equal(new[] { "front", "launch Steam" }, platform.Calls);
    }

    [Fact]
    public async Task TapLaunchesWhenBringToFrontIsOff()
    {
        var (instance, watch, platform, _) = Create();
        await instance.SetAppAsync(Steam);
        await instance.SetBringToFrontAsync(false);
        watch.State = Running();

        await instance.OnTouchAsync(Tap, default);

        Assert.Equal(new[] { "launch Steam" }, platform.Calls);
    }

    [Theory]
    [InlineData(LongPressAction.Quit, "quit Quit")]
    [InlineData(LongPressAction.ForceQuit, "quit ForceQuit")]
    public async Task LongPressQuits(LongPressAction action, string expected)
    {
        var (instance, watch, platform, _) = Create();
        await instance.SetAppAsync(Steam);
        await instance.SetLongPressAsync(action);
        watch.State = Running();

        await instance.OnTouchAsync(Hold, default);

        Assert.Equal(new[] { expected }, platform.Calls);
    }

    [Fact]
    public async Task LongPressDoesNothingWhenClosedOrDisabled()
    {
        var (instance, watch, platform, _) = Create();
        await instance.SetAppAsync(Steam);
        await instance.OnTouchAsync(Hold, default);

        await instance.SetLongPressAsync(LongPressAction.DoNothing);
        watch.State = Running();
        await instance.OnTouchAsync(Hold, default);

        Assert.Empty(platform.Calls);
    }

    [Fact]
    public async Task SettingsSurviveReload()
    {
        var (instance, _, _, context) = Create();
        await instance.SetAppAsync(Steam);
        await instance.SetTitleAsync("Jogos");
        await instance.SetLongPressAsync(LongPressAction.ForceQuit);
        await instance.SetBringToFrontAsync(false);

        var reloaded = new OpenApplicationInstance(context, new AppWatch(new AppMonitor()), new FakePlatform());
        await reloaded.LoadSettingsAsync(default);

        Assert.Equal(Steam, reloaded.App);
        Assert.Equal("Jogos", reloaded.Title);
        Assert.Equal(LongPressAction.ForceQuit, reloaded.LongPress);
        Assert.False(reloaded.BringToFront);
    }

    [Fact]
    public async Task VisualShowsRunningDotAndTitle()
    {
        var (instance, watch, _, _) = Create();
        Assert.Equal("Escolha um app", instance.CurrentVisual().Detail);

        await instance.SetAppAsync(Steam);
        await instance.SetTitleAsync("Steam");
        watch.State = Running();

        var visual = instance.CurrentVisual();
        Assert.True(visual.Running);
        Assert.Equal("Steam", visual.Title);
        Assert.Null(visual.Detail);
    }

    [Fact]
    public async Task RendersAtContentSize()
    {
        var (instance, _, _, context) = Create();
        var frame = await instance.RenderAsync(RenderReason.Initial, default);
        Assert.Equal(context.PixelSize, frame.Size);
    }

    [Fact]
    public async Task ManifestIsValidAndPreviewRenders()
    {
        var widget = new OpenApplicationWidget();
        Assert.Empty(widget.Manifest.Validate());

        foreach (var size in widget.Manifest.SupportedSizes)
        {
            var pixels = TileFrame.Default.ContentSize(size.CellSpan);
            var preview = await widget.GetPreviewAsync(size, pixels, default);
            Assert.Equal(pixels, preview!.Size);
        }
    }
}
