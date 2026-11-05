using OpenWigiDash.SystemPlugin.Apps;

namespace SystemPlugin.Tests;

public class AppLaunchTests
{
    private static readonly AppChoice Rdp = new(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\mstsc.exe", "Conexão de Área de Trabalho Remota");
    private static readonly AppTarget RdpTarget =
        new(Rdp.Id, Rdp.Name, false, null, @"C:\Windows\system32\mstsc.exe", "mstsc", null);

    [Fact]
    public void WithoutArgumentsGoesThroughStartMenu()
    {
        var plan = AppLaunch.Plan(Rdp, RdpTarget, "   ");

        Assert.Equal(LaunchKind.StartMenu, plan.Kind);
        Assert.Equal(@"shell:AppsFolder\" + Rdp.Id, plan.File);
        Assert.Equal("", plan.Arguments);
    }

    [Fact]
    public void RdpOpensTheExeWithArguments()
    {
        var plan = AppLaunch.Plan(Rdp, RdpTarget, " /v:MEU-PC /f ");

        Assert.Equal(LaunchKind.Exe, plan.Kind);
        Assert.Equal(@"C:\Windows\system32\mstsc.exe", plan.File);
        Assert.Equal("/v:MEU-PC /f", plan.Arguments);
    }

    [Fact]
    public void ShortcutArgumentsComeFirst()
    {
        var target = new AppTarget("x", "Chrome", false, null, @"C:\Program Files\Google\Chrome\Application\chrome.exe", "chrome",
            null, "--profile-directory=Default");

        var plan = AppLaunch.Plan(new AppChoice("x", "Chrome"), target, "--incognito");

        Assert.Equal("--profile-directory=Default --incognito", plan.Arguments);
    }

    [Fact]
    public void SquirrelPassesArgumentsThroughProcessStartArgs()
    {
        var target = new AppTarget("com.squirrel.Discord.Discord", "Discord", false, "com.squirrel.Discord.Discord",
            @"C:\Users\victor\AppData\Local\Discord\Update.exe", "Discord", null, "--processStart Discord.exe");

        var plan = AppLaunch.Plan(new AppChoice(target.Id, target.Name), target, "--url \"discord://x\"");

        Assert.Equal("--processStart Discord.exe --process-start-args \"--url \\\"discord://x\\\"\"", plan.Arguments);
    }

    [Fact]
    public void StoreAppIsActivatedByAumid()
    {
        var id = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
        var target = new AppTarget(id, "Terminal", true, id, null, null, null);

        var plan = AppLaunch.Plan(new AppChoice(id, "Terminal"), target, "-p PowerShell");

        Assert.Equal(LaunchKind.Packaged, plan.Kind);
        Assert.Equal(id, plan.File);
        Assert.Equal("-p PowerShell", plan.Arguments);
    }

    [Fact]
    public void UnknownTargetFallsBackToStartMenuWithArguments()
    {
        var plan = AppLaunch.Plan(Rdp, null, "/v:MEU-PC");

        Assert.Equal(LaunchKind.StartMenu, plan.Kind);
        Assert.Equal("/v:MEU-PC", plan.Arguments);
    }

    [Fact]
    public void ExpandsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("OPENWIGI_TEST_RDP", @"C:\rdp");

        var plan = AppLaunch.Plan(Rdp, RdpTarget, @"""%OPENWIGI_TEST_RDP%\pc.rdp""");

        Assert.Equal(@"""C:\rdp\pc.rdp""", plan.Arguments);
    }

    [Fact]
    public void BuildKeepsShortcutArgumentsOfTheExe()
    {
        var target = AppTarget.Build("com.squirrel.Discord.Discord", "Discord", "com.squirrel.Discord.Discord",
            @"C:\Users\victor\AppData\Local\Discord\Update.exe", " --processStart Discord.exe ", _ => null);

        Assert.Equal("--processStart Discord.exe", target.LinkArguments);
    }
}
