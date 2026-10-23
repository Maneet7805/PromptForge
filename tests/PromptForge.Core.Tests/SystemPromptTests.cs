using System.Text.RegularExpressions;
using PromptForge.Core.Eval;
using PromptForge.Core.Optimization;

namespace PromptForge.Core.Tests;

/// <summary>The rules follow docs/design/rewrite-intent.md (#35): what they must say, and what the old ones said that they must not.</summary>
public partial class SystemPromptTests
{
    private static string Text => SystemPrompt.Text;

    [Fact]
    public void States_the_principle_and_the_test_for_every_added_line()
    {
        Assert.Contains("write out what the user clearly meant, and never add what they didn't", Text);
        Assert.Contains("must serve a goal the draft states, and must change the result", Text);
    }

    [Fact]
    public void Asks_the_seven_questions_in_order()
    {
        var positions = Enumerable.Range(1, 7).Select(n => Text.IndexOf($"\n{n}. ", StringComparison.Ordinal)).ToList();

        Assert.All(positions, p => Assert.True(p > 0));
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public void No_longer_adds_things_by_task_type()
    {
        Assert.DoesNotContain("What usually helps, by task type", Text);
        Assert.DoesNotContain("ask for the likely root cause before the fix", Text);
        Assert.DoesNotContain("ask for reliable, cited sources", Text);
    }

    [Fact]
    public void Never_teaches_an_ask_me_first_fallback()
    {
        // D8: these lines are padding. The old JWT example ended with one, and the old rules told the model to add them.
        Assert.DoesNotContain("ask me for them before guessing", Text);
        Assert.DoesNotContain("the rewrite asks the AI to request them", Text);
        Assert.DoesNotContain("ask the AI to state its assumption or to ask before starting", Text);
        Assert.Contains("Never add a fallback line the user didn't write", Text);
    }

    [Fact]
    public void A_destructive_draft_without_a_safety_goal_gets_no_safety_steps()
    {
        // D3: the user asked for the action; confirmations and "list first" steps are the user's to ask for.
        Assert.Contains("add no safety steps, confirmations or \"list them first\" requests, however destructive the action is", Text);
    }

    [Fact]
    public void An_open_ended_draft_gets_suggestions_not_a_role_line_or_requirements()
    {
        Assert.Contains("Do not add a role line (\"act as my tutor\"), a \"Requirements\" list", Text);
    }

    [Fact]
    public void Proposals_are_labelled_so_they_stay_the_users_to_remove()
    {
        Assert.Contains("Suggested approach (keep, change or remove any of these):", Text);
    }

    [Fact]
    public void Light_mode_answers_only_the_questions_that_dont_add()
    {
        Assert.Contains("light: wording only", Text);
        Assert.Contains("Answer questions 1, 2, 5 and 7 only.", Text);
    }

    [Fact]
    public void Examples_parse_as_the_json_the_optimiser_expects()
    {
        var examples = ExampleRegex().Matches(Text).ToList();

        Assert.True(examples.Count >= 7);
        foreach (var example in examples)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(example.Groups["json"].Value);
            Assert.True(doc.RootElement.TryGetProperty("verdict", out _));
            Assert.True(doc.RootElement.TryGetProperty("questions", out _));
        }
    }

    [Fact]
    public void No_example_is_a_draft_in_the_test_set()
    {
        // An example the rules show is an answer the quality check would only be remembering, not measuring.
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "eval", "cases.json"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var drafts = EvalRunner.LoadCases(Path.Combine(dir!, "eval", "cases.json")).Select(c => Normalise(c.Draft)).ToHashSet();
        var shown = ExampleRegex().Matches(Text).Select(m => Normalise(m.Groups["draft"].Value)).ToList();

        Assert.NotEmpty(shown);
        Assert.DoesNotContain(shown, drafts.Contains);
    }

    private static string Normalise(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();

    [GeneratedRegex(@"^Draft: ""(?<draft>.*)""\n(?<json>\{.*\})$", RegexOptions.Multiline)]
    private static partial Regex ExampleRegex();
}
