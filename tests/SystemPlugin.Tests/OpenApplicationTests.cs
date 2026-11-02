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

public class TileRendererTests
{
    [Fact]
    public void ContentBoundsSkipsTransparentMargin()
    {
        using var icon = new System.Drawing.Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(icon))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.FillRectangle(System.Drawing.Brushes.Red, 10, 20, 30, 24);
        }

        Assert.Equal(new System.Drawing.Rectangle(10, 20, 30, 24), OpenWigiDash.SystemPlugin.Rendering.TileRenderer.ContentBounds(icon));
    }

    [Fact]
    public void ContentBoundsIgnoresShellFrameAroundSmallIcon()
    {
        // O que o shell devolve para um exe que só tem ícone de 48 px (Intel PresentMon): o ícone no meio de 256 px e
        // uma moldura fraca (alfa 26 a 77) encostada na borda.
        using var icon = new System.Drawing.Bitmap(256, 256, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(icon))
        {
            g.Clear(System.Drawing.Color.Transparent);
            using var shadow = new System.Drawing.Pen(System.Drawing.Color.FromArgb(38, 0, 0, 0));
            using var glow = new System.Drawing.Pen(System.Drawing.Color.FromArgb(77, 255, 255, 255));
            g.DrawRectangle(shadow, 0, 0, 255, 255);
            g.DrawRectangle(glow, 2, 2, 251, 251);
            g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0, 59, 113)), 104, 104, 48, 48);
        }

        Assert.Equal(new System.Drawing.Rectangle(104, 104, 48, 48), OpenWigiDash.SystemPlugin.Rendering.TileRenderer.ContentBounds(icon));
    }

    [Theory]
    [InlineData(OpenWigiDash.SystemPlugin.Rendering.IconFit.Padded)]
    [InlineData(OpenWigiDash.SystemPlugin.Rendering.IconFit.Fill)]
    [InlineData(OpenWigiDash.SystemPlugin.Rendering.IconFit.Cover)]
    public void RendersEveryFitAtTileSize(OpenWigiDash.SystemPlugin.Rendering.IconFit fit)
    {
        using var icon = new System.Drawing.Bitmap(256, 256, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = System.Drawing.Graphics.FromImage(icon))
            g.FillEllipse(System.Drawing.Brushes.Green, 40, 40, 176, 176);
        var size = OpenWigi.Sdk.TileFrame.Default.ContentSize(OpenWigi.Sdk.WidgetSize.Mini.CellSpan);

        var frame = OpenWigiDash.SystemPlugin.Rendering.TileRenderer.Render(size, new OpenWigi.Sdk.WidgetTheme(),
            new OpenWigiDash.SystemPlugin.Rendering.TileVisual(icon, "Xbox", Running: true) { Fit = fit });

        Assert.Equal(size, frame.Size);
    }
}

public class PlateTests
{
    [Fact]
    public void SolidPlateIsDetectedAndTransparentIconIsNot()
    {
        using var plate = new System.Drawing.Bitmap(128, 128, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(plate))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(16, 124, 16)), 16, 16, 96, 96);
            g.FillEllipse(System.Drawing.Brushes.White, 40, 40, 48, 48);
        }
        using var round = new System.Drawing.Bitmap(128, 128, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(round))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.FillEllipse(System.Drawing.Brushes.Blue, 16, 16, 96, 96);
        }

        var info = OpenWigiDash.SystemPlugin.Rendering.TileRenderer.Analyze(plate);
        Assert.Equal(System.Drawing.Color.FromArgb(16, 124, 16).ToArgb(), info.Plate?.ToArgb());
        Assert.Null(OpenWigiDash.SystemPlugin.Rendering.TileRenderer.Analyze(round).Plate);
    }
}

public class CustomIconTests
{
    private static string WritePng(int width, int height, System.Drawing.Color color)
    {
        var path = Path.Combine(Path.GetTempPath(), "owd-icon-" + Guid.NewGuid().ToString("N") + ".png");
        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
            g.Clear(color);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    [Fact]
    public void BigImageIsShrunkKeepingAspect()
    {
        var path = WritePng(2000, 1000, Color.Red);
        try
        {
            using var icon = CustomIconFile.Load(path);
            Assert.Equal(new Size(CustomIconFile.MaxSide, CustomIconFile.MaxSide / 2), icon.Size);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NotAnImageFails()
    {
        var path = Path.Combine(Path.GetTempPath(), "owd-icon-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(path, "não é imagem");
        try
        {
            Assert.Throws<InvalidDataException>(() => CustomIconFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExeIconIsExtracted()
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using var icon = CustomIconFile.Load(explorer);
        Assert.Equal(new Size(256, 256), icon.Size);
    }

    [Fact]
    public async Task CustomIconReplacesAppIconAndSurvivesReload()
    {
        var context = new FakeContext(WidgetSize.Small);
        var instance = new OpenApplicationInstance(context, new AppWatch(new AppMonitor()), new NullPlatform());
        await instance.SetAppAsync(new AppChoice("x", "X"));
        var path = WritePng(64, 64, Color.Blue);
        try
        {
            await instance.SetCustomIconAsync(path);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.NotNull(instance.CurrentVisual().Icon);
        Assert.StartsWith("data:image/png;base64,", instance.CustomIconDataUrl);

        // Outra instância com as mesmas configurações e arquivos (Manager reiniciado).
        var reloaded = new OpenApplicationInstance(context, new AppWatch(new AppMonitor()), new NullPlatform());
        await reloaded.LoadSettingsAsync(default);
        Assert.Equal(Color.Blue.ToArgb(), reloaded.CurrentVisual().Icon!.GetPixel(10, 10).ToArgb());

        var fileId = reloaded.CustomIconFileId!;
        await reloaded.ClearCustomIconAsync();
        Assert.Null(reloaded.CurrentVisual().Icon);
        Assert.Null(await context.Files.GetPathAsync(fileId));
    }

    private sealed class NullPlatform : IAppPlatform
    {
        public void Launch(AppChoice app) { }
        public bool BringToFront(AppState state) => false;
        public void Quit(AppState state, LongPressAction action) { }
        public Bitmap? PeekIcon(string id) => null;
        public Task<Bitmap?> LoadIconAsync(string id, bool refresh) => Task.FromResult<Bitmap?>(null);
    }
}
