using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PromptForge.Core.Llm;
using PromptForge.Core.Optimization;
using PromptForge.Core.Settings;

namespace PromptForge.Core.Eval;

public sealed record EvalCase
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Draft { get; init; }
    public OptimizeMode Mode { get; init; } = OptimizeMode.Standard;

    /// <summary>Text that must appear verbatim in the output (beyond the automatic checks).</summary>
    public IReadOnlyList<string> MustContain { get; init; } = [];

    /// <summary>Upper bound on output length / input length; checks against over-expansion.</summary>
    public double? MaxLengthRatio { get; init; }

    /// <summary>True when the draft is already good and should come back unchanged or lightly edited.</summary>
    public bool ExpectMinimalChange { get; init; }

    /// <summary>Run this case several times to measure consistency.</summary>
    public int Repeat { get; init; } = 1;
}

public sealed record JudgeVerdict(bool AnsweredInsteadOfRewriting, bool InventedDetails, string InventedExamples, bool ClearerThanOriginal, bool AppropriateLength, string Comment);

public sealed record EvalCaseResult
{
    public required EvalCase Case { get; init; }
    public int Run { get; init; } = 1;
    public OptimizeResult? Result { get; init; }
    public string? Error { get; init; }
    public List<string> Failures { get; init; } = [];
    public JudgeVerdict? Judge { get; init; }
    public bool Passed => Error is null && Failures.Count == 0;
}

