using System.Text.Json;
using PromptForge.Core.Eval;
using PromptForge.Core.Optimization;
using PromptForge.Core.Settings;

namespace PromptForge.App;

/// <summary>
/// Runs the optimiser over a test set with the saved settings and API keys, writing report.md and
/// results.json. Available as
/// "PromptForge.exe --eval cases.json outDir [--judge] [--repeat N] [--system-prompt file] [--pause-seconds N]"
/// and from the tray menu (Developer → Run quality check). Keys never leave this PC.
/// "PromptForge.exe --eval-compare before\results.json after\results.json compare.md" compares two runs.
/// "PromptForge.exe --rejudge run\results.json cases.json outDir [--grades grades.csv]" grades a saved run again (#34).
/// </summary>
internal static class EvalCommand
{
    public const int DefaultRepeat = EvalRunner.DefaultRepeat;

    private const string Usage =
        "Usage:\nPromptForge.exe --eval <cases.json> <output folder> [--judge] [--repeat N] [--system-prompt <file>] [--pause-seconds N] [--include-held-out]\n" +
        "PromptForge.exe --eval-compare <before results.json> <after results.json> <output .md>\n" +
        "PromptForge.exe --rejudge <results.json> <cases.json> <output folder> [--grades <grades.csv>] [--pause-seconds N]";

