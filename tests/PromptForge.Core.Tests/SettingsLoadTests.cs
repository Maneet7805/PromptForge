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
            Assert.Equal("groq", loaded.Primary.Provider);
            Assert.NotNull(loaded.Hotkeys);
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
            Assert.NotNull(loaded.Primary);
            Assert.NotNull(loaded.Fallback);
            Assert.False(loaded.Fallback.Enabled);
            Assert.NotNull(loaded.Hotkeys);
            Assert.NotNull(loaded.Voice);
            Assert.Empty(loaded.StandingRequirements);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
