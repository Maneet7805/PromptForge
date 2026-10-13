using System.Text;
using System.Text.RegularExpressions;

namespace PromptForge.Core.Optimization;

/// <summary>A chunk of pasted material held back from the model and restored verbatim afterwards.</summary>
public sealed record MaskedBlock(string Marker, string Content, string Description);

public sealed record MaskResult(string Text, IReadOnlyList<MaskedBlock> Blocks)
{
    public bool HasBlocks => Blocks.Count > 0;
}

public sealed record UnmaskResult(string Text, IReadOnlyList<string> MissingMarkers);

/// <summary>
/// Replaces large pasted material (fenced code, tagged documents, runs of code or log lines)
/// with markers like [[BLOCK_1]] before the draft is sent to the model.
/// Two reasons: the material comes back byte-for-byte identical (the model can't "tidy" a stack
/// trace), and it saves tokens, which matters on free tiers capped at a few thousand tokens per minute.
/// </summary>
public static partial class BlockMasker
{
    public const int MinFencedLines = 3;
    public const int MinFencedChars = 200;
    public const int MinTaggedChars = 300;
    public const int MinTechnicalRunLines = 6;
    public const int MinTechnicalRunChars = 200;

    public static string MarkerFor(int index) => $"[[BLOCK_{index}]]";

    public static MaskResult Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Replace("\r\n", "\n");
        var spans = new List<(int Start, int Length, string Kind)>();

        foreach (Match m in FencedBlockRegex().Matches(normalized))
        {
            var body = m.Groups["body"].Value;
            var lines = body.Count(c => c == '\n') + 1;
            if (lines >= MinFencedLines || body.Length >= MinFencedChars)
            {
                var lang = m.Groups["lang"].Value.Trim();
                spans.Add((m.Index, m.Length, lang.Length > 0 ? $"code block ({lang})" : "code block"));
            }
        }

        foreach (Match m in TaggedBlockRegex().Matches(normalized))
        {
            if (Overlaps(spans, m.Index, m.Length)) continue;
            if (m.Groups["body"].Value.Length >= MinTaggedChars)
                spans.Add((m.Index, m.Length, $"<{m.Groups["tag"].Value}> section"));
        }

        foreach (var (start, length) in FindTechnicalRuns(normalized))
        {
            if (Overlaps(spans, start, length)) continue;
            spans.Add((start, length, "pasted code or log output"));
        }

