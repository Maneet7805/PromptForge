namespace PromptForge.Core.Voice;

public enum AudioLevel
{
    /// <summary>Every sample is zero: the microphone is muted or turned off in Windows.</summary>
    Muted,

    /// <summary>The microphone works but nothing loud enough to be speech was recorded.</summary>
    Silent,

    /// <summary>Enough sound to be worth transcribing.</summary>
    HasSound,
}

/// <summary>
/// Decides whether a recording is worth sending for transcription. Speech-to-text models such as
/// Whisper invent text from silence ("Thank you.", "Thanks for watching"), so silent clips must
/// be caught before the call, not filtered after it.
/// </summary>
public static class SpeechDetector
{
    /// <summary>Analysis frame length. 20 ms is the usual frame size for voice activity checks.</summary>
    private const int FrameMs = 20;

    /// <summary>
    /// Frames quieter than this don't count as sound. Normal speech at a laptop mic is roughly
    /// -35 to -15 dBFS; a quiet room's background noise is usually below -55 dBFS.
    /// </summary>
    public const double SoundThresholdDbfs = -50;

    /// <summary>How much sound is needed before a clip counts as possibly containing speech.</summary>
    public static readonly TimeSpan MinSound = TimeSpan.FromMilliseconds(200);

    /// <summary>Classifies 16-bit little-endian mono PCM recorded at <paramref name="sampleRate"/>.</summary>
    public static AudioLevel Analyze(ReadOnlySpan<byte> pcm16, int sampleRate)
    {
        var samples = pcm16.Length / 2;
        if (samples == 0) return AudioLevel.Muted;

        var frameSamples = Math.Max(1, sampleRate * FrameMs / 1000);
        var threshold = 32768.0 * Math.Pow(10, SoundThresholdDbfs / 20);
        var anyNonZero = false;
        var loudFrames = 0;

        for (var start = 0; start < samples; start += frameSamples)
        {
            var count = Math.Min(frameSamples, samples - start);
            double sumSquares = 0;
            for (var i = 0; i < count; i++)
            {
                var offset = (start + i) * 2;
                var sample = (short)(pcm16[offset] | (pcm16[offset + 1] << 8));
                if (sample != 0) anyNonZero = true;
                sumSquares += (double)sample * sample;
            }
            if (Math.Sqrt(sumSquares / count) >= threshold) loudFrames++;
        }

        if (!anyNonZero) return AudioLevel.Muted;
        return TimeSpan.FromMilliseconds(loudFrames * FrameMs) >= MinSound ? AudioLevel.HasSound : AudioLevel.Silent;
    }
}
