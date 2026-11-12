using System.Text;

namespace PromptForge.Core.Feedback;

/// <summary>The link to open, and what was left out of it so the app can tell the user.</summary>
/// <remarks>
/// TODO(Maneet, 13 Nov 2026): temporary, with the "Report bad rewrite" button. Replace or remove it
/// before a public release (issue #160). <see cref="SecretScanner"/> and the cutting logic can be reused.
/// </remarks>
/// <param name="TextIncluded">The draft and rewrite are in the link.</param>
/// <param name="Truncated">At least one of them was cut to fit.</param>
/// <param name="SkippedForSecret">The user asked to include text but it looked like it held a credential.</param>
public sealed record BadRewriteLink(string Url, bool TextIncluded, bool Truncated, bool SkippedForSecret)
{
    public const string IssueFormUrl = "https://github.com/Maneet7805/PromptForge/issues/new";
    public const string Template = "bad_rewrite.yml";

    // Field ids in .github/ISSUE_TEMPLATE/bad_rewrite.yml.
    public const string OriginalField = "original";
    public const string RewriteField = "rewrite";
    public const string DetailsField = "details";

    /// <summary>GitHub answers HTTP 500 above about 6,500 characters and 414 above about 9,000 (measured 13 Nov 2026).</summary>
    public const int MaxUrlLength = 6000;

    private const int MaxDetailsLength = 200;

    /// <summary>
    /// Builds the pre-filled issue link. Pass <paramref name="original"/> and <paramref name="rewrite"/>
    /// only when the user chose to include them. Nothing here is logged or stored.
    /// </summary>
    public static BadRewriteLink Build(string version, string mode, string model, string? original, string? rewrite)
    {
        var details = Clip($"{version}, {mode}, {model}", MaxDetailsLength);
        var baseUrl = $"{IssueFormUrl}?template={Template}&{DetailsField}={Escape(details)}";

        if (original is null || rewrite is null)
            return new BadRewriteLink(baseUrl, TextIncluded: false, Truncated: false, SkippedForSecret: false);

        if (SecretScanner.LooksLikeSecret(original) || SecretScanner.LooksLikeSecret(rewrite))
            return new BadRewriteLink(baseUrl, TextIncluded: false, Truncated: false, SkippedForSecret: true);

        var o = Clean(original);
        var r = Clean(rewrite);
        var room = MaxUrlLength - baseUrl.Length - $"&{OriginalField}=&{RewriteField}=".Length;
        var (oRoom, rRoom) = Split(room, Escape(o).Length, Escape(r).Length);
        var (oFit, oCut) = Fit(o, oRoom);
        var (rFit, rCut) = Fit(r, rRoom);

        var url = $"{baseUrl}&{OriginalField}={Escape(oFit)}&{RewriteField}={Escape(rFit)}";
        return new BadRewriteLink(url, TextIncluded: true, Truncated: oCut || rCut, SkippedForSecret: false);
    }

    /// <summary>Gives each field what it needs; when both are too big they get half each, and a short one frees room for the other.</summary>
    private static (int First, int Second) Split(int room, int first, int second)
    {
        if (first + second <= room) return (first, second);
        var half = room / 2;
        if (first <= half) return (first, room - first);
        if (second <= half) return (room - second, second);
        return (half, room - half);
    }

    /// <summary>Largest start of <paramref name="text"/> that, with a cut note, encodes to at most <paramref name="room"/> characters.</summary>
    private static (string Text, bool Cut) Fit(string text, int room)
    {
        if (Escape(text).Length <= room) return (text, false);

        string Candidate(int n)
        {
            if (n > 0 && char.IsHighSurrogate(text[n - 1])) n--;
            return text[..n] + $"\n[...cut by PromptForge: {n:N0} of {text.Length:N0} characters shown]";
        }

        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (Escape(Candidate(mid)).Length <= room) low = mid;
            else high = mid - 1;
        }
        var fitted = Candidate(low);
        return (Escape(fitted).Length <= room ? fitted : string.Empty, true);
    }

    /// <summary>Unix newlines (3 encoded characters instead of 6) and no lone surrogates, which Uri.EscapeDataString rejects.</summary>
    private static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') continue; sb.Append('\n'); }
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) sb.Append(c).Append(text[++i]);
            else if (char.IsSurrogate(c)) sb.Append('�');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Escape(string text) => Uri.EscapeDataString(text);

    private static string Clip(string text, int max) => Clean(text.Length > max ? text[..max] : text);
}
