using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PromptForge.Core.Eval;

/// <summary>One run of one case, as written to results.json. An errored run never got a result, so it's neither a pass nor a fail.</summary>
public sealed record CaseRun(string Id, bool Passed, bool Errored = false);

/// <summary>
/// Compares two quality-check runs case by case. Single runs at temperature above zero vary, so
/// a case counts as better or worse only when it ran at least twice in both and its pass rate
/// moved by at least half (with three runs: from 0 or 1 passes to 2 or 3, or the reverse).
/// Smaller moves, and any change in a case that ran once, are reported as noise.
/// Errored runs (rate limits, network failures) are left out: a run that never got a result says
/// nothing about the rules. A case left with no clean runs on one side, or with fewer than two
/// because of errors, is "not comparable".
/// </summary>
public static class EvalComparison
{
    public const double RealChange = 0.5;

    public static IReadOnlyList<CaseRun> LoadRuns(string resultsJsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(resultsJsonPath));
        return doc.RootElement.EnumerateArray()
            .Select(e => new CaseRun(
                e.GetProperty("Id").GetString() ?? "",
                e.GetProperty("Passed").GetBoolean(),
                e.TryGetProperty("Error", out var error) && error.ValueKind == JsonValueKind.String))
            .ToList();
    }

    private readonly record struct Tally(int Passed, int Clean, int Errors)
    {
        public double Rate => Clean == 0 ? 0 : Passed / (double)Clean;
        public override string ToString() => Errors == 0 ? $"{Passed}/{Clean}" : $"{Passed}/{Clean} ({Errors} errored)";
    }

    public static string Build(IReadOnlyList<CaseRun> before, IReadOnlyList<CaseRun> after, string beforeLabel, string afterLabel)
    {
        var inv = CultureInfo.InvariantCulture;
        static Dictionary<string, Tally> Rates(IEnumerable<CaseRun> runs) =>
            runs.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => new Tally(
                g.Count(r => r.Passed && !r.Errored), g.Count(r => !r.Errored), g.Count(r => r.Errored)));
        var a = Rates(before);
        var b = Rates(after);

        static bool Comparable(Tally t) => t.Clean > 0 && (t.Errors == 0 || t.Clean >= 2);
        var rows = a.Keys.Intersect(b.Keys).Select(id =>
        {
            var delta = b[id].Rate - a[id].Rate;
            var repeated = a[id].Clean > 1 && b[id].Clean > 1;
            var change = !Comparable(a[id]) || !Comparable(b[id]) ? "not comparable"
                : delta == 0 ? "same" : !repeated ? "noise" : delta >= RealChange ? "better" : delta <= -RealChange ? "worse" : "noise";
            return (Id: id, Before: a[id], After: b[id], Delta: delta, Change: change);
        }).OrderBy(r => r.Change switch { "worse" => 0, "better" => 1, "not comparable" => 2, "noise" => 3, _ => 4 })
            .ThenBy(r => r.Delta).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();

        static string Overall(IReadOnlyList<CaseRun> runs)
        {
            var clean = runs.Count(r => !r.Errored);
            var passed = runs.Count(r => r.Passed && !r.Errored);
            return clean == 0 ? "0/0" : $"{passed}/{clean} ({100.0 * passed / clean:0}%)";
        }
        var sb = new StringBuilder();
        sb.AppendLine("# PromptForge quality check comparison");
        sb.AppendLine();
        sb.AppendLine(inv, $"Before: {beforeLabel}  ");
        sb.AppendLine(inv, $"After: {afterLabel}");
        sb.AppendLine();
        sb.AppendLine("| | Before | After |");
        sb.AppendLine("| --- | --- | --- |");
        sb.AppendLine(inv, $"| Runs passed (errored runs left out) | {Overall(before)} | {Overall(after)} |");
        sb.AppendLine(inv, $"| Errored runs | {before.Count(r => r.Errored)} | {after.Count(r => r.Errored)} |");
        sb.AppendLine();
        sb.AppendLine(inv, $"Cases compared: {rows.Count}. Better: {rows.Count(r => r.Change == "better")}. Worse: {rows.Count(r => r.Change == "worse")}. Noise: {rows.Count(r => r.Change == "noise")}. Not comparable: {rows.Count(r => r.Change == "not comparable")}.");
        if (rows.Any(r => r.Change == "not comparable"))
            sb.AppendLine("\nSome cases errored too often to compare (rate limits or network failures), so they say nothing about the rules. Re-run them.");
        var onlyBefore = a.Keys.Except(b.Keys).Order(StringComparer.Ordinal).ToList();
        var onlyAfter = b.Keys.Except(a.Keys).Order(StringComparer.Ordinal).ToList();
        if (onlyBefore.Count > 0) sb.AppendLine(inv, $"\nOnly in before: {string.Join(", ", onlyBefore)}");
        if (onlyAfter.Count > 0) sb.AppendLine(inv, $"\nOnly in after: {string.Join(", ", onlyAfter)}");
        if (rows.Any(r => r.Change != "not comparable" && (r.Before.Clean < 2 || r.After.Clean < 2)))
            sb.AppendLine("\nSome cases ran only once, so any change in them is counted as noise. Run with repeats.");
        sb.AppendLine();
        sb.AppendLine("| Case | Before | After | Change |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var r in rows.Where(r => r.Change != "same"))
            sb.AppendLine(inv, $"| {r.Id.Replace("|", "\\|")} | {r.Before} | {r.After} | {r.Change} |");
        sb.AppendLine(inv, $"\n{rows.Count(r => r.Change == "same")} cases had the same pass rate in both runs.");
        return sb.ToString();
    }
}
