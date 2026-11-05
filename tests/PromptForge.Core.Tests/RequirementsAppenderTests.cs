using PromptForge.Core.Optimization;
using PromptForge.Core.Settings;

namespace PromptForge.Core.Tests;

public class RequirementsAppenderTests
{
    private const string CodingRules = "Find the root cause, follow existing conventions, no workarounds.";

    private static readonly Dictionary<string, string> Coding = new() { ["Coding"] = CodingRules };

    private static OptimizeResult Result(string prompt, TaskType task = TaskType.Coding, Verdict verdict = Verdict.Rewritten, string original = "fix the login bug") => new()
    {
        Original = original,
        Prompt = prompt,
        TaskType = task,
        Verdict = verdict,
        Changes = ["Clarified the request"],
        Model = "test",
    };

    [Fact]
    public void Adds_the_text_word_for_word_after_a_blank_line()
    {
        var result = RequirementsAppender.Apply(Result("Fix the login bug."), Coding);
        Assert.Equal("Fix the login bug.\n\n" + CodingRules, result.Prompt);
    }

    [Fact]
    public void Only_the_matching_task_type_gets_the_text()
    {
        var result = RequirementsAppender.Apply(Result("Explain closures.", TaskType.Study), Coding);
        Assert.Equal("Explain closures.", result.Prompt);
        Assert.Equal(["Clarified the request"], result.Changes);
    }

    [Fact]
    public void An_unchanged_rewrite_gets_nothing()
    {
        var unchanged = Result("i have turned on the memory", verdict: Verdict.Unchanged, original: "i have turned on the memory");
        Assert.Same(unchanged, RequirementsAppender.Apply(unchanged, Coding));
    }

    [Fact]
    public void A_rewrite_identical_to_the_draft_gets_nothing()
    {
        var same = Result("fix the login bug", verdict: Verdict.LightEdit);
        Assert.Equal("fix the login bug", RequirementsAppender.Apply(same, Coding).Prompt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    public void Blank_requirements_add_nothing(string text)
    {
        var result = RequirementsAppender.Apply(Result("Fix the login bug."), new Dictionary<string, string> { ["Coding"] = text });
        Assert.Equal("Fix the login bug.", result.Prompt);
    }

    [Fact]
    public void No_requirements_add_nothing()
    {
        Assert.Equal("Fix the login bug.", RequirementsAppender.Apply(Result("Fix the login bug."), null).Prompt);
        Assert.Equal("Fix the login bug.", RequirementsAppender.Apply(Result("Fix the login bug."), new Dictionary<string, string>()).Prompt);
    }

    [Fact]
    public void Text_already_at_the_end_is_not_added_twice()
    {
        var once = RequirementsAppender.Apply(Result("Fix the login bug."), Coding);
        var twice = RequirementsAppender.Apply(once, Coding);
        Assert.Equal(once.Prompt, twice.Prompt);
        Assert.Single(twice.Changes, c => c.StartsWith("Added your standing requirements", StringComparison.Ordinal));
    }

    [Fact]
    public void Windows_newlines_in_the_text_become_plain_newlines_and_the_wording_is_kept()
    {
        var rules = new Dictionary<string, string> { ["Coding"] = "  Root cause first.\r\nNo workarounds.  " };
        var result = RequirementsAppender.Apply(Result("Fix the login bug."), rules);
        Assert.Equal("Fix the login bug.\n\nRoot cause first.\nNo workarounds.", result.Prompt);
    }

    [Fact]
    public void The_task_type_name_is_matched_ignoring_case()
    {
        var result = RequirementsAppender.Apply(Result("Fix the login bug."), new Dictionary<string, string> { ["coding"] = CodingRules });
        Assert.EndsWith(CodingRules, result.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_change_list_says_the_text_was_added()
    {
        var result = RequirementsAppender.Apply(Result("Fix the login bug."), Coding);
        Assert.Equal(["Clarified the request", "Added your standing requirements for coding"], result.Changes);
    }

    [Fact]
    public void Requirements_survive_saving_and_loading_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        try
        {
            SettingsStore.Save(path, new AppSettings { StandingRequirements = { ["Coding"] = "Line one\nLine two" } });
            Assert.Equal("Line one\nLine two", SettingsStore.Load(path).StandingRequirements["Coding"]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Settings_files_from_before_requirements_still_load()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{ "Version": 1, "PersonalContext": "CS student" }""");
            var loaded = SettingsStore.Load(path);
            Assert.Equal("CS student", loaded.PersonalContext);
            Assert.Empty(loaded.StandingRequirements);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
