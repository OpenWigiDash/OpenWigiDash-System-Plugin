using OpenWigiDash.SystemPlugin.Apps;

namespace SystemPlugin.Tests;

public class AppTargetTests
{
    private static readonly Guid ProgramFilesX86 = new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E");
    private const string Discord = @"C:\Users\victor\AppData\Local\Discord";

    private static string? Folders(Guid id) => id == ProgramFilesX86 ? @"C:\Program Files (x86)" : null;

    [Fact]
    public void StoreAppUsesItsAumid()
    {
        var target = AppTarget.Build("Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App", "Xbox", null, null, null, Folders);

        Assert.True(target.IsPackaged);
        Assert.Equal("Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App", target.Aumid);
        Assert.Null(target.ExePath);
    }

    [Fact]
    public void SquirrelShortcutWatchesTheAppExeNotUpdateExe()
    {
        var target = AppTarget.Build("com.squirrel.Discord.Discord", "Discord", "com.squirrel.Discord.Discord",
            Discord + @"\Update.exe", "--processStart Discord.exe", Folders);

        Assert.False(target.IsPackaged);
        Assert.Equal("Discord", target.ProcessName);
        Assert.Equal(Discord, target.InstallDirectory);
        Assert.Equal("com.squirrel.Discord.Discord", target.Aumid);
    }

    [Fact]
    public void SquirrelArgumentInQuotes()
    {
        var target = AppTarget.Build("com.squirrel.slack.slack", "Slack", null,
            @"C:\Users\victor\AppData\Local\slack\Update.exe", "--processStart \"slack.exe\" --process-start-args \"--x\"", Folders);

        Assert.Equal("slack", target.ProcessName);
        Assert.Equal("com.squirrel.slack.slack", target.Aumid);
    }

    [Fact]
    public void KnownFolderIdBecomesAPath()
    {
        var target = AppTarget.Build(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Steam\steam.exe", "Steam", null, null, null, Folders);

        Assert.Equal(@"C:\Program Files (x86)\Steam\steam.exe", target.ExePath);
        Assert.Equal("steam", target.ProcessName);
        Assert.Null(target.Aumid);
    }

    [Fact]
    public void ShortcutTargetWinsOverTheId()
    {
        var target = AppTarget.Build(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Old\old.exe", "App", null,
            @"D:\Apps\New\new.exe", null, Folders);

        Assert.Equal(@"D:\Apps\New\new.exe", target.ExePath);
    }

    [Fact]
    public void WindowsFolderIsNotAnInstallDirectory()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var target = AppTarget.Build("notepad", "Bloco de Notas", null, Path.Combine(windows, "System32", "notepad.exe"), null, Folders);

        Assert.Null(target.InstallDirectory);
    }

    [Theory]
    [InlineData(@"C:\Program Files", false)]
    [InlineData(@"C:\Program Files\EA", true)]
    [InlineData(@"D:\Games", false)]
    [InlineData(null, false)]
    public void SpecificDirectory(string? directory, bool expected) =>
        Assert.Equal(expected, AppTarget.IsSpecificDirectory(directory));
}
