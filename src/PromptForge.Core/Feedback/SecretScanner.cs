using System.Text.RegularExpressions;

namespace PromptForge.Core.Feedback;

/// <summary>
/// Cheap check for text that probably holds a credential. Used before any text is put in a link
/// the user will open: a false positive only means the form opens empty, a miss would publish a key.
/// </summary>
public static partial class SecretScanner
{
    public static bool LooksLikeSecret(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return KnownTokenRegex().IsMatch(text) || AssignmentRegex().IsMatch(text);
    }

    [GeneratedRegex(
        @"\b(?:sk|pk|rk)-[A-Za-z0-9_-]{16,}" +
        @"|\bgsk_[A-Za-z0-9]{16,}" +
        @"|\bgh[pousr]_[A-Za-z0-9]{20,}" +
        @"|\bgithub_pat_[A-Za-z0-9_]{20,}" +
        @"|\bAKIA[0-9A-Z]{16}\b" +
        @"|\bxox[abprs]-[A-Za-z0-9-]{10,}" +
        @"|\bAIza[0-9A-Za-z_-]{30,}" +
        @"|-----BEGIN [A-Z ]*PRIVATE KEY-----" +
        @"|\bBearer\s+[A-Za-z0-9._~+/=-]{20,}" +
        @"|\beyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")]
    private static partial Regex KnownTokenRegex();

    // "password = hunter2hunter2", "api_key: abc123...", "token":"..." — a name followed by : or = and a long value.
    [GeneratedRegex(@"\b(?:api[_-]?key|secret|token|password|passwd|pwd)\b[""']?\s*[:=]\s*[""']?[^\s""']{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex AssignmentRegex();
}
