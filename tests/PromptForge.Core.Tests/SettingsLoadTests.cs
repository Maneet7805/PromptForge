using PromptForge.Core.Optimization;
using PromptForge.Core.Settings;

namespace PromptForge.Core.Tests;

public class SettingsLoadTests
{
    [Fact]
    public void A_missing_settings_file_gives_defaults_and_no_bad_copy()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(OptimizeMode.Standard, loaded.DefaultMode);
        Assert.False(File.Exists(path + ".bad"));
    }

    [Fact]
    public void A_settings_file_holding_only_null_gives_defaults()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "null");
            var loaded = SettingsStore.Load(path);
            AssertSectionsAreDefaults(loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Null_sections_in_the_settings_file_load_as_usable_defaults()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"primary":null,"fallback":null,"hotkeys":null,"voice":null,"standingRequirements":null}""");
            var loaded = SettingsStore.Load(path);
            AssertSectionsAreDefaults(loaded);
            Assert.False(loaded.Fallback.Enabled);
            Assert.Empty(loaded.StandingRequirements);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertSectionsAreDefaults(AppSettings loaded)
    {
        var defaults = new AppSettings();
        Assert.Equal(defaults.Primary.Provider, loaded.Primary.Provider);
        Assert.Equal(defaults.Primary.Model, loaded.Primary.Model);
        Assert.True(loaded.Primary.Enabled);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Fallback.Provider));
        Assert.False(string.IsNullOrWhiteSpace(loaded.Fallback.Model));
        Assert.Equal(defaults.Hotkeys.Optimize, loaded.Hotkeys.Optimize);
        Assert.Equal(defaults.Hotkeys.OptimizeSelection, loaded.Hotkeys.OptimizeSelection);
        Assert.Equal(defaults.Hotkeys.Voice, loaded.Hotkeys.Voice);
        Assert.Equal(defaults.Voice.Provider, loaded.Voice.Provider);
        Assert.Equal(defaults.Voice.Model, loaded.Voice.Model);
        Assert.Equal(defaults.Voice.MaxSeconds, loaded.Voice.MaxSeconds);
        Assert.Equal(defaults.Judge.Provider, loaded.Judge.Provider);
    }
}
