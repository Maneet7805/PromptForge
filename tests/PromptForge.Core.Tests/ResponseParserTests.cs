using PromptForge.Core.Optimization;

namespace PromptForge.Core.Tests;

public class ResponseParserTests
{
    [Fact]
    public void Parses_a_clean_response()
    {
        var parsed = ResponseParser.Parse("""
            {"task_type":"coding","verdict":"rewritten","prompt":"Do X.","changes":["a","b"],"questions":["q1"]}
            """);
        Assert.Equal(TaskType.Coding, parsed.TaskType);
        Assert.Equal(Verdict.Rewritten, parsed.Verdict);
        Assert.Equal("Do X.", parsed.Prompt);
        Assert.Equal(["a", "b"], parsed.Changes);
        Assert.Equal(["q1"], parsed.Questions);
    }

    [Fact]
    public void Tolerates_fences_surrounding_text_and_trailing_commas()
    {
        var parsed = ResponseParser.Parse("Here you go:\n```json\n{\"task_type\":\"research\",\"verdict\":\"light_edit\",\"prompt\":\"Find X.\",}\n```");
        Assert.Equal(TaskType.Research, parsed.TaskType);
        Assert.Equal(Verdict.LightEdit, parsed.Verdict);
        Assert.Empty(parsed.Changes);
    }

    [Fact]
    public void Unchanged_verdict_may_omit_the_prompt()
    {
        var parsed = ResponseParser.Parse("{\"task_type\":\"general\",\"verdict\":\"unchanged\"}");
        Assert.Equal(Verdict.Unchanged, parsed.Verdict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("I improved your prompt!")]
    [InlineData("{\"verdict\":\"rewritten\",\"prompt\":\"\"}")]
    [InlineData("{not json}")]
    public void Rejects_unusable_responses(string content)
    {
        Assert.Throws<ResponseFormatException>(() => ResponseParser.Parse(content));
    }

    [Fact]
    public void Limits_list_lengths_and_drops_none()
    {
        var parsed = ResponseParser.Parse("{\"verdict\":\"rewritten\",\"prompt\":\"p\",\"changes\":[\"1\",\"2\",\"3\",\"4\",\"5\"],\"questions\":[\"none\"]}");
        Assert.Equal(4, parsed.Changes.Count);
        Assert.Empty(parsed.Questions);
    }

    [Theory]
    [InlineData("Light-Edit", Verdict.LightEdit)]
    [InlineData("no change", Verdict.Unchanged)]
    [InlineData("whatever", Verdict.Rewritten)]
    public void Verdict_synonyms(string value, Verdict expected) => Assert.Equal(expected, ResponseParser.ParseVerdict(value));
}
