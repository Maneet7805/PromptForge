using System.Text.Json;
using PromptForge.Core.Voice;

namespace PromptForge.Core.Tests;

/// <summary>#66: Whisper invents "Thank you." from sound that isn't speech; its own confidence tells them apart, not the words.</summary>
public class TranscriptFilterTests
{
    private static string Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return TranscriptFilter.Read(doc.RootElement);
    }

    [Fact]
    public void A_response_without_segments_is_returned_as_it_was()
    {
        Assert.Equal("hello world", Read("""{"text":"  hello world \n"}"""));
    }

    [Fact]
    public void A_response_without_a_text_field_is_empty()
    {
        Assert.Equal("", Read("{}"));
    }

    [Fact]
    public void A_segment_the_model_thinks_is_not_speech_and_was_unsure_of_is_dropped()
    {
        Assert.Equal("", Read("""{"text":" Thank you.","segments":[{"text":" Thank you.","no_speech_prob":0.85,"avg_logprob":-1.4}]}"""));
    }

    [Fact]
    public void A_single_word_is_dropped_the_same_way_whatever_the_word_is()
    {
        Assert.Equal("", Read("""{"text":" you","segments":[{"text":" you","no_speech_prob":0.9,"avg_logprob":-1.2}]}"""));
    }

    [Fact]
    public void Real_speech_is_kept()
    {
        Assert.Equal("Explain closures.", Read("""{"text":" Explain closures.","segments":[{"text":" Explain closures.","no_speech_prob":0.02,"avg_logprob":-0.2}]}"""));
    }

    [Fact]
    public void A_real_one_word_answer_is_kept_even_though_it_is_short()
    {
        Assert.Equal("you", Read("""{"text":" you","segments":[{"text":" you","no_speech_prob":0.05,"avg_logprob":-0.3}]}"""));
    }

    [Fact]
    public void An_invented_ending_after_real_speech_is_dropped_and_the_speech_is_kept()
    {
        var json = """
            {"text":" Explain closures. Thank you.","segments":[
              {"text":" Explain closures.","no_speech_prob":0.03,"avg_logprob":-0.25},
              {"text":" Thank you.","no_speech_prob":0.8,"avg_logprob":-1.3}]}
            """;

        Assert.Equal("Explain closures.", Read(json));
    }

    [Theory]
    [InlineData(0.9, -0.3)] // probably not speech, but confident of the words: keep
    [InlineData(0.1, -1.5)] // unsure of the words, but probably speech (mumbling): keep
    [InlineData(0.6, -1.0)] // exactly on both limits: keep
    public void A_segment_is_dropped_only_when_both_signals_agree(double noSpeech, double logProb)
    {
        var json = $$"""{"text":" hi","segments":[{"text":" hi","no_speech_prob":{{noSpeech}},"avg_logprob":{{logProb}}}]}""";

        Assert.Equal("hi", Read(json));
    }

    [Fact]
    public void A_segment_without_scores_is_kept()
    {
        Assert.Equal("hi there", Read("""{"text":" hi there","segments":[{"text":" hi there"}]}"""));
    }
}