    /// <summary>Judge calls only, so a shorter default wait than a full run, which also calls the optimiser.</summary>
    private const double DefaultRejudgePauseSeconds = 4;

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            MessageBox.Show(Usage, "PromptForge eval");
            return 2;
        }
        var pause = TimeSpan.FromSeconds(Option(args, "--pause-seconds") is { } p && double.TryParse(p, out var s) ? s : 8);
        var repeat = Option(args, "--repeat") is { } r && int.TryParse(r, out var n) && n > 0 ? n : DefaultRepeat;
        var instructions = Option(args, "--system-prompt");
        using var services = new AppServices();
        return Task.Run(() => RunAsync(services, args[0], args[1], args.Contains("--judge"), pause, repeat, instructions, args.Contains("--include-held-out"))).GetAwaiter().GetResult() ? 0 : 1;
    }

    public static int Compare(string[] args)
    {
        if (args.Length < 3)
        {
            MessageBox.Show(Usage, "PromptForge eval");
            return 2;
        }
        var output = Path.GetFullPath(args[2]);
        if (output.Equals(Path.GetFullPath(args[0]), StringComparison.OrdinalIgnoreCase) ||
            output.Equals(Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("The comparison would overwrite one of the runs. Choose a different output file.", "PromptForge eval");
            return 2;
        }
        try
        {
            var report = EvalComparison.Build(EvalComparison.LoadRuns(args[0]), EvalComparison.LoadRuns(args[1]), args[0], args[1]);
            File.WriteAllText(args[2], report);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        {
            MessageBox.Show("Couldn't compare the runs: " + ex.Message, "PromptForge eval");
            return 1;
        }
    }

    public static int Rejudge(string[] args)
    {
        if (args.Length < 3)
        {
            MessageBox.Show(Usage, "PromptForge eval");
            return 2;
        }
        var outDir = Path.GetFullPath(args[2]);
        var savedDir = Path.GetDirectoryName(Path.GetFullPath(args[0]));
        if (outDir.Equals(savedDir, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("The re-judge would overwrite the run it grades. Choose a different output folder.", "PromptForge eval");
            return 2;
        }
        var pause = TimeSpan.FromSeconds(Option(args, "--pause-seconds") is { } p && double.TryParse(p, out var s) ? s : DefaultRejudgePauseSeconds);
        using var services = new AppServices();
        return Task.Run(() => RejudgeAsync(services, args[0], args[1], outDir, Option(args, "--grades"), pause)).GetAwaiter().GetResult() ? 0 : 1;
    }

    /// <summary>Returns true on success. Writes report.md, results.json and, with grades, calibration.md.</summary>
    private static async Task<bool> RejudgeAsync(AppServices services, string resultsPath, string casesPath, string outDir, string? gradesPath, TimeSpan pause)
    {
        Directory.CreateDirectory(outDir);
        var progressFile = Path.Combine(outDir, "progress.txt");
        void Progress(string msg) => File.WriteAllText(progressFile, $"{DateTime.Now:HH:mm:ss} {msg}");
        try
        {
            var cases = EvalRunner.LoadCases(casesPath);
            var saved = SavedResults.Load(resultsPath);
            // Read the grades first, so a typo in the file fails before any judge calls are spent.
            var grades = gradesPath is null ? null : JudgeCalibration.LoadGrades(gradesPath);
            var endpoints = EndpointResolver.Resolve(services.Settings, services.Secrets);
            var (judge, independent) = EndpointResolver.ResolveJudge(services.Settings, services.Secrets, endpoints);
            var runner = new EvalRunner(services.Optimizer, services.Client);
            Log.Info($"rejudge started runs={saved.Count} judge={judge.Label} independent={independent} grades={grades is not null}");
            var results = await runner.RejudgeAsync(cases, saved, judge, pause, new Progress<string>(Progress), CancellationToken.None);

            var model = saved.Select(r => r.Model).FirstOrDefault(m => !string.IsNullOrEmpty(m)) ?? "unknown";
            var info = new EvalRunInfo(model, judge.Label, independent, StoppedEarly: runner.StoppedEarly,
                RejudgedFrom: Path.GetFullPath(resultsPath), Skipped: runner.Skipped);
            await File.WriteAllTextAsync(Path.Combine(outDir, "report.md"), EvalRunner.BuildReport(results, info));
            await File.WriteAllTextAsync(Path.Combine(outDir, "results.json"), SavedResults.ToJson(results));
            if (grades is not null)
                await File.WriteAllTextAsync(Path.Combine(outDir, "calibration.md"), JudgeCalibration.Build(results, grades));

            var errors = results.Count(r => r.Error is not null);
            var summary = $"{(runner.StoppedEarly is null ? "done" : "stopped early")}: {results.Count(r => r.Passed)}/{results.Count - errors} runs passed, {errors} errored, {runner.Skipped.Count} left out"
                + (runner.StoppedEarly is null ? "" : $". {runner.StoppedEarly}");
            Progress(summary);
            Log.Info("rejudge " + summary);
            return true;
        }
        catch (Exception ex)
        {
            Progress($"failed: {ex.GetType().Name}: {ex.Message}");
            Log.Error("rejudge failed", ex);
            return false;
        }
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>
    /// The system prompt from a file, e.g. eval\prompts\v1.0.0.txt, to measure it against the built-in one.
    /// Line endings are normalised so a file saved on Windows matches the built-in text byte for byte.
    /// </summary>
    public static string LoadInstructions(string path) => File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n');

    /// <summary>Returns true on success. Progress and the final summary go to progress.txt in the output folder.</summary>
    public static async Task<bool> RunAsync(AppServices services, string casesPath, string outDir, bool judge, TimeSpan pause,
        int repeat = DefaultRepeat, string? instructionsPath = null, bool includeHeldOut = false)
    {
        Directory.CreateDirectory(outDir);
        var progressFile = Path.Combine(outDir, "progress.txt");
        void Progress(string msg) => File.WriteAllText(progressFile, $"{DateTime.Now:HH:mm:ss} {msg}");
        try
        {
            var allCases = EvalRunner.LoadCases(casesPath);
            var cases = EvalRunner.SelectCases(allCases, includeHeldOut);
            var endpoints = EndpointResolver.Resolve(services.Settings, services.Secrets);
            var (judgeEndpoint, independent) = EndpointResolver.ResolveJudge(services.Settings, services.Secrets, endpoints);
            var options = instructionsPath is null ? new OptimizerOptions() : new OptimizerOptions { Instructions = LoadInstructions(instructionsPath) };
            var info = new EvalRunInfo(endpoints[0].Label, judge ? judgeEndpoint.Label : null, independent,
                instructionsPath is null ? null : Path.GetFileName(instructionsPath), HeldOutSkipped: allCases.Count - cases.Count);
            var runner = new EvalRunner(services.Optimizer, services.Client);
            Log.Info($"eval started cases={cases.Count} repeat={repeat} judge={(judge ? judgeEndpoint.Label : "off")} independent={independent} customPrompt={instructionsPath is not null}");
            var results = await runner.RunAsync(cases, endpoints, judge ? judgeEndpoint : null, pause, new Progress<string>(Progress), CancellationToken.None, repeat, options);

            info = info with { StoppedEarly = runner.StoppedEarly };
            await File.WriteAllTextAsync(Path.Combine(outDir, "report.md"), EvalRunner.BuildReport(results, info));
            await File.WriteAllTextAsync(Path.Combine(outDir, "results.json"), SavedResults.ToJson(results));
            var errors = results.Count(r => r.Error is not null);
            var summary = $"{(runner.StoppedEarly is null ? "done" : "stopped early")}: {results.Count(r => r.Passed)}/{results.Count - errors} runs passed, {errors} errored"
                + (runner.StoppedEarly is null ? "" : $". {runner.StoppedEarly}");
            Progress(summary);
            Log.Info("eval " + summary);
            return true;
        }
        catch (Exception ex)
        {
            Progress($"failed: {ex.GetType().Name}: {ex.Message}");
            Log.Error("eval failed", ex);
            return false;
        }
    }
}
