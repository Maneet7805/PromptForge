using System.Text.Json;
using PromptForge.Core.Optimization;

namespace PromptForge.Core.Eval;

/// <summary>One run as saved in results.json: enough to grade its rewrite again without calling the optimiser.</summary>
public sealed record SavedRun
{
    public required string Id { get; init; }
    public int Run { get; init; } = 1;

    /// <summary>The draft the run rewrote. Null in files saved before it was recorded.</summary>
    public string? Draft { get; init; }

    public string? Error { get; init; }
    public string? Output { get; init; }
    public string? Model { get; init; }
    public string? TaskType { get; init; }
    public string? Verdict { get; init; }
    public IReadOnlyList<string> Questions { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public double Ms { get; init; }
    public int PromptTokens { get; init; }
    public int CachedTokens { get; init; }
    public int CompletionTokens { get; init; }
}

/// <summary>Writes and reads results.json, so the quality check, the comparison and the re-judge agree on its format.</summary>
public static class SavedResults
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string ToJson(IReadOnlyList<EvalCaseResult> results) => JsonSerializer.Serialize(results.Select(r => new
    {
        r.Case.Id,
        r.Case.Category,
        r.Run,
        r.Passed,
        r.Error,
        r.Failures,
        r.Case.Draft,
        Output = r.Result?.Prompt,
        Model = r.Result?.Model,
        TaskType = r.Result?.TaskType.ToString(),
        Verdict = r.Result?.Verdict.ToString(),
        r.Result?.Questions,
        r.Result?.Warnings,
        Ms = r.Result?.Elapsed.TotalMilliseconds,
        r.Result?.PromptTokens,
        r.Result?.CachedTokens,
        r.Result?.CompletionTokens,
        r.Judge,
    }), Indented);

    public static IReadOnlyList<SavedRun> Load(string resultsJsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(resultsJsonPath));
        return doc.RootElement.EnumerateArray().Select(e => new SavedRun
        {
            Id = e.GetProperty("Id").GetString() ?? "",
            Run = Int(e, "Run") is > 0 and var run ? run : 1,
            Draft = Str(e, "Draft"),
            Error = Str(e, "Error"),
            Output = Str(e, "Output"),
            Model = Str(e, "Model"),
            TaskType = Str(e, "TaskType"),
            Verdict = Str(e, "Verdict"),
            Questions = Strings(e, "Questions"),
            Warnings = Strings(e, "Warnings"),
            Ms = e.TryGetProperty("Ms", out var ms) && ms.ValueKind == JsonValueKind.Number ? ms.GetDouble() : 0,
            PromptTokens = Int(e, "PromptTokens"),
            CachedTokens = Int(e, "CachedTokens"),
            CompletionTokens = Int(e, "CompletionTokens"),
        }).ToList();
    }

    /// <summary>The saved rewrite as an optimiser result, or null when the run never got one.</summary>
    public static OptimizeResult? ToResult(SavedRun run, string draft) => run.Output is null ? null : new OptimizeResult
    {
        Original = draft,
        Prompt = run.Output,
        TaskType = Enum.TryParse<TaskType>(run.TaskType, out var type) ? type : Optimization.TaskType.General,
        Verdict = Enum.TryParse<Verdict>(run.Verdict, out var verdict) ? verdict : Optimization.Verdict.Rewritten,
        Model = run.Model ?? "",
        Questions = run.Questions,
        Warnings = run.Warnings,
        Elapsed = TimeSpan.FromMilliseconds(run.Ms),
        PromptTokens = run.PromptTokens,
        CachedTokens = run.CachedTokens,
        CompletionTokens = run.CompletionTokens,
    };

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private static IReadOnlyList<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").ToList()
            : [];
}
