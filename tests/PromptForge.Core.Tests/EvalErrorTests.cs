using System.Text.Json;
using PromptForge.Core.Eval;
using PromptForge.Core.Llm;
using PromptForge.Core.Optimization;

namespace PromptForge.Core.Tests;

/// <summary>Errored runs (rate limits, missing judge verdicts) must never be counted as passes or failures (#39).</summary>
public class EvalErrorTests
{
    private static readonly string Rewrite = JsonSerializer.Serialize(new
    {
        task_type = "general", verdict = "light_edit", prompt = "What is DNS?", changes = Array.Empty<string>(), questions = Array.Empty<string>(),
    });

    private const string CleanVerdict = """{"answered_instead_of_rewriting":false,"invented_details":false,"clearer_than_original":true,"appropriate_length":true,"comment":"ok"}""";

    private static readonly LlmEndpoint Model = new(ProviderPresets.Groq, "https://x", "model", "k");
    private static readonly LlmEndpoint Judge = new(ProviderPresets.Groq, "https://y", "judge", "k");

    private static EvalCase Case(string id) => new() { Id = id, Category = "simple", Draft = "what is dns" };

    private static (EvalRunner Runner, List<TimeSpan> Waits) Runner(PromptOptimizerTests.FakeClient client)
    {
        var waits = new List<TimeSpan>();
        Task Delay(TimeSpan t, CancellationToken _) { if (t > TimeSpan.Zero) waits.Add(t); return Task.CompletedTask; }
        return (new EvalRunner(new PromptOptimizer(client, Delay), client, Delay), waits);
    }

    private static LlmException Limited(TimeSpan? retryAfter) => new(LlmErrorKind.RateLimit, "rate limit reached", retryAfter);

    [Fact]
    public void Comparison_leaves_errored_runs_out()
    {
        static IEnumerable<CaseRun> Runs(string id, int passed, int failed, int errored) =>
            Enumerable.Repeat(new CaseRun(id, true), passed)
                .Concat(Enumerable.Repeat(new CaseRun(id, false), failed))
                .Concat(Enumerable.Repeat(new CaseRun(id, false, Errored: true), errored));
        var before = Runs("ran", 3, 0, 0).Concat(Runs("never-ran", 3, 0, 0)).Concat(Runs("one-left", 3, 0, 0)).Concat(Runs("two-left", 1, 2, 0)).ToList();
        var after = Runs("ran", 3, 0, 0).Concat(Runs("never-ran", 0, 0, 3)).Concat(Runs("one-left", 0, 1, 2)).Concat(Runs("two-left", 2, 0, 1)).ToList();
        var report = EvalComparison.Build(before, after, "a", "b");

        Assert.Contains("| never-ran | 3/3 | 0/0 (3 errored) | not comparable |", report);
        Assert.Contains("| one-left | 3/3 | 0/1 (2 errored) | not comparable |", report);
        Assert.Contains("| two-left | 1/3 | 2/2 (1 errored) | better |", report);
        Assert.Contains("Worse: 0.", report);
        Assert.Contains("Not comparable: 2.", report);
        Assert.Contains("| Runs passed (errored runs left out) | 10/12 (83%) | 5/6 (83%) |", report);
        Assert.Contains("| Errored runs | 0 | 6 |", report);
    }

    [Fact]
    public async Task Runner_waits_out_a_short_rate_limit_and_retries_the_same_run()
    {
        var limited = 0;
        var client = new PromptOptimizerTests.FakeClient(_ => limited++ < 1 ? throw Limited(TimeSpan.FromSeconds(90)) : Rewrite);
        var (runner, waits) = Runner(client);
        var results = await runner.RunAsync([Case("dns")], [Model], null, TimeSpan.Zero, null, CancellationToken.None, repeat: 1);

        Assert.True(Assert.Single(results).Passed);
        Assert.Contains(TimeSpan.FromSeconds(91), waits);
        Assert.Null(runner.StoppedEarly);
    }

    [Fact]
    public async Task Runner_stops_cleanly_when_the_days_allowance_is_gone()
    {
        var calls = 0;
        var client = new PromptOptimizerTests.FakeClient(_ => ++calls > 2 ? throw Limited(TimeSpan.FromHours(3)) : Rewrite);
        var (runner, waits) = Runner(client);
        var results = await runner.RunAsync([Case("first"), Case("second"), Case("third")], [Model], null, TimeSpan.Zero, null, CancellationToken.None, repeat: 1);

        Assert.Equal(["first", "second"], results.Select(r => r.Case.Id));
        Assert.All(results, r => Assert.True(r.Passed));
        Assert.Contains("day's allowance is used up", runner.StoppedEarly);
        Assert.Contains("1 of 3 cases didn't finish", runner.StoppedEarly);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task Runner_stops_after_repeated_limits_without_a_wait_time()
    {
        var client = new PromptOptimizerTests.FakeClient(_ => throw Limited(null));
        var (runner, waits) = Runner(client);
        var results = await runner.RunAsync([Case("dns")], [Model], null, TimeSpan.Zero, null, CancellationToken.None, repeat: 1);

        Assert.Empty(results);
        Assert.Equal(EvalRunner.MaxRateLimitRetries, waits.Count(w => w == EvalRunner.DefaultRateLimitWait + TimeSpan.FromSeconds(1)));
        Assert.Contains("still rate-limited", runner.StoppedEarly);
    }

    [Fact]
    public async Task A_rate_limited_judge_is_waited_out_without_redoing_the_rewrite()
    {
        var judgeLimited = 0;
        var client = new PromptOptimizerTests.FakeClient(call => call.Endpoint.Model == "judge"
            ? judgeLimited++ < 1 ? throw Limited(TimeSpan.FromSeconds(30)) : CleanVerdict
            : Rewrite);
        var (runner, _) = Runner(client);
        var result = Assert.Single(await runner.RunAsync([Case("dns")], [Model], Judge, TimeSpan.Zero, null, CancellationToken.None, repeat: 1));

        Assert.True(result.Passed);
        Assert.NotNull(result.Judge);
        Assert.Single(client.Calls, c => c.Endpoint.Model == "model");
    }

    [Fact]
    public async Task A_run_the_judge_couldnt_grade_is_an_error_not_a_pass()
    {
        var client = new PromptOptimizerTests.FakeClient(call => call.Endpoint.Model == "judge" ? "no json here" : Rewrite);
        var (runner, _) = Runner(client);
        var result = Assert.Single(await runner.RunAsync([Case("dns")], [Model], Judge, TimeSpan.Zero, null, CancellationToken.None, repeat: 1));

        Assert.False(result.Passed);
        Assert.StartsWith("judge gave no verdict", result.Error);
        Assert.NotNull(result.Result);
    }

    [Fact]
    public void Report_pass_rate_leaves_errors_out_and_says_when_it_stopped_early()
    {
        var ok = new OptimizeResult { Original = "d", Prompt = "p", TaskType = TaskType.General, Verdict = Verdict.LightEdit, Model = "m" };
        var c = Case("dns");
        var results = new[]
        {
            new EvalCaseResult { Case = c, Run = 1, Result = ok },
            new EvalCaseResult { Case = c, Run = 2, Error = "rate limit" },
        };
        var report = EvalRunner.BuildReport(results, new EvalRunInfo("m", StoppedEarly: "Out of allowance."));

        Assert.Contains("| Runs that passed all checks (errored runs left out) | 1/1 (100%) |", report);
        Assert.Contains("| dns | simple | 1/1 (1 errored) |", report);
        Assert.Contains("**Stopped early.** Out of allowance.", report);
    }
}
