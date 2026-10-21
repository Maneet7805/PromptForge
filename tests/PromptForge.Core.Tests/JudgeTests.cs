using PromptForge.Core.Eval;
using PromptForge.Core.Llm;
using PromptForge.Core.Optimization;

namespace PromptForge.Core.Tests;

/// <summary>The judge grades by intent (#34): it sees what a case must cover, flags under-delivery, and can re-grade saved runs.</summary>
public class JudgeTests
{
    private static readonly LlmEndpoint Judge = new(ProviderPresets.Gemini, "https://y", "judge", "k");

    private const string Draft = "organise my drive make sure nothing breaks";

    private static EvalCase DriveCase(string id = "drive") => new()
    {
        Id = id, Category = "agent", Draft = Draft, Expect = Expectation.SpellOut,
        MustCover = ["propose a plan and wait for approval", "check the result"],
    };

    private const string UnderDeliveredVerdict = """
        {"clearer_than_original":true,"appropriate_length":true,"under_delivered":true,"under_delivered_examples":"no plan or approval step","comment":"only fixed the wording"}
        """;

    private static (EvalRunner Runner, PromptOptimizerTests.FakeClient Client) Runner(string verdict)
    {
        var client = new PromptOptimizerTests.FakeClient(_ => verdict);
        Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;
        return (new EvalRunner(new PromptOptimizer(client, NoDelay), client, NoDelay), client);
    }

    private static SavedRun Saved(string id = "drive", int run = 1, string? draft = Draft, string? output = "Organise my drive. Make sure nothing breaks.", string verdict = "Rewritten") =>
        new() { Id = id, Run = run, Draft = draft, Output = output, Verdict = verdict, TaskType = "Planning", Model = "groq/m" };

    [Fact]
    public void The_judge_reads_the_expected_depth_and_what_the_rewrite_must_cover()
    {
        var message = EvalRunner.BuildJudgeMessage(DriveCase(), "Organise my drive.");

        Assert.Contains("<expected_depth>SpellOut</expected_depth>", message);
        Assert.Contains("- propose a plan and wait for approval\n- check the result\n", message);
        Assert.EndsWith("<rewrite>\nOrganise my drive.\n</rewrite>", message);
    }

    [Fact]
    public void An_unlabelled_case_sends_only_the_draft_and_the_rewrite()
    {
        var message = EvalRunner.BuildJudgeMessage(new EvalCase { Id = "t", Category = "simple", Draft = "what is dns" }, "What is DNS?");

        Assert.Equal("<draft>\nwhat is dns\n</draft>\n\n<rewrite>\nWhat is DNS?\n</rewrite>", message);
    }

    [Fact]
    public void Under_delivery_is_read_from_the_judge_and_fails_the_run()
    {
        var verdict = EvalRunner.ParseVerdict(UnderDeliveredVerdict);

        Assert.True(verdict.UnderDelivered);
        Assert.Equal("no plan or approval step", verdict.UnderDeliveredExamples);
        Assert.Contains("judge: under-delivered (no plan or approval step)", EvalRunner.JudgeFailures(verdict));
    }

    [Fact]
    public void A_judge_reply_without_the_new_field_still_parses()
    {
        var verdict = EvalRunner.ParseVerdict("""{"clearer_than_original":true,"appropriate_length":true,"comment":"ok"}""");

        Assert.False(verdict.UnderDelivered);
        Assert.Empty(EvalRunner.JudgeFailures(verdict));
    }

    [Fact]
    public void The_instructions_allow_spelling_out_a_stated_goal()
    {
        Assert.Contains("Turning a stated goal into the steps or checks it requires is not an added request", EvalRunner.JudgeInstructions);
        Assert.Contains("\"under_delivered\"", EvalRunner.JudgeInstructions);
    }

    [Fact]
    public void Report_counts_under_delivery()
    {
        var c = DriveCase();
        var result = new OptimizeResult { Original = Draft, Prompt = "Organise my drive.", TaskType = TaskType.Planning, Verdict = Verdict.Rewritten, Model = "m" };
        var report = EvalRunner.BuildReport([new EvalCaseResult { Case = c, Result = result, Judge = EvalRunner.ParseVerdict(UnderDeliveredVerdict) }], "m");

        Assert.Contains("| Judge: under-delivered | 1/1 |", report);
        Assert.Contains("under-delivered=True no plan or approval step", report);
    }

