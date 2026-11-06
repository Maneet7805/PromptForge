using System.Text.RegularExpressions;

namespace PromptForge.Core.Optimization;

/// <summary>
/// The repair step is asked to put dropped details back. A model sometimes just appends the bare token (a lone number
/// on its own line, or stuck on after a full stop), which satisfies the preservation check but reads as a stray
/// fragment. This finds such repairs so they can be rejected.
/// </summary>
internal static partial class RepairFragments
{
    /// <summary>A restored detail needs at least this many other words in its sentence or line to count as placed.</summary>
    private const int MinWordsAroundDetail = 2;

    /// <summary>
    /// True when some detail from <paramref name="restored"/> appears in <paramref name="repaired"/> only as a bare
    /// fragment: every place it occurs has fewer than two other words in its sentence or line.
    /// </summary>
    public static bool HasBareDetail(string repaired, IEnumerable<string> restored)
    {
        var segments = SegmentBoundary().Split(repaired);
        foreach (var detail in restored)
        {
            var places = segments.Where(s => s.Contains(detail, StringComparison.OrdinalIgnoreCase)).ToList();
            if (places.Count == 0) continue; // not restored by this repair, nothing to judge
            if (places.All(s => WordsOtherThan(s, detail) < MinWordsAroundDetail))
                return true;
        }
        return false;
    }

    private static int WordsOtherThan(string segment, string detail)
    {
        var rest = segment.Replace(detail, " ", StringComparison.OrdinalIgnoreCase);
        return WordRegex().Matches(rest).Count;
    }

    // A new sentence (after . ! ?) or a new line.
    [GeneratedRegex(@"(?<=[.!?])\s+|\r?\n")]
    private static partial Regex SegmentBoundary();

    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex WordRegex();
}
