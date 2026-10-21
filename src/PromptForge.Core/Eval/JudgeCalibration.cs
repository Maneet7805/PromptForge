using System.Globalization;
using System.Text;

namespace PromptForge.Core.Eval;

/// <summary>The owner's own grade of one run. Null means that question wasn't graded.</summary>
public sealed record OwnerGrade(string Id, int Run, bool? Added, bool? LeftOut, bool? ChangedKind);

/// <summary>
/// Checks the judge against the owner's grades (#34): for each question, how often the two agree, and which runs
/// they disagree on. The grades file is a CSV kept outside the repo:
/// <c>id,run,added,left_out,changed_kind</c>, one graded run per line, each answer yes/no (blank = not graded).
/// </summary>
public static class JudgeCalibration
{
    private static readonly string[] Header = ["id", "run", "added", "left_out", "changed_kind"];

    public static IReadOnlyList<OwnerGrade> LoadGrades(string csvPath) => ParseGrades(File.ReadAllLines(csvPath));

    public static IReadOnlyList<OwnerGrade> ParseGrades(IEnumerable<string> lines)
    {
        var grades = new List<OwnerGrade>();
        var lineNo = 0;
        foreach (var raw in lines)
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var cells = line.Split(',').Select(c => c.Trim().Trim('"')).ToArray();
            if (lineNo == 1 && cells[0].Equals(Header[0], StringComparison.OrdinalIgnoreCase)) continue;
            if (cells.Length < 2 || !int.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var run) || run < 1)
                throw new InvalidDataException($"Grades line {lineNo}: expected {string.Join(",", Header)}, with a run number of 1 or more.");
            grades.Add(new OwnerGrade(cells[0], run, Answer(cells, 2, lineNo), Answer(cells, 3, lineNo), Answer(cells, 4, lineNo)));
        }
        return grades;
    }

    private static bool? Answer(string[] cells, int index, int lineNo)
    {
        if (index >= cells.Length || cells[index].Length == 0) return null;
        return cells[index].ToLowerInvariant() switch
        {
            "yes" or "y" or "true" or "1" => true,
            "no" or "n" or "false" or "0" => false,
            _ => throw new InvalidDataException($"Grades line {lineNo}: \"{cells[index]}\" isn't yes or no."),
        };
    }

    private sealed record Question(string Name, Func<OwnerGrade, bool?> Owner, Func<JudgeVerdict, bool> Judge);

    // Each question matches the owner's grading page, which asked one question where the judge has several fields:
    // "added" took in invented details, "left out" took in under-delivery, "changed kind" took in the AI's-reply voice.
    private static readonly Question[] Questions =
    [
        new("Added something the user didn't mean", g => g.Added, j => j.AddedRequests || j.InventedDetails),
        new("Left something out", g => g.LeftOut, j => j.UnderDelivered || j.DroppedRequirements),
        new("Changed the kind of message", g => g.ChangedKind, j => j.ChangedIntent || j.WrittenAsReply),
    ];

    public static string Build(IReadOnlyList<EvalCaseResult> results, IReadOnlyList<OwnerGrade> grades)
    {
        var inv = CultureInfo.InvariantCulture;
        var verdicts = results.Where(r => r.Judge is not null).ToDictionary(r => (r.Case.Id, r.Run), r => r.Judge!);
        var matched = grades.Where(g => verdicts.ContainsKey((g.Id, g.Run))).ToList();
        var unmatched = grades.Where(g => !verdicts.ContainsKey((g.Id, g.Run))).Select(g => $"{g.Id} #{g.Run}").ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Judge calibration");
        sb.AppendLine();
        sb.AppendLine(inv, $"Graded runs: {grades.Count}. With a judge verdict to compare: {matched.Count}.");
        if (unmatched.Count > 0)
            sb.AppendLine(inv, $"\nNo verdict for: {string.Join(", ", unmatched)} (not in this run, errored, or unchanged so not judged).");
        sb.AppendLine();
        sb.AppendLine("| Question | Agreed | Judge yes, you no | Judge no, you yes |");
        sb.AppendLine("| --- | --- | --- | --- |");
        int agreedTotal = 0, gradedTotal = 0;
        foreach (var q in Questions)
        {
            var graded = matched.Where(g => q.Owner(g) is not null).ToList();
            var judgeOnly = graded.Where(g => q.Judge(verdicts[(g.Id, g.Run)]) && q.Owner(g) == false).Select(Name).ToList();
            var ownerOnly = graded.Where(g => !q.Judge(verdicts[(g.Id, g.Run)]) && q.Owner(g) == true).Select(Name).ToList();
            var agreed = graded.Count - judgeOnly.Count - ownerOnly.Count;
            agreedTotal += agreed;
            gradedTotal += graded.Count;
            sb.AppendLine(inv, $"| {q.Name} | {agreed}/{graded.Count}{Percent(agreed, graded.Count)} | {List(judgeOnly)} | {List(ownerOnly)} |");
        }
        sb.AppendLine();
        sb.AppendLine(inv, $"Overall agreement: {agreedTotal}/{gradedTotal}{Percent(agreedTotal, gradedTotal)}.");
        return sb.ToString();
    }

    private static string Name(OwnerGrade g) => $"{g.Id} #{g.Run}";

    private static string List(List<string> names) => names.Count == 0 ? "none" : $"{names.Count}: {string.Join(", ", names).Replace("|", "\\|")}";

    private static string Percent(int part, int whole) => whole == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $" ({100.0 * part / whole:0}%)");
}
