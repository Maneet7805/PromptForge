using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.Chunkers;

namespace PromptForge.Core.Diffing;

public enum DiffKind
{
    Equal,
    Inserted,
    Deleted,
}

public sealed record DiffSegment(DiffKind Kind, string Text);

/// <summary>Word-level diff between the original and improved prompt, for the review window.</summary>
public static partial class TextDiff
{
    public static IReadOnlyList<DiffSegment> Compute(string original, string improved)
    {
        original = original.Replace("\r\n", "\n");
        improved = improved.Replace("\r\n", "\n");
        var result = Differ.Instance.CreateDiffs(original, improved, ignoreWhiteSpace: false, ignoreCase: false, new TokenChunker());
        var oldPieces = result.PiecesOld;
        var newPieces = result.PiecesNew;
        var segments = new List<DiffSegment>();

        int a = 0, b = 0;
        foreach (var block in result.DiffBlocks)
        {
            while (a < block.DeleteStartA && b < block.InsertStartB)
            {
                Add(segments, DiffKind.Equal, oldPieces[a]);
                a++;
                b++;
            }
            for (var i = 0; i < block.DeleteCountA; i++) Add(segments, DiffKind.Deleted, oldPieces[a + i]);
            for (var i = 0; i < block.InsertCountB; i++) Add(segments, DiffKind.Inserted, newPieces[b + i]);
            a += block.DeleteCountA;
            b += block.InsertCountB;
        }
        while (a < oldPieces.Count) Add(segments, DiffKind.Equal, oldPieces[a++]);
        return segments;
    }

    /// <summary>Share of the improved text that is new, 0..1. Used to label "light edit" vs "rewrite".</summary>
    public static double ChangedFraction(IReadOnlyList<DiffSegment> segments)
    {
        var total = segments.Where(s => s.Kind != DiffKind.Deleted).Sum(s => s.Text.Length);
        if (total == 0) return 0;
        return segments.Where(s => s.Kind == DiffKind.Inserted).Sum(s => s.Text.Length) / (double)total;
    }

    private static void Add(List<DiffSegment> segments, DiffKind kind, string text)
    {
        if (segments.Count > 0 && segments[^1].Kind == kind)
            segments[^1] = segments[^1] with { Text = segments[^1].Text + text };
        else
            segments.Add(new DiffSegment(kind, text));
    }

    /// <summary>Splits into words, whitespace runs and single punctuation marks, keeping everything.</summary>
    private sealed partial class TokenChunker : IChunker
    {
        public IReadOnlyList<string> Chunk(string text) =>
            TokenRegex().Matches(text).Select(m => m.Value).ToList();

        [GeneratedRegex(@"\s+|[\p{L}\p{N}_]+|[^\s\p{L}\p{N}_]")]
        private static partial Regex TokenRegex();
    }
}
