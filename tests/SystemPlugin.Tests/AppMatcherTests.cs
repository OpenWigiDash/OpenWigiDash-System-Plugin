using OpenWigiDash.SystemPlugin.Apps;

namespace SystemPlugin.Tests;

public class AppMatcherTests
{
    private const string Discord = @"C:\Users\victor\AppData\Local\Discord";

    private static readonly AppTarget DiscordTarget =
        new("com.squirrel.Discord.Discord", "Discord", false, "com.squirrel.Discord.Discord", Discord + @"\Update.exe", "Discord", Discord);

    [Fact]
    public void SquirrelMatchesAnyVersionFolder()
    {
        // O bug do Stream Deck: depois de atualizar, o exe muda de app-1.0.9034 para app-1.0.9035.
        Assert.True(AppMatcher.IsMatch(DiscordTarget, new ProcessInfo(1, "Discord", Discord + @"\app-1.0.9035\Discord.exe", null)));
        Assert.True(AppMatcher.IsMatch(DiscordTarget, new ProcessInfo(2, "Discord", Discord + @"\app-1.0.9100\Discord.exe", null)));
    }

    [Fact]
    public void SquirrelIgnoresSameNameElsewhere() =>
        Assert.False(AppMatcher.IsMatch(DiscordTarget, new ProcessInfo(1, "Discord", @"C:\Outro\Discord.exe", null)));

    [Fact]
    public void SquirrelStubWithoutProcessStartMatchesVersionFolder()
    {
        // Discord atual: o atalho do Iniciar aponta para ...\Discord\Discord.exe, sem Update.exe nem argumentos.
        var target = AppTarget.Build("com.squirrel.Discord.Discord", "Discord", "com.squirrel.Discord.Discord",
            Discord + @"\Discord.exe", null, _ => null);

        Assert.True(AppMatcher.IsMatch(target, new ProcessInfo(1, "Discord", Discord + @"\app-1.0.9266\Discord.exe", null)));
        Assert.False(AppMatcher.IsMatch(target, new ProcessInfo(2, "Discord", Discord + @"\outra\Discord.exe", null)));
        Assert.False(AppMatcher.IsMatch(target, new ProcessInfo(3, "Updater", Discord + @"\app-1.0.9266\Updater.exe", null)));
    }

    [Fact]
    public void LauncherMatchesSiblingProcess()
    {
        var ea = new AppTarget("EA", "EA", false, null, @"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EALauncher.exe",
            "EALauncher", @"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop");

        Assert.True(AppMatcher.IsMatch(ea, new ProcessInfo(1, "EADesktop", @"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe", null)));
        Assert.False(AppMatcher.IsMatch(ea, new ProcessInfo(2, "Other", @"C:\Program Files\Electronic Arts\Other.exe", null)));
    }

    [Fact]
    public void SameExePathMatchesWithoutInstallDirectory()
    {
        var notepad = new AppTarget("notepad", "Notepad", false, null, @"C:\Windows\System32\notepad.exe", "notepad", null);

        Assert.True(AppMatcher.IsMatch(notepad, new ProcessInfo(1, "notepad", @"c:\windows\system32\NOTEPAD.EXE", null)));
        Assert.False(AppMatcher.IsMatch(notepad, new ProcessInfo(2, "calc", @"C:\Windows\System32\calc.exe", null)));
    }

    [Fact]
    public void PackagedMatchesByAumid()
    {
        var xbox = new AppTarget("Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App", "Xbox", true,
            "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App", null, null, null);

        Assert.True(AppMatcher.IsMatch(xbox, new ProcessInfo(1, "XboxPcApp", @"C:\Program Files\WindowsApps\x\XboxPcApp.exe",
            "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App")));
        Assert.False(AppMatcher.IsMatch(xbox, new ProcessInfo(2, "XboxPcApp", @"C:\Program Files\WindowsApps\x\XboxPcApp.exe", null)));
    }

    [Fact]
    public void SnapshotFindsWindowsByAumidAndByProcess()
    {
        var snapshot = new AppSnapshot(
            [new ProcessInfo(10, "Discord", Discord + @"\app-1.0.9035\Discord.exe", null), new ProcessInfo(20, "steam", @"C:\Steam\steam.exe", null)],
            [
                new WindowInfo(1, 10, "com.squirrel.Discord.Discord", false),
                new WindowInfo(2, 99, "com.squirrel.Discord.Discord", true),
                new WindowInfo(3, 20, null, false),
            ]);

        var state = snapshot.StateOf(DiscordTarget);

        Assert.True(state.IsRunning);
        Assert.Single(state.Processes);
        Assert.Equal(new IntPtr[] { 1, 2 }, state.Windows.Select(w => w.Handle));
        Assert.False(snapshot.StateOf(null).IsRunning);
    }
}
