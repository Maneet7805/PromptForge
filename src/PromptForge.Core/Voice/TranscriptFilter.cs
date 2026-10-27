using System.Text.Json;

namespace PromptForge.Core.Voice;

/// <summary>
/// Removes text a speech-to-text model invented from sound that wasn't speech (a fan, a keyboard, breathing).
/// <see cref="SpeechDetector"/> stops clips with no sound at all; this catches clips that have some sound but no speech,
/// using the model's own confidence, never the words. A phrase list would also delete real answers such as "you".
/// </summary>
public static class TranscriptFilter
{
    /// <summary>Above this, the model thinks the segment is probably not speech. Whisper's own decoder uses 0.6 for this decision.</summary>
    public const double NoSpeechThreshold = 0.6;

    /// <summary>Below this average log-probability, the model was unsure of its own words. Whisper's decoder uses -1.0.</summary>
    public const double LowConfidenceLogProb = -1.0;

    /// <summary>
    /// The transcript of a <c>verbose_json</c> response. A segment is dropped only when both signals say it isn't speech;
    /// a response without segments or scores (another provider, or plain <c>json</c>) is returned exactly as it was.
    /// </summary>
    public static string Read(JsonElement root)
    {
        var text = String(root, "text").Trim();
        if (!root.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Array) return text;

        var kept = new List<string>();
        var dropped = false;
        foreach (var segment in segments.EnumerateArray())
        {
            if (IsNotSpeech(segment))
            {
                dropped = true;
                continue;
            }
            var part = String(segment, "text").Trim();
            if (part.Length > 0) kept.Add(part);
        }
        return dropped ? string.Join(" ", kept) : text;
    }

    private static bool IsNotSpeech(JsonElement segment) =>
        Number(segment, "no_speech_prob") is { } noSpeech
        && Number(segment, "avg_logprob") is { } logProb
        && noSpeech > NoSpeechThreshold
        && logProb < LowConfidenceLogProb;

    private static double? Number(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string String(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
