using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Audio;
using OpenWigiDash.SystemPlugin.Widgets;

namespace SystemPlugin.Tests;

public class DeviceMatcherTests
{
    private static readonly AudioDevice Speakers = new("{0.0.0}.{spk}", "AC10 (NVIDIA High Definition Audio)", "AC10", "NVIDIA High Definition Audio", true);
    private static readonly AudioDevice Headset = new("{0.0.0}.{jbl-new}", "Fones de ouvido (2- JBL Quantum 800)", "Fones de ouvido", "2- JBL Quantum 800", true);
    private static readonly AudioDevice Realtek = new("{0.0.0}.{rtk}", "Alto-falantes (Realtek(R) Audio)", "Alto-falantes", "Realtek(R) Audio", true);

    private static readonly AudioDeviceEntry SavedHeadset =
        new("{0.0.0}.{jbl-old}", "Fones de ouvido (JBL Quantum 800)", "Fones de ouvido", "JBL Quantum 800");

    [Theory]
    [InlineData("Microfone do Headset (2- JBL Quantum 800)", "microfone do headset (jbl quantum 800)")]
    [InlineData("2- Alto-falantes", "alto-falantes")]
    [InlineData("  AC10   (NVIDIA High Definition Audio) ", "ac10 (nvidia high definition audio)")]
    public void NormalizeDropsWindowsCounter(string name, string expected) =>
        Assert.Equal(expected, DeviceMatcher.Normalize(name));

    [Fact]
    public void FuzzyFindsHeadsetAfterDongleChangedPort()
    {
        Assert.Equal(Headset, DeviceMatcher.Find(SavedHeadset, [Speakers, Headset], DeviceMatching.Fuzzy));
        Assert.Null(DeviceMatcher.Find(SavedHeadset, [Speakers, Headset], DeviceMatching.Exact));
    }

    [Fact]
    public void InactiveDeviceIsNotFound()
    {
        var unplugged = Headset with { Id = SavedHeadset.Id, Active = false };
        Assert.Null(DeviceMatcher.Find(SavedHeadset, [Speakers, unplugged], DeviceMatching.Fuzzy));
    }

    [Fact]
    public void CarouselGoesThroughThreeAndWraps()
    {
        AudioDevice[] devices = [Speakers, Headset, Realtek];
        var entries = devices.Select(AudioDeviceEntry.From).ToList();

        Assert.Equal(1, DeviceMatcher.NextIndex(entries, Speakers.Id, devices, DeviceMatching.Fuzzy));
        Assert.Equal(2, DeviceMatcher.NextIndex(entries, Headset.Id, devices, DeviceMatching.Fuzzy));
        Assert.Equal(0, DeviceMatcher.NextIndex(entries, Realtek.Id, devices, DeviceMatching.Fuzzy));
    }

    [Fact]
    public void CarouselSkipsMissingAndStartsAtFirstWhenDefaultIsOutside()
    {
        var entries = new[] { Speakers, Headset, Realtek }.Select(AudioDeviceEntry.From).ToList();
        AudioDevice[] devices = [Speakers, Realtek, new("{other}", "HDMI", "HDMI", "Intel", true)];

        Assert.Equal(2, DeviceMatcher.NextIndex(entries, Speakers.Id, devices, DeviceMatching.Fuzzy));
        Assert.Equal(0, DeviceMatcher.NextIndex(entries, "{other}", devices, DeviceMatching.Fuzzy));
        Assert.Equal(-1, DeviceMatcher.CurrentIndex(entries, "{other}", devices, DeviceMatching.Fuzzy));
    }

    [Fact]
    public void NothingElseToSwitchTo()
    {
        var entries = new List<AudioDeviceEntry> { AudioDeviceEntry.From(Speakers) };
        Assert.Null(DeviceMatcher.NextIndex(entries, Speakers.Id, [Speakers], DeviceMatching.Fuzzy));
    }

    [Theory]
    [InlineData("Microfone do Headset (2- JBL Quantum 800)", AudioFlow.Input, "headset")]
    [InlineData("Microphone (HD Pro Webcam C920)", AudioFlow.Input, "camera")]
    [InlineData("AC10 (NVIDIA High Definition Audio)", AudioFlow.Output, "monitor")]
    [InlineData("Alto-falantes (Realtek(R) Audio)", AudioFlow.Output, "speaker")]
    [InlineData("Microfone (Realtek(R) Audio)", AudioFlow.Input, "microphone")]
    public void GlyphGuess(string name, AudioFlow flow, string expected) =>
        Assert.Equal(expected, AudioGlyphs.Guess(name, flow));
}

