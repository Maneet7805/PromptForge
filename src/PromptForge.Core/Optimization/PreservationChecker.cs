using System.Text.RegularExpressions;

namespace PromptForge.Core.Optimization;

/// <summary>
/// Deterministic check that exact details from the draft survived the rewrite: URLs, paths,
/// file names, inline code, quoted text, template placeholders, identifiers and numbers. The model is told to keep
/// them; this catches the cases where it didn't, without a second model call.
/// </summary>
public static partial class PreservationChecker
{
    private const string FileExtensions =
        "cs|csproj|sln|py|ipynb|js|mjs|cjs|ts|tsx|jsx|java|kt|kts|gradle|json|md|txt|csv|tsv|html|htm|css|scss|xml|xaml|yml|yaml|toml|ini|cfg|conf|env|sql|db|sqlite|c|h|cpp|hpp|cc|go|rs|rb|php|swift|dart|lua|r|m|sh|bash|ps1|psm1|bat|cmd|exe|dll|jar|war|zip|tar|gz|docx|doc|pdf|xlsx|xls|pptx|png|jpg|jpeg|gif|svg|webp|mp3|mp4|wav|log|lock|dockerfile|vue|svelte|astro";

    /// <summary>Returns the protected items from the draft that do not appear in the rewrite.</summary>
    public static IReadOnlyList<string> FindMissing(string original, string rewritten)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(rewritten);
        var normalizedRewrite = Normalize(rewritten);
        return ExtractProtected(original)
            .Where(item => !normalizedRewrite.Contains(Normalize(item), StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>Details that must appear verbatim in any faithful rewrite.</summary>
    public static IReadOnlyList<string> ExtractProtected(string text)
    {
        // Masked material is restored verbatim separately; don't double-check its contents.
        var prose = MarkerRegex().Replace(text, " ");
        var items = new List<string>();

        void AddAll(Regex regex, int group = 0, Func<string, string>? clean = null)
        {
            foreach (Match m in regex.Matches(prose))
            {
                var value = m.Groups[group].Value;
                if (clean is not null) value = clean(value);
                if (value.Length >= 2) items.Add(value);
            }
        }

        AddAll(UrlRegex(), clean: TrimTrailingPunctuation);
        AddAll(EmailRegex());
        AddAll(InlineCodeRegex(), group: 1);
        AddAll(WindowsPathRegex(), clean: TrimTrailingPunctuation);
        AddAll(UnixPathRegex(), clean: TrimTrailingPunctuation);
        AddAll(FileNameRegex());
        AddAll(DoubleQuotedRegex(), group: 1);
        AddAll(CurlyQuotedRegex(), group: 1);
        AddAll(TemplatePlaceholderRegex());
        AddAll(IdentifierRegex());
        AddAll(NumberRegex());

        // Drop items contained in a longer item already listed (e.g. a file name inside a path or URL).
        var distinct = items.Distinct(StringComparer.Ordinal).ToList();
        return distinct
            .Where(item => !distinct.Any(other => other.Length > item.Length && other.Contains(item, StringComparison.Ordinal)))
            .ToList();
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Replace('’', '\'').Replace('‘', '\'');

    private static string TrimTrailingPunctuation(string s) => s.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '\'', '"');

    [GeneratedRegex(@"\[\[BLOCK_\d+\]\]")]
    private static partial Regex MarkerRegex();

    [GeneratedRegex(@"https?://[^\s<>""'`]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\b[\w.+-]+@[\w-]+(?:\.[\w-]+)+\b")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\b[A-Za-z]:\\[^\s""'<>|*?]+")]
    private static partial Regex WindowsPathRegex();

    // Relative or absolute paths with at least one slash and a final segment, e.g. src/app/main.py, ./build, ~/notes.
    [GeneratedRegex(@"(?<![\w/:])(?:~|\.{1,2})?/?(?:[\w.-]+/)+[\w.-]+")]
    private static partial Regex UnixPathRegex();

    [GeneratedRegex(@"\b[\w-]+(?:\.[\w-]+)*\.(?:" + FileExtensions + @")\b", RegexOptions.IgnoreCase)]
    private static partial Regex FileNameRegex();

    [GeneratedRegex(@"""([^""\n]{2,100})""")]
    private static partial Regex DoubleQuotedRegex();

    [GeneratedRegex("“([^”\n]{2,100})”")]
    private static partial Regex CurlyQuotedRegex();

    // Template slots the user wrote on purpose: {{name}}, ${Topic:default}, [Brand], [target audience].
    // Square brackets need a letter then at least one more character (so citations like [1] and checkboxes
    // like [x] don't count), must not follow a word (array[i] is code, not a slot) and must not be markdown
    // link text, i.e. not followed by "(".
    [GeneratedRegex(@"\{\{\s*[A-Za-z_][\w.\- ]{0,40}\}\}|\$\{[A-Za-z_][^}\n]{0,60}\}|(?<![\[\w])\[[A-Za-z][\w \-/]{1,40}\](?![\(\]])")]
    private static partial Regex TemplatePlaceholderRegex();

    // camelCase / PascalCase with an inner capital, snake_case, SCREAMING_CASE, dotted.member.access, call()
    // Dotted names need segments of 2+ characters so "e.g" and "i.e" don't count.
    [GeneratedRegex(@"\b(?:[a-z]+[A-Z][A-Za-z0-9]*|[A-Z][a-z0-9]+[A-Z][A-Za-z0-9]*|[A-Za-z][A-Za-z0-9]*_[A-Za-z0-9_]+|[A-Za-z_]\w+(?:\.[A-Za-z_]\w+)+(?:\(\))?|[A-Za-z_]\w*\(\))")]
    private static partial Regex IdentifierRegex();

    // Numbers with two or more characters: 42, 3.11, 1,500, 10:30, 18.2.0, 2026. Single digits are allowed to become words.
    [GeneratedRegex(@"(?<![\w.])\d+(?:[.,:]\d+)*(?![\w])|(?<![\w.])\d{2,}")]
    private static partial Regex NumberRegex();
}
