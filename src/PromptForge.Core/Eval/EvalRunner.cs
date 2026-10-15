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

    /// <summary>True when the draft leaves important choices open, so the optimiser should ask questions.</summary>
    public bool ExpectQuestions { get; init; }

    /// <summary>The app the draft was typed in, sent as a hint the way the app does ("the Claude desktop app").</summary>
    public string? WrittenIn { get; init; }

    /// <summary>Minimum runs for this case. The run's own repeat count applies when it's higher.</summary>
    public int Repeat { get; init; } = 1;
}

public sealed record JudgeVerdict(bool AnsweredInsteadOfRewriting, bool InventedDetails, string InventedExamples, bool ClearerThanOriginal, bool AppropriateLength, string Comment,
    bool DroppedRequirements = false, string DroppedExamples = "", bool ChangedIntent = false,
    bool WrittenAsReply = false, bool AddedRequests = false, string AddedExamples = "");

/// <summary>How a run of the quality check is set up, for the report header.</summary>
public sealed record EvalRunInfo(string ModelLabel, string? JudgeLabel = null, bool JudgeIndependent = false, string? InstructionsLabel = null);

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
    /// <summary>Runs per case when the caller doesn't say: enough to tell a real change from run-to-run noise.</summary>
    public const int DefaultRepeat = 3;

    public static IReadOnlyList<EvalCase> LoadCases(string path)
    {
        var cases = JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(path), SettingsStore.JsonOptions)
            ?? throw new InvalidDataException("No eval cases found.");
        // Results are grouped by id, so two cases with one id would be reported as one.
        var duplicate = cases.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Two eval cases have the id \"{duplicate.Key}\".");
        return cases;
    }

    public async Task<IReadOnlyList<EvalCaseResult>> RunAsync(
        IReadOnlyList<EvalCase> cases,
        IReadOnlyList<LlmEndpoint> endpoints,
        LlmEndpoint? judge,
        TimeSpan pause,
        IProgress<string>? progress,
        CancellationToken ct,
        int repeat = DefaultRepeat,
        OptimizerOptions? options = null)
    {
        options ??= new OptimizerOptions();
        var results = new List<EvalCaseResult>();
        foreach (var evalCase in cases)
        {
            var runs = RunsFor(evalCase, repeat);
            for (var run = 1; run <= runs; run++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"{evalCase.Id} (run {run} of {runs})");
                EvalCaseResult caseResult;
                try
                {
                    var result = await optimizer.OptimizeAsync(
                        new OptimizeRequest { Text = evalCase.Draft, Mode = evalCase.Mode, WrittenIn = evalCase.WrittenIn }, endpoints, options, ct);
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
                            if (verdict.DroppedRequirements) failures.Add($"judge: dropped a stated requirement ({verdict.DroppedExamples})");
                            if (verdict.ChangedIntent) failures.Add("judge: changed what kind of request it is");
                            if (verdict.WrittenAsReply) failures.Add("judge: written as the AI's reply, not the user's message");
                            if (verdict.AddedRequests) failures.Add($"judge: added requests the draft didn't make ({verdict.AddedExamples})");
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

    /// <summary>Runs for one case: the run's repeat count, or the case's own minimum if higher.</summary>
    public static int RunsFor(EvalCase evalCase, int repeat) => Math.Max(1, Math.Max(evalCase.Repeat, repeat));

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

        if (evalCase.ExpectQuestions && result.Questions.Count == 0)
            failures.Add("vague draft but no questions for the user");

        var addedNumbers = NumberRegex().Matches(output).Select(m => m.Value).Distinct()
            .Where(n => !evalCase.Draft.Contains(n, StringComparison.Ordinal)).ToList();
        if (addedNumbers.Count > 0) failures.Add("added numbers not in the draft: " + string.Join(", ", addedNumbers));

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
             "dropped_requirements": true/false (true if the draft states a rule, criterion, condition, preference or item to cover, however casually, and the rewrite leaves it out or weakens it),
             "dropped_examples": "what was dropped, or empty",
             "changed_intent": true/false (true if the rewrite changes what kind of message the draft is: a question became an instruction or the reverse; a statement, report or answer became a request or a question; a standing instruction ("whenever", "from now on") became a one-off task; the requested action changed, e.g. "fix" became "explain" or "set up" became "plan"; or pasted context was turned into the task),
             "written_as_reply": true/false (true if any part of the rewrite is written as the AI's reply to the user instead of the user's own message: offering help, asking the user what they want, or asking the user to supply details),
             "added_requests": true/false (true if the rewrite asks the AI for something the draft did not ask for or clearly imply: extra deliverables, a required answer format, or steps such as "ask me for more details before answering"),
             "added_examples": "what was added, or empty",
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
                B("clearer_than_original"), B("appropriate_length"), S("comment"),
                B("dropped_requirements"), S("dropped_examples"), B("changed_intent"),
                B("written_as_reply"), B("added_requests"), S("added_examples"));
        }
        catch (Exception ex) when (ex is LlmException or JsonException)
        {
            return null;
        }
    }

    public static string BuildReport(IReadOnlyList<EvalCaseResult> results, string modelLabel) => BuildReport(results, new EvalRunInfo(modelLabel));

    public static string BuildReport(IReadOnlyList<EvalCaseResult> results, EvalRunInfo info)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var ok = results.Where(r => r.Result is not null).ToList();
        var latencies = ok.Select(r => r.Result!.Elapsed.TotalMilliseconds).OrderBy(x => x).ToList();
        double Pct(double p) => latencies.Count == 0 ? 0 : latencies[Math.Clamp((int)Math.Ceiling(p * latencies.Count) - 1, 0, latencies.Count - 1)];

        sb.AppendLine("# PromptForge optimiser eval");
        sb.AppendLine();
        sb.AppendLine(inv, $"Run {DateTime.Now:yyyy-MM-dd HH:mm} · model {info.ModelLabel}");
        if (info.InstructionsLabel is not null) sb.AppendLine(inv, $"\nSystem prompt: {info.InstructionsLabel}");
        if (info.JudgeLabel is not null)
            sb.AppendLine(inv, $"\nJudge: {info.JudgeLabel} ({(info.JudgeIndependent ? "independent provider" : "same provider as the optimiser, so not independent; set a judge in Settings → Data")})");
        sb.AppendLine();
        var byCase = CaseRates(results);
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine(inv, $"| Cases / runs | {byCase.Count} / {results.Count} |");
        sb.AppendLine(inv, $"| Runs that passed all checks | {results.Count(r => r.Passed)} ({100.0 * results.Count(r => r.Passed) / Math.Max(1, results.Count):0}%) |");
        sb.AppendLine(inv, $"| Cases that passed every run | {byCase.Count(c => c.Passed == c.Runs)} |");
        sb.AppendLine(inv, $"| Cases that failed every run | {byCase.Count(c => c.Passed == 0)} |");
        sb.AppendLine(inv, $"| Cases with mixed results | {byCase.Count(c => c.Passed > 0 && c.Passed < c.Runs)} |");
        sb.AppendLine(inv, $"| Errors | {results.Count(r => r.Error is not null)} |");
        var judged = ok.Where(r => r.Judge is not null).ToList();
        if (judged.Count > 0)
        {
            sb.AppendLine(inv, $"| Judge: clearer than original | {judged.Count(r => r.Judge!.ClearerThanOriginal)}/{judged.Count} |");
            sb.AppendLine(inv, $"| Judge: appropriate length | {judged.Count(r => r.Judge!.AppropriateLength)}/{judged.Count} |");
            sb.AppendLine(inv, $"| Judge: invented details | {judged.Count(r => r.Judge!.InventedDetails)}/{judged.Count} |");
            sb.AppendLine(inv, $"| Judge: answered instead of rewriting | {judged.Count(r => r.Judge!.AnsweredInsteadOfRewriting)}/{judged.Count} |");
            sb.AppendLine(inv, $"| Judge: dropped a stated requirement | {judged.Count(r => r.Judge!.DroppedRequirements)}/{judged.Count} |");
            sb.AppendLine(inv, $"| Judge: changed the kind of request | {judged.Count(r => r.Judge!.ChangedIntent)}/{judged.Count} |");
            sb.AppendLine(inv, $"| Judge: written as the AI's reply | {judged.Count(r => r.Judge!.WrittenAsReply)}/{judged.Count} |");
            sb.AppendLine(inv, $"| Judge: added requests | {judged.Count(r => r.Judge!.AddedRequests)}/{judged.Count} |");
        }
        sb.AppendLine(inv, $"| Latency p50 / p95 | {Pct(0.5):0} ms / {Pct(0.95):0} ms |");
        sb.AppendLine(inv, $"| Tokens (prompt / cached / completion) | {ok.Sum(r => r.Result!.PromptTokens):N0} / {ok.Sum(r => r.Result!.CachedTokens):N0} / {ok.Sum(r => r.Result!.CompletionTokens):N0} |");
        sb.AppendLine(inv, $"| Mean output/input length | {(ok.Count == 0 ? 0 : ok.Average(r => r.Result!.Prompt.Length / (double)Math.Max(1, r.Case.Draft.Length))):0.0}x |");

        sb.AppendLine();
        sb.AppendLine("## Pass rate by case");
        sb.AppendLine();
        sb.AppendLine("Lowest first. With repeated runs, a change between two versions is real only if it shows in most runs of a case, not one.");
        sb.AppendLine();
        sb.AppendLine("| Case | Category | Passed | Most common problem |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var c in byCase.OrderBy(c => c.Rate).ThenBy(c => c.Id, StringComparer.Ordinal))
            sb.AppendLine(inv, $"| {Escape(c.Id)} | {Escape(c.Category)} | {c.Passed}/{c.Runs} | {Escape(c.TopProblem ?? "")} |");

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
                $"| {Escape(r.Case.Id)}{(r.Run > 1 ? $" #{r.Run}" : "")} | {Escape(r.Case.Category)} | {(r.Passed ? "pass" : "FAIL")} | {res?.TaskType} | {res?.Verdict} | {(res is null ? "" : (res.Prompt.Length / (double)Math.Max(1, r.Case.Draft.Length)).ToString("0.0", inv))} | {res?.Elapsed.TotalMilliseconds:0} | {Escape(problems)} |");
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
                if (r.Judge is { } j) sb.AppendLine(inv, $"\nJudge: clearer={j.ClearerThanOriginal}, length ok={j.AppropriateLength}, invented={j.InventedDetails} {j.InventedExamples}, answered={j.AnsweredInsteadOfRewriting}, dropped={j.DroppedRequirements} {j.DroppedExamples}, changed intent={j.ChangedIntent}, reply voice={j.WrittenAsReply}, added={j.AddedRequests} {j.AddedExamples}. {j.Comment}");
            }
            else
            {
                sb.AppendLine("\nError: " + r.Error);
            }
            if (r.Failures.Count > 0) sb.AppendLine("\nFailed checks: " + string.Join("; ", r.Failures));
        }
        return sb.ToString();
    }

    public sealed record CaseRate(string Id, string Category, int Passed, int Runs, string? TopProblem)
    {
        public double Rate => Runs == 0 ? 0 : Passed / (double)Runs;
    }

    /// <summary>Pass rate per case across its runs, with the failure seen most often.</summary>
    public static IReadOnlyList<CaseRate> CaseRates(IReadOnlyList<EvalCaseResult> results) =>
        results.GroupBy(r => r.Case.Id).Select(g => new CaseRate(
            g.Key,
            g.First().Case.Category,
            g.Count(r => r.Passed),
            g.Count(),
            g.SelectMany(r => r.Error is null ? r.Failures.Select(ProblemKind) : ["error"])
                .GroupBy(p => p).OrderByDescending(p => p.Count()).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key).FirstOrDefault()))
            .ToList();

    // "judge: invented details (Python 3.12)" and "judge: invented details (React)" are the same kind of problem.
    private static string ProblemKind(string failure)
    {
        var cut = failure.IndexOfAny(['(', ':'], failure.StartsWith("judge:", StringComparison.Ordinal) ? 6 : 0);
        return (cut > 0 ? failure[..cut] : failure).Trim();
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ");

    private static string Fence(string s)
    {
        var fence = s.Contains("````", StringComparison.Ordinal) ? "`````" : s.Contains("```", StringComparison.Ordinal) ? "````" : "```";
        return $"{fence}text\n{s}\n{fence}";
    }

    [GeneratedRegex(@"\[(?:insert|your|add|specify|describe|enter|fill|tbd|name of)[^\]]{0,40}\]|<(?:insert|your|add|specify)[^>]{0,40}>|\{\{\s*\w+\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"(?<![\w.])\d+(?:\.\d+)?(?![\w.])")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"world[- ]class|you are an expert|you are a highly|best practices|step[- ]by[- ]step|\bIMPORTANT\b|\bCRITICAL\b|\bMUST\b", RegexOptions.None)]
    private static partial Regex FillerRegex();

}
