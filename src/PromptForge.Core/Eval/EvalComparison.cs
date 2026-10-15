using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PromptForge.Core.Eval;

/// <summary>One run of one case, as written to results.json.</summary>
public sealed record CaseRun(string Id, bool Passed);

/// <summary>
/// Compares two quality-check runs case by case. Single runs at temperature above zero vary, so
/// a case counts as better or worse only when it ran at least twice in both and its pass rate
/// moved by at least half (with three runs: from 0 or 1 passes to 2 or 3, or the reverse).
/// Smaller moves, and any change in a case that ran once, are reported as noise.
/// </summary>
public static class EvalComparison
{
    public const double RealChange = 0.5;

    public static IReadOnlyList<CaseRun> LoadRuns(string resultsJsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(resultsJsonPath));
        return doc.RootElement.EnumerateArray()
            .Select(e => new CaseRun(e.GetProperty("Id").GetString() ?? "", e.GetProperty("Passed").GetBoolean()))
            .ToList();
    }

    public static string Build(IReadOnlyList<CaseRun> before, IReadOnlyList<CaseRun> after, string beforeLabel, string afterLabel)
    {
        var inv = CultureInfo.InvariantCulture;
        static Dictionary<string, (int Passed, int Runs)> Rates(IEnumerable<CaseRun> runs) =>
            runs.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => (g.Count(r => r.Passed), g.Count()));
        var a = Rates(before);
        var b = Rates(after);
        static string Fmt((int Passed, int Runs) r) => $"{r.Passed}/{r.Runs}";
        static double Rate((int Passed, int Runs) r) => r.Runs == 0 ? 0 : r.Passed / (double)r.Runs;

        var rows = a.Keys.Intersect(b.Keys).Select(id =>
        {
            var delta = Rate(b[id]) - Rate(a[id]);
            var repeated = a[id].Runs > 1 && b[id].Runs > 1;
            var change = delta == 0 ? "same" : !repeated ? "noise" : delta >= RealChange ? "better" : delta <= -RealChange ? "worse" : "noise";
            return (Id: id, Before: a[id], After: b[id], Delta: delta, Change: change);
        }).OrderBy(r => r.Change switch { "worse" => 0, "better" => 1, "noise" => 2, _ => 3 }).ThenBy(r => r.Delta).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();

        static double Overall(IReadOnlyList<CaseRun> runs) => runs.Count == 0 ? 0 : 100.0 * runs.Count(r => r.Passed) / runs.Count;
        var sb = new StringBuilder();
        sb.AppendLine("# PromptForge quality check comparison");
        sb.AppendLine();
        sb.AppendLine(inv, $"Before: {beforeLabel}  ");
        sb.AppendLine(inv, $"After: {afterLabel}");
        sb.AppendLine();
        sb.AppendLine("| | Before | After |");
        sb.AppendLine("| --- | --- | --- |");
        sb.AppendLine(inv, $"| Runs passed | {before.Count(r => r.Passed)}/{before.Count} ({Overall(before):0}%) | {after.Count(r => r.Passed)}/{after.Count} ({Overall(after):0}%) |");
        sb.AppendLine();
        sb.AppendLine(inv, $"Cases compared: {rows.Count}. Better: {rows.Count(r => r.Change == "better")}. Worse: {rows.Count(r => r.Change == "worse")}. Noise: {rows.Count(r => r.Change == "noise")}.");
        var onlyBefore = a.Keys.Except(b.Keys).Order(StringComparer.Ordinal).ToList();
        var onlyAfter = b.Keys.Except(a.Keys).Order(StringComparer.Ordinal).ToList();
        if (onlyBefore.Count > 0) sb.AppendLine(inv, $"\nOnly in before: {string.Join(", ", onlyBefore)}");
        if (onlyAfter.Count > 0) sb.AppendLine(inv, $"\nOnly in after: {string.Join(", ", onlyAfter)}");
        if (rows.Any(r => r.Before.Runs < 2 || r.After.Runs < 2))
            sb.AppendLine("\nSome cases ran only once, so any change in them is counted as noise. Run with repeats.");
        sb.AppendLine();
        sb.AppendLine("| Case | Before | After | Change |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var r in rows.Where(r => r.Change != "same"))
            sb.AppendLine(inv, $"| {r.Id.Replace("|", "\\|")} | {Fmt(r.Before)} | {Fmt(r.After)} | {r.Change} |");
        sb.AppendLine(inv, $"\n{rows.Count(r => r.Change == "same")} cases had the same pass rate in both runs.");
        return sb.ToString();
    }
}