/// <summary>
/// Runs the optimiser over a fixed set of drafts and grades the results: code-graded checks
/// (dropped details, leftover markers, placeholders, filler, over-expansion) plus an optional
/// LLM judge for things code can't see (did it answer instead of rewrite? invent facts?).
/// </summary>
public sealed partial class EvalRunner(PromptOptimizer optimizer, ILlmClient client)
{
    public static IReadOnlyList<EvalCase> LoadCases(string path) =>
        JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(path), SettingsStore.JsonOptions)
        ?? throw new InvalidDataException("No eval cases found.");

    public async Task<IReadOnlyList<EvalCaseResult>> RunAsync(
        IReadOnlyList<EvalCase> cases,
        IReadOnlyList<LlmEndpoint> endpoints,
        LlmEndpoint? judge,
        TimeSpan pause,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var results = new List<EvalCaseResult>();
        foreach (var evalCase in cases)
        {
            for (var run = 1; run <= Math.Max(1, evalCase.Repeat); run++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"{evalCase.Id} (run {run})");
                EvalCaseResult caseResult;
                try
                {
                    var result = await optimizer.OptimizeAsync(
                        new OptimizeRequest { Text = evalCase.Draft, Mode = evalCase.Mode }, endpoints, new OptimizerOptions(), ct);
                    var failures = Grade(evalCase, result);
                    JudgeVerdict? verdict = null;
                    if (judge is not null && !result.IsUnchanged)
                    {
                        await Task.Delay(pause / 2, ct);
                        verdict = await JudgeAsync(judge, evalCase.Draft, result.Prompt, ct);
                        if (verdict is not null)
                        {
                            if (verdict.AnsweredInsteadOfRewriting) failures.Add("judge: answered the draft instead of rewriting it");
                            if (verdict.InventedDetails) failures.Add($"judge: invented details ({verdict.InventedExamples})");
                        }
                    }
                    caseResult = new EvalCaseResult { Case = evalCase, Run = run, Result = result, Failures = failures, Judge = verdict };
                }
                catch (Exception ex) when (ex is OptimizeException or LlmException)
                {
                    caseResult = new EvalCaseResult { Case = evalCase, Run = run, Error = ex.Message };
                }
                results.Add(caseResult);
                await Task.Delay(pause, ct);
            }
        }
        return results;
    }

    public static List<string> Grade(EvalCase evalCase, OptimizeResult result)
    {
        var failures = new List<string>();
        var output = result.Prompt;

        var missing = PreservationChecker.FindMissing(evalCase.Draft, output);
        if (missing.Count > 0) failures.Add("dropped details: " + string.Join(", ", missing));

        foreach (var required in evalCase.MustContain)
            if (!output.Contains(required, StringComparison.Ordinal)) failures.Add($"missing required text: {required}");

        if (output.Contains("[[BLOCK_", StringComparison.Ordinal)) failures.Add("left a [[BLOCK_n]] marker in the output");

        var placeholder = PlaceholderRegex().Matches(output).FirstOrDefault(m => !evalCase.Draft.Contains(m.Value, StringComparison.OrdinalIgnoreCase));
        if (placeholder is not null) failures.Add("inserted a fill-in placeholder: " + placeholder.Value);

        var filler = FillerRegex().Match(output);
        if (filler.Success && !evalCase.Draft.Contains(filler.Value, StringComparison.OrdinalIgnoreCase))
            failures.Add($"added filler: \"{filler.Value}\"");

        var ratio = output.Length / (double)Math.Max(1, evalCase.Draft.Length);
        if (evalCase.MaxLengthRatio is { } max && ratio > max)
            failures.Add($"over-expanded: {ratio:0.0}x the draft (limit {max:0.0}x)");

        if (evalCase.ExpectMinimalChange && result.Verdict == Verdict.Rewritten && ratio > 1.4)
            failures.Add($"rewrote an already-good draft ({ratio:0.0}x length)");

        return failures;
    }

    private async Task<JudgeVerdict?> JudgeAsync(LlmEndpoint judge, string draft, string rewrite, CancellationToken ct)
    {
        const string instructions = """
            You grade a prompt-rewriting tool. It received a user's draft prompt and produced a rewrite meant to be sent to an AI assistant.
            Judge only the rewrite against the draft. Answer with one JSON object:
            {"answered_instead_of_rewriting": true/false (true if the rewrite performs or answers the task rather than being a prompt that asks for it),
             "invented_details": true/false (true if the rewrite adds specific facts, technologies, numbers, deadlines, audiences or requirements the draft did not state or clearly imply; asking the AI to state assumptions or ask questions is fine),
             "invented_examples": "the invented details, or empty",
             "clearer_than_original": true/false,
             "appropriate_length": true/false (false if a simple request was padded or a complex one was left vague),
             "comment": "one short sentence"}
            """;
        var messages = new List<ChatMessage>
        {
            new("system", instructions),
            new("user", $"<draft>\n{draft}\n</draft>\n\n<rewrite>\n{rewrite}\n</rewrite>"),
        };
        try
        {
            var response = await client.CompleteAsync(judge, messages, new ChatOptions { MaxTokens = 1500, Temperature = 0 }, ct);
            var start = response.Content.IndexOf('{');
            var end = response.Content.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            using var doc = JsonDocument.Parse(response.Content[start..(end + 1)]);
            var r = doc.RootElement;
            bool B(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
            string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            return new JudgeVerdict(B("answered_instead_of_rewriting"), B("invented_details"), S("invented_examples"),
                B("clearer_than_original"), B("appropriate_length"), S("comment"));
        }
        catch (Exception ex) when (ex is LlmException or JsonException)
        {
            return null;
        }
    }

    public static string BuildReport(IReadOnlyList<EvalCaseResult> results, string modelLabel)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var ok = results.Where(r => r.Result is not null).ToList();
        var latencies = ok.Select(r => r.Result!.Elapsed.TotalMilliseconds).OrderBy(x => x).ToList();
        double Pct(double p) => latencies.Count == 0 ? 0 : latencies[Math.Clamp((int)Math.Ceiling(p * latencies.Count) - 1, 0, latencies.Count - 1)];

        sb.AppendLine("# PromptForge optimiser eval");
        sb.AppendLine();
        sb.AppendLine(inv, $"Run {DateTime.Now:yyyy-MM-dd HH:mm} · model {modelLabel}");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine(inv, $"| Runs | {results.Count} |");
        sb.AppendLine(inv, $"| Passed all checks | {results.Count(r => r.Passed)} ({100.0 * results.Count(r => r.Passed) / Math.Max(1, results.Count):0}%) |");
        sb.AppendLine(inv, $"| Errors | {results.Count(r => r.Error is not null)} |");
        var judged = ok.Where(r => r.Judge is not null).ToList();
        if (judged.Count > 0)
        {
            sb.AppendLine(inv, $"| Judge: clearer than original | {judged.Count(r => r.Judge!.ClearerThanOriginal)}/{judged.Count} |");
            sb.AppendLine(inv, $"| Judge: appropriate length | {judged.Count(r => r.Judge!.AppropriateLength)}/{judged.Count} |");
            sb.AppendLine(inv, $"| Judge: invented details | {judged.Count(r => r.Judge!.InventedDetails)}/{judged.Count} |");
            sb.AppendLine(inv, $"| Judge: answered instead of rewriting | {judged.Count(r => r.Judge!.AnsweredInsteadOfRewriting)}/{judged.Count} |");
        }
        sb.AppendLine(inv, $"| Latency p50 / p95 | {Pct(0.5):0} ms / {Pct(0.95):0} ms |");
        sb.AppendLine(inv, $"| Tokens (prompt / cached / completion) | {ok.Sum(r => r.Result!.PromptTokens):N0} / {ok.Sum(r => r.Result!.CachedTokens):N0} / {ok.Sum(r => r.Result!.CompletionTokens):N0} |");
        sb.AppendLine(inv, $"| Mean output/input length | {(ok.Count == 0 ? 0 : ok.Average(r => r.Result!.Prompt.Length / (double)Math.Max(1, r.Case.Draft.Length))):0.0}x |");

        var repeated = ok.GroupBy(r => r.Case.Id).Where(g => g.Count() > 1).ToList();
        if (repeated.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Consistency across repeated runs");
            sb.AppendLine();
            sb.AppendLine("| Case | Task types | Verdicts | Output length range |");
            sb.AppendLine("| --- | --- | --- | --- |");
            foreach (var g in repeated)
            {
                var lengths = g.Select(r => r.Result!.Prompt.Length).ToList();
                sb.AppendLine(inv, $"| {g.Key} | {string.Join(", ", g.Select(r => r.Result!.TaskType).Distinct())} | {string.Join(", ", g.Select(r => r.Result!.Verdict).Distinct())} | {lengths.Min()}–{lengths.Max()} chars |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Cases");
        sb.AppendLine();
        sb.AppendLine("| Case | Category | Result | Task | Verdict | Length ratio | ms | Problems |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var r in results)
        {
            var res = r.Result;
            var problems = r.Error ?? string.Join("; ", r.Failures);
            sb.AppendLine(inv,
                $"| {r.Case.Id}{(r.Run > 1 ? $" #{r.Run}" : "")} | {r.Case.Category} | {(r.Passed ? "pass" : "FAIL")} | {res?.TaskType} | {res?.Verdict} | {(res is null ? "" : (res.Prompt.Length / (double)Math.Max(1, r.Case.Draft.Length)).ToString("0.0", inv))} | {res?.Elapsed.TotalMilliseconds:0} | {Escape(problems)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Outputs");
        foreach (var r in results.Where(r => r.Run == 1))
        {
            sb.AppendLine();
            sb.AppendLine(inv, $"### {r.Case.Id} ({r.Case.Category}, {r.Case.Mode})");
            sb.AppendLine();
            sb.AppendLine("**Draft**");
            sb.AppendLine();
            sb.AppendLine(Fence(r.Case.Draft));
            if (r.Result is { } res)
            {
                sb.AppendLine();
                sb.AppendLine(inv, $"**Output** ({res.TaskType}, {res.Verdict}, {res.Elapsed.TotalMilliseconds:0} ms)");
                sb.AppendLine();
                sb.AppendLine(Fence(res.Prompt));
                if (res.Questions.Count > 0) sb.AppendLine("\nQuestions: " + string.Join(" | ", res.Questions));
                if (res.Changes.Count > 0) sb.AppendLine("\nChanges: " + string.Join(" | ", res.Changes));
                if (res.Warnings.Count > 0) sb.AppendLine("\nWarnings: " + string.Join(" | ", res.Warnings));
                if (r.Judge is { } j) sb.AppendLine(inv, $"\nJudge: clearer={j.ClearerThanOriginal}, length ok={j.AppropriateLength}, invented={j.InventedDetails} {j.InventedExamples}, answered={j.AnsweredInsteadOfRewriting}. {j.Comment}");
            }
            else
            {
                sb.AppendLine("\nError: " + r.Error);
            }
            if (r.Failures.Count > 0) sb.AppendLine("\nFailed checks: " + string.Join("; ", r.Failures));
        }
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ");

    private static string Fence(string s)
    {
        var fence = s.Contains("````", StringComparison.Ordinal) ? "`````" : s.Contains("```", StringComparison.Ordinal) ? "````" : "```";
        return $"{fence}text\n{s}\n{fence}";
    }

    [GeneratedRegex(@"\[(?:insert|your|add|specify|describe|enter|fill|tbd|name of)[^\]]{0,40}\]|<(?:insert|your|add|specify)[^>]{0,40}>|\{\{\s*\w+\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"world[- ]class|you are an expert|you are a highly|best practices|step[- ]by[- ]step|\bIMPORTANT\b|\bCRITICAL\b|\bMUST\b", RegexOptions.None)]
    private static partial Regex FillerRegex();
}