    [Fact]
    public async Task Rejudge_grades_saved_rewrites_without_calling_the_optimiser()
    {
        var (runner, client) = Runner(UnderDeliveredVerdict);

        var result = Assert.Single(await runner.RejudgeAsync([DriveCase()], [Saved()], Judge, TimeSpan.Zero, null, CancellationToken.None));

        var call = Assert.Single(client.Calls);
        Assert.Equal("judge", call.Endpoint.Model);
        Assert.Contains("<must_cover>", call.Messages[1].Content);
        Assert.True(result.Judge!.UnderDelivered);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task Rejudge_leaves_out_runs_whose_case_is_gone_or_whose_draft_changed()
    {
        var (runner, client) = Runner(UnderDeliveredVerdict);

        var results = await runner.RejudgeAsync([DriveCase()], [Saved(id: "gone"), Saved(draft: "an older draft")], Judge, TimeSpan.Zero, null, CancellationToken.None);

        Assert.Empty(results);
        Assert.Empty(client.Calls);
        Assert.Equal(["gone (run 1): not in the cases file", "drive (run 1): the case's draft changed since this run"], runner.Skipped);
    }

    [Fact]
    public async Task Rejudge_keeps_runs_without_a_rewrite_as_errors_and_doesnt_judge_unchanged_ones()
    {
        var (runner, client) = Runner(UnderDeliveredVerdict);
        var unchanged = Saved(run: 2, output: Draft, verdict: "Unchanged");
        var errored = Saved(run: 3, output: null) with { Error = "rate limit" };

        var results = await runner.RejudgeAsync([DriveCase()], [unchanged, errored], Judge, TimeSpan.Zero, null, CancellationToken.None);

        Assert.Empty(client.Calls);
        Assert.Null(results[0].Judge);
        Assert.Equal("rate limit", results[1].Error);
    }

    [Fact]
    public void Saved_results_round_trip_and_still_feed_the_comparison()
    {
        var c = DriveCase();
        var result = new OptimizeResult
        {
            Original = Draft, Prompt = "Organise my drive.", TaskType = TaskType.Planning, Verdict = Verdict.LightEdit, Model = "groq/m",
            Questions = ["Which drive?"], Elapsed = TimeSpan.FromMilliseconds(900), PromptTokens = 10,
        };
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, SavedResults.ToJson([new EvalCaseResult { Case = c, Run = 2, Result = result }]));

            var saved = Assert.Single(SavedResults.Load(path));
            Assert.Equal((2, Draft, "Organise my drive.", "LightEdit", "Which drive?"), (saved.Run, saved.Draft, saved.Output, saved.Verdict, saved.Questions[0]));
            var back = SavedResults.ToResult(saved, Draft)!;
            Assert.Equal((TaskType.Planning, Verdict.LightEdit, 10), (back.TaskType, back.Verdict, back.PromptTokens));
            Assert.Equal(new CaseRun("drive", true), Assert.Single(EvalComparison.LoadRuns(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Grades_file_skips_the_header_and_reads_blank_as_not_graded()
    {
        var grades = JudgeCalibration.ParseGrades(["id,run,added,left_out,changed_kind", "drive,1,no,yes,", "", "dns,2,y,n,n"]);

        Assert.Equal([new OwnerGrade("drive", 1, false, true, null), new OwnerGrade("dns", 2, true, false, false)], grades);
    }

    [Fact]
    public void A_typo_in_the_grades_file_is_reported_with_its_line()
    {
        var ex = Assert.Throws<InvalidDataException>(() => JudgeCalibration.ParseGrades(["drive,1,maybe"]));

        Assert.Contains("line 1", ex.Message);
    }

    [Fact]
    public void Calibration_reports_agreement_and_names_the_runs_that_disagree()
    {
        JudgeVerdict V(bool added = false, bool under = false) => new(false, false, "", true, true, "", AddedRequests: added, UnderDelivered: under);
        EvalCaseResult R(string id, JudgeVerdict v) => new() { Case = DriveCase(id), Judge = v };
        var results = new[] { R("a", V(added: true)), R("b", V(under: true)), R("c", V()) };
        var grades = new[]
        {
            new OwnerGrade("a", 1, false, null, false),
            new OwnerGrade("b", 1, false, true, false),
            new OwnerGrade("c", 1, false, true, null),
            new OwnerGrade("missing", 1, true, true, true),
        };

        var report = JudgeCalibration.Build(results, grades);

        Assert.Contains("Graded runs: 4. With a judge verdict to compare: 3.", report);
        Assert.Contains("No verdict for: missing #1", report);
        Assert.Contains("| Added something the user didn't mean | 2/3 (67%) | 1: a #1 | none |", report);
        Assert.Contains("| Left something out | 1/2 (50%) | none | 1: c #1 |", report);
        Assert.Contains("| Changed the kind of message | 2/2 (100%) | none | none |", report);
        Assert.Contains("Overall agreement: 5/7 (71%).", report);
    }
}