public class AudioDeviceInstanceTests
{
    private sealed class FakeAudio : IAudioPlatform
    {
        public List<AudioDevice> Output { get; } =
        [
            new("spk", "AC10 (NVIDIA High Definition Audio)", "AC10", "NVIDIA High Definition Audio", true),
            new("jbl", "Fones de ouvido (JBL Quantum 800)", "Fones de ouvido", "JBL Quantum 800", true),
        ];

        public Dictionary<AudioRoles, string> Defaults { get; } = new() { [AudioRoles.Default] = "spk", [AudioRoles.Communications] = "spk" };

        public IReadOnlyList<AudioDevice> GetDevices(AudioFlow flow) => flow == AudioFlow.Output ? Output : [];
        public string? GetDefaultId(AudioFlow flow, AudioRoles roles) => Defaults[roles == AudioRoles.Communications ? AudioRoles.Communications : AudioRoles.Default];

        public void SetDefault(string deviceId, AudioRoles roles)
        {
            if (roles != AudioRoles.Communications)
                Defaults[AudioRoles.Default] = deviceId;
            if (roles != AudioRoles.Default)
                Defaults[AudioRoles.Communications] = deviceId;
            Changed?.Invoke();
        }

        public event Action? Changed;
    }

    private static TouchEvent Tap => new(TouchKind.Tap, 5, 5, TimeSpan.FromMilliseconds(60));
    private static TouchEvent Hold => new(TouchKind.LongPress, 5, 5, TimeSpan.FromMilliseconds(600), 1);

    [Fact]
    public async Task TapTogglesAndLongPressGoesBackToFirst()
    {
        var audio = new FakeAudio();
        var instance = new AudioDeviceInstance(new FakeContext(WidgetSize.Small), audio);
        foreach (var device in audio.Output)
            await instance.AddDeviceAsync(device);

        await instance.OnTouchAsync(Tap, default);
        Assert.Equal("jbl", audio.Defaults[AudioRoles.Default]);
        Assert.Equal("spk", audio.Defaults[AudioRoles.Communications]);

        await instance.OnTouchAsync(Hold, default);
        Assert.Equal("spk", audio.Defaults[AudioRoles.Default]);
    }

    [Fact]
    public async Task BothRolesSwitchTogether()
    {
        var audio = new FakeAudio();
        var instance = new AudioDeviceInstance(new FakeContext(WidgetSize.Small), audio);
        foreach (var device in audio.Output)
            await instance.AddDeviceAsync(device);
        await instance.SetRolesAsync(AudioRoles.Both);

        await instance.OnTouchAsync(Tap, default);

        Assert.Equal("jbl", audio.Defaults[AudioRoles.Default]);
        Assert.Equal("jbl", audio.Defaults[AudioRoles.Communications]);
    }

    [Fact]
    public async Task VisualShowsPositionAndName()
    {
        var audio = new FakeAudio();
        var context = new FakeContext(WidgetSize.Small);
        var instance = new AudioDeviceInstance(context, audio);
        Assert.Equal("Escolha os dispositivos", instance.CurrentVisual().Detail);

        foreach (var device in audio.Output)
            await instance.AddDeviceAsync(device);
        var visual = instance.CurrentVisual();

        Assert.Equal(2, visual.DotCount);
        Assert.Equal(0, visual.DotIndex);
        Assert.Equal("AC10", visual.Title);
        Assert.Equal(AudioGlyphs.ByKey("monitor")!.Glyph, visual.Glyph);

        var frame = await instance.RenderAsync(RenderReason.Initial, default);
        Assert.Equal(context.PixelSize, frame.Size);
    }

    [Fact]
    public async Task SettingsSurviveReload()
    {
        var audio = new FakeAudio();
        var context = new FakeContext(WidgetSize.Small);
        var instance = new AudioDeviceInstance(context, audio);
        foreach (var device in audio.Output)
            await instance.AddDeviceAsync(device);
        await instance.SetGlyphAsync(1, "headset");
        await instance.MoveAsync(1, -1);
        await instance.SetMatchingAsync(DeviceMatching.Exact);
        await instance.SetTitleAsync("Som");

        var reloaded = new AudioDeviceInstance(context, audio);
        await reloaded.LoadSettingsAsync(default);

        Assert.Equal(new[] { "jbl", "spk" }, reloaded.Entries.Select(e => e.Id));
        Assert.Equal("headset", reloaded.Entries[0].Glyph);
        Assert.Equal(DeviceMatching.Exact, reloaded.Matching);
        Assert.Equal("Som", reloaded.Title);
    }

    [Fact]
    public async Task ManifestIsValidAndPreviewRenders()
    {
        var widget = new AudioDeviceWidget();
        Assert.Empty(widget.Manifest.Validate());
        foreach (var size in widget.Manifest.SupportedSizes)
        {
            var pixels = TileFrame.Default.ContentSize(size.CellSpan);
            Assert.Equal(pixels, (await widget.GetPreviewAsync(size, pixels, default))!.Size);
        }
    }
}
