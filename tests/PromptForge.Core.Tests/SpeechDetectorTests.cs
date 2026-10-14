using PromptForge.Core.Voice;

namespace PromptForge.Core.Tests;

public class SpeechDetectorTests
{
    private const int Rate = 16000;

    /// <summary>16-bit PCM of a sine wave at the given level (dBFS), optionally after a stretch of silence.</summary>
    private static byte[] Tone(double seconds, double dbfs, double leadingSilenceSeconds = 0)
    {
        var lead = (int)(leadingSilenceSeconds * Rate);
        var count = lead + (int)(seconds * Rate);
        var amplitude = 32767 * Math.Pow(10, dbfs / 20) * Math.Sqrt(2); // RMS of a sine is peak / sqrt(2)
        var bytes = new byte[count * 2];
        for (var i = lead; i < count; i++)
        {
            var sample = (short)Math.Clamp(amplitude * Math.Sin(2 * Math.PI * 220 * i / Rate), short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)sample;
            bytes[i * 2 + 1] = (byte)(sample >> 8);
        }
        return bytes;
    }

    [Fact]
    public void All_zero_audio_is_muted()
    {
        Assert.Equal(AudioLevel.Muted, SpeechDetector.Analyze(new byte[Rate * 2 * 3], Rate));
    }

    [Fact]
    public void Empty_audio_is_muted()
    {
        Assert.Equal(AudioLevel.Muted, SpeechDetector.Analyze([], Rate));
    }

    [Fact]
    public void Quiet_background_noise_is_silent_not_speech()
    {
        Assert.Equal(AudioLevel.Silent, SpeechDetector.Analyze(Tone(3, dbfs: -65), Rate));
    }

    [Fact]
    public void A_short_click_is_not_enough()
    {
        // 100 ms of sound after 2 s of nothing: below the 200 ms minimum.
        Assert.Equal(AudioLevel.Silent, SpeechDetector.Analyze(Tone(0.1, dbfs: -20, leadingSilenceSeconds: 2), Rate));
    }

    [Theory]
    [InlineData(-20)] // normal speaking level
    [InlineData(-40)] // quiet speaker or distant mic
    public void Sound_at_speaking_levels_is_kept(double dbfs)
    {
        Assert.Equal(AudioLevel.HasSound, SpeechDetector.Analyze(Tone(1, dbfs), Rate));
    }

    [Fact]
    public void Speech_after_a_pause_is_kept()
    {
        Assert.Equal(AudioLevel.HasSound, SpeechDetector.Analyze(Tone(0.5, dbfs: -25, leadingSilenceSeconds: 2), Rate));
    }
}
