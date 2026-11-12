namespace PromptForge.Core.Voice;

/// <summary>Judges short slices of audio for speech. Behind an interface so the run logic is testable without a model.</summary>
public interface ISpeechModel
{
    /// <summary>The only sample rate the model accepts. Audio at another rate is not sent to it.</summary>
    int SampleRate { get; }

    /// <summary>The chance (0 to 1) that each consecutive slice of 16-bit little-endian mono PCM is speech.</summary>
    IReadOnlyList<float> Probabilities(ReadOnlySpan<byte> pcm16);
}
