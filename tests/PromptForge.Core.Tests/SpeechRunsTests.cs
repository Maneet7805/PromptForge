using PromptForge.Core.Voice;

namespace PromptForge.Core.Tests;

public class SpeechRunsTests
{
    private sealed class FakeModel(float[] probabilities, int sampleRate = 16000) : ISpeechModel
    {
        public int SampleRate => sampleRate;
        public int Calls { get; private set; }

        public IReadOnlyList<float> Probabilities(ReadOnlySpan<byte> pcm16)
        {
            Calls++;
            return probabilities;
        }
    }

    private static byte[] LoudAudio(int rate = 16000)
    {
        // A sine well above the loudness threshold, so the model is the deciding check.
        var bytes = new byte[rate * 2];
        for (var i = 0; i < rate; i++)
        {
            var sample = (short)(8000 * Math.Sin(2 * Math.PI * 220 * i / rate));
            bytes[i * 2] = (byte)sample;
            bytes[i * 2 + 1] = (byte)(sample >> 8);
        }
        return bytes;
    }

    [Fact]
    public void Nothing_above_the_threshold_is_not_speech() =>
        Assert.False(SpeechRuns.HasSustainedSpeech([0.1f, 0.4f, 0.49f, 0.2f]));

    [Fact]
    public void A_single_speech_slice_is_not_enough() =>
        Assert.False(SpeechRuns.HasSustainedSpeech([0.1f, 0.9f, 0.1f, 0.1f, 0.1f]));

    [Fact]
    public void Two_speech_slices_in_a_row_are_speech() =>
        Assert.True(SpeechRuns.HasSustainedSpeech([0.1f, 0.6f, 0.7f, 0.1f]));

    [Fact]
    public void A_run_may_bridge_one_quiet_slice() =>
        Assert.True(SpeechRuns.HasSustainedSpeech([0.8f, 0.1f, 0.8f]));

    [Fact]
    public void A_run_does_not_bridge_two_quiet_slices() =>
        Assert.False(SpeechRuns.HasSustainedSpeech([0.8f, 0.1f, 0.1f, 0.8f]));

    [Fact]
    public void The_threshold_itself_counts() =>
        Assert.True(SpeechRuns.HasSustainedSpeech([0.5f, 0.5f]));

    [Fact]
    public void No_slices_is_not_speech() =>
        Assert.False(SpeechRuns.HasSustainedSpeech([]));

    [Fact]
    public void Loud_audio_the_model_calls_noise_is_silent()
    {
        var model = new FakeModel([0.05f, 0.9f, 0.05f, 0.05f, 0.9f]);
        Assert.Equal(AudioLevel.Silent, SpeechDetector.Analyze(LoudAudio(), 16000, model));
    }

    [Fact]
    public void Loud_audio_the_model_calls_speech_is_kept()
    {
        var model = new FakeModel([0.05f, 0.9f, 0.95f, 0.8f]);
        Assert.Equal(AudioLevel.HasSound, SpeechDetector.Analyze(LoudAudio(), 16000, model));
    }

    [Fact]
    public void Quiet_audio_the_model_calls_speech_is_kept()
    {
        // Below the loudness threshold, so loudness alone would drop it: a whispered word.
        var quiet = new byte[32000];
        for (var i = 0; i < 16000; i++)
        {
            var sample = (short)(40 * Math.Sin(2 * Math.PI * 220 * i / 16000));
            quiet[i * 2] = (byte)sample;
            quiet[i * 2 + 1] = (byte)(sample >> 8);
        }
        Assert.Equal(AudioLevel.Silent, SpeechDetector.Analyze(quiet, 16000));
        Assert.Equal(AudioLevel.HasSound, SpeechDetector.Analyze(quiet, 16000, new FakeModel([0.9f, 0.9f])));
    }

    [Fact]
    public void Muted_audio_never_reaches_the_model()
    {
        var model = new FakeModel([0.99f, 0.99f]);
        Assert.Equal(AudioLevel.Muted, SpeechDetector.Analyze(new byte[32000], 16000, model));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public void Audio_at_another_sample_rate_keeps_the_loudness_verdict()
    {
        var model = new FakeModel([0.01f]);
        Assert.Equal(AudioLevel.HasSound, SpeechDetector.Analyze(LoudAudio(44100), 44100, model));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public void Without_a_model_the_loudness_verdict_stands() =>
        Assert.Equal(AudioLevel.HasSound, SpeechDetector.Analyze(LoudAudio(), 16000, null));

    [Fact]
    public void The_embedded_model_loads_and_hears_no_speech_in_silence_or_noise()
    {
        using var model = new SileroVad();
        var rng = new Random(7);

        var silence = new byte[16000 * 2 * 2];
        var noise = new byte[16000 * 2 * 2];
        for (var i = 0; i < noise.Length / 2; i++)
        {
            var sample = (short)(rng.Next(-300, 300));
            noise[i * 2] = (byte)sample;
            noise[i * 2 + 1] = (byte)(sample >> 8);
        }

        Assert.Equal(62, model.Probabilities(silence).Count); // 2 s of 32 ms slices
        Assert.False(SpeechRuns.HasSustainedSpeech(model.Probabilities(silence)));
        Assert.False(SpeechRuns.HasSustainedSpeech(model.Probabilities(noise)));
    }
}
