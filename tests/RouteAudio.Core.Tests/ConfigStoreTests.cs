using RouteAudio.Core.Config;
using RouteAudio.Core.Models;
using RouteAudio.Core.Rules;
using Xunit;

namespace RouteAudio.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RouteAudioTests", Guid.NewGuid().ToString("n"));

    public ConfigStoreTests() => Directory.CreateDirectory(_directory);

    private string ConfigPath => Path.Combine(_directory, "config.json");

    [Fact]
    public void SavesAndLoadsPolymorphicRulesAndActions()
    {
        var store = new ConfigStore(ConfigPath);
        var profile = store.Current.ActiveProfile;

        profile.Entries.Add(new RouteEntry
        {
            App = AppKey.Create(@"C:\apps\game.exe"),
            Target = RouteTarget.ToDevice("{device-b}"),
            Mode = RouteMode.Auto,
        });

        profile.Rules.Add(new Rule
        {
            Id = "r1",
            Name = "测试规则",
            Priority = 50,
            Match = new AllNode
            {
                Children =
                [
                    new ProcessNameGlobNode { Pattern = "game*" },
                    new PathGlobNode { Pattern = @"C:\apps\**" },
                ],
            },
            Actions = [new RouteToDeviceAction { DeviceId = "{device-b}", Role = AudioRole.Multimedia }],
        });

        store.Save();

        var reloaded = new ConfigStore(ConfigPath);
        var entry = Assert.Single(reloaded.Current.ActiveProfile.Entries);
        Assert.Equal(@"{device-b}", entry.Target.DeviceId);

        var rule = Assert.Single(reloaded.Current.ActiveProfile.Rules, r => r.Id == "r1");
        var all = Assert.IsType<AllNode>(rule.Match);
        Assert.Equal(2, all.Children.Count);
        var action = Assert.IsType<RouteToDeviceAction>(Assert.Single(rule.Actions));
        Assert.Equal(AudioRole.Multimedia, action.Role);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        File.WriteAllText(ConfigPath, "{ this is not json");

        var store = new ConfigStore(ConfigPath);

        Assert.NotNull(store.Current);
        Assert.NotEmpty(store.Current.Profiles);
        Assert.NotNull(store.LastError);
    }

    [Fact]
    public void SecondSaveCreatesBackup()
    {
        var store = new ConfigStore(ConfigPath);
        store.Save();
        store.Current.DeviceAliases["{d}"] = "别名";
        store.Save();

        Assert.True(File.Exists(ConfigPath + ".bak"));
    }

    [Fact]
    public void AliasesRoundTrip()
    {
        var store = new ConfigStore(ConfigPath);
        store.Current.DeviceAliases["{d}"] = "客厅功放";
        store.Save();

        var reloaded = new ConfigStore(ConfigPath);
        Assert.Equal("客厅功放", reloaded.Current.DeviceAliases["{d}"]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // 忽略
        }
    }
}
