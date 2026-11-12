namespace PromptForge.Core.Voice;

/// <summary>
/// Turns per-slice speech chances into a yes or no. The numbers were chosen on recorded clips of
/// typing, clicks, breathing, a fan, silence and short spoken words (#66); a lone slice is not enough.
/// </summary>
public static class SpeechRuns
{
    /// <summary>A slice counts as speech at or above this chance.</summary>
    public const float Threshold = 0.5f;

    /// <summary>Speech slices needed in one run; with 32 ms slices, 64 ms.</summary>
    public const int MinRunSlices = 2;

    /// <summary>A run may bridge this many non-speech slices between speech slices.</summary>
    public const int MaxGapSlices = 1;

    public static bool HasSustainedSpeech(IReadOnlyList<float> probabilities)
    {
        int run = 0, gap = 0;
        foreach (var p in probabilities)
        {
            if (p >= Threshold)
            {
                run += gap + 1;
                gap = 0;
                if (run >= MinRunSlices) return true;
            }
            else if (run > 0 && ++gap > MaxGapSlices)
            {
                run = 0;
                gap = 0;
            }
        }
        return false;
    }
}