        if (spans.Count == 0) return new MaskResult(normalized, []);

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var sb = new StringBuilder();
        var blocks = new List<MaskedBlock>();
        var cursor = 0;
        foreach (var (start, length, kind) in spans)
        {
            sb.Append(normalized, cursor, start - cursor);
            var content = normalized.Substring(start, length);
            var marker = MarkerFor(blocks.Count + 1);
            blocks.Add(new MaskedBlock(marker, content, Describe(kind, content)));
            sb.Append(marker);
            cursor = start + length;
        }
        sb.Append(normalized, cursor, normalized.Length - cursor);
        return new MaskResult(sb.ToString(), blocks);
    }

    /// <summary>
    /// Puts the held-back material back. Each marker is replaced once; extra copies are removed;
    /// a marker the model dropped has its material appended at the end so nothing is lost.
    /// </summary>
    public static UnmaskResult Unmask(string text, IReadOnlyList<MaskedBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (blocks.Count == 0) return new UnmaskResult(text, []);

        var result = text;
        var missing = new List<string>();
        foreach (var block in blocks)
        {
            var first = result.IndexOf(block.Marker, StringComparison.Ordinal);
            if (first < 0)
            {
                missing.Add(block.Marker);
                continue;
            }
            // Remove duplicates after the first occurrence, then substitute the first.
            var head = result[..(first + block.Marker.Length)];
            var tail = result[(first + block.Marker.Length)..].Replace(block.Marker, string.Empty, StringComparison.Ordinal);
            result = head[..first] + block.Content + tail;
        }

        foreach (var marker in missing)
        {
            var block = blocks.First(b => b.Marker == marker);
            result = result.TrimEnd() + "\n\n" + block.Content;
        }

        return new UnmaskResult(result, missing);
    }

    private static bool Overlaps(List<(int Start, int Length, string Kind)> spans, int start, int length) =>
        spans.Any(s => start < s.Start + s.Length && s.Start < start + length);

    private static string Describe(string kind, string content)
    {
        var lines = content.Split('\n');
        var firstMeaningful = lines
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith("```", StringComparison.Ordinal) && !l.StartsWith("~~~", StringComparison.Ordinal)) ?? "";
        if (firstMeaningful.Length > 80) firstMeaningful = firstMeaningful[..77] + "...";
        return $"{kind}, {lines.Length} lines, starts with: {firstMeaningful}";
    }

    /// <summary>Finds runs of consecutive lines that look like code, stack traces or logs rather than prose.</summary>
    internal static IEnumerable<(int Start, int Length)> FindTechnicalRuns(string text)
    {
        var lines = new List<(int Start, string Line)>();
        var pos = 0;
        foreach (var line in text.Split('\n'))
        {
            lines.Add((pos, line));
            pos += line.Length + 1;
        }

        var i = 0;
        while (i < lines.Count)
        {
            if (!IsTechnicalLine(lines[i].Line))
            {
                i++;
                continue;
            }

            var j = i;
            var technical = 0;
            var lastTechnical = i;
            // Allow single blank or prose-looking lines inside a run (e.g. a blank line in a stack trace).
            while (j < lines.Count)
            {
                var line = lines[j].Line;
                if (IsTechnicalLine(line))
                {
                    technical++;
                    lastTechnical = j;
                }
                else if (j - lastTechnical > 1 || !string.IsNullOrWhiteSpace(line) && LooksLikeProse(line))
                {
                    break;
                }
                j++;
            }

            var runLines = lastTechnical - i + 1;
            var start = lines[i].Start;
            var end = lines[lastTechnical].Start + lines[lastTechnical].Line.Length;
            if (runLines >= MinTechnicalRunLines && technical >= runLines * 0.7 && end - start >= MinTechnicalRunChars)
            {
                yield return (start, end - start);
            }
            i = lastTechnical + 1;
        }
    }

    internal static bool IsTechnicalLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (LooksLikeProse(line)) return false;
        if (line.StartsWith("  ", StringComparison.Ordinal) || line.StartsWith('\t')) return true;
        if (TechnicalStartRegex().IsMatch(line)) return true;
        var symbols = line.Count(c => ";{}()[]=<>|\\$_".Contains(c));
        return symbols >= 2;
    }

    private static bool LooksLikeProse(string line)
    {
        var trimmed = line.Trim();
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 6) return false;
        var symbols = trimmed.Count(c => ";{}()[]=<>|\\$_".Contains(c));
        var lettersAndSpaces = trimmed.Count(c => char.IsLetter(c) || c == ' ');
        return symbols <= 1 && lettersAndSpaces >= trimmed.Length * 0.85;
    }

    [GeneratedRegex(@"^(?<fence>```|~~~)(?<lang>[^\n]*)\n(?<body>.*?)(?:\n\k<fence>[ \t]*(?=\n|$)|\z)", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex FencedBlockRegex();

    [GeneratedRegex(@"<(?<tag>[A-Za-z][\w-]{1,30})(?:\s[^>]*)?>(?<body>.*?)</\k<tag>>", RegexOptions.Singleline)]
    private static partial Regex TaggedBlockRegex();

    [GeneratedRegex(@"^\s*(at\s+\S+[.(]|File ""|Traceback|[\w.]*(Exception|Error)\b[:\s]|#include|import\s|from\s\S+\simport|def\s|class\s|public\s|private\s|protected\s|function\s|const\s|let\s|var\s|return\b|SELECT\s|INSERT\s|UPDATE\s|\d{4}-\d{2}-\d{2}[ T]\d{2}:|\[(ERROR|WARN|INFO|DEBUG)\]|(ERROR|WARN|INFO|DEBUG)\s*[:|-]|[$>#]\s)", RegexOptions.IgnoreCase)]
    private static partial Regex TechnicalStartRegex();
}
