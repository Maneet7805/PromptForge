using System.Text.Json;
using PromptForge.Core.Eval;
using PromptForge.Core.Settings;

namespace PromptForge.App;

/// <summary>
/// Runs the optimiser over a test set with the saved settings and API key, writing report.md and
/// results.json. Available as "PromptForge.exe --eval cases.json outDir [--judge] [--pause-seconds N]"
/// and from the tray menu (Developer → Run quality check). The key never leaves this PC.
/// </summary>
internal static class EvalCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            MessageBox.Show("Usage: PromptForge.exe --eval <cases.json> <output folder> [--judge] [--pause-seconds N]", "PromptForge eval");
            return 2;
        }
        var pauseIndex = Array.IndexOf(args, "--pause-seconds");
        var pause = TimeSpan.FromSeconds(pauseIndex >= 0 && pauseIndex + 1 < args.Length && double.TryParse(args[pauseIndex + 1], out var p) ? p : 8);
        using var services = new AppServices();
        return Task.Run(() => RunAsync(services, args[0], args[1], args.Contains("--judge"), pause)).GetAwaiter().GetResult() ? 0 : 1;
    }

    /// <summary>Returns true on success. Progress and the final summary go to progress.txt in the output folder.</summary>
    public static async Task<bool> RunAsync(AppServices services, string casesPath, string outDir, bool judge, TimeSpan pause)
    {
        Directory.CreateDirectory(outDir);
        var progressFile = Path.Combine(outDir, "progress.txt");
        void Progress(string msg) => File.WriteAllText(progressFile, $"{DateTime.Now:HH:mm:ss} {msg}");
        try
        {
            var cases = EvalRunner.LoadCases(casesPath);
            var endpoints = EndpointResolver.Resolve(services.Settings, services.Secrets);
            var judgeEndpoint = judge ? endpoints.Count > 1 ? endpoints[1] : endpoints[0] : null;
            var runner = new EvalRunner(services.Optimizer, services.Client);
            Log.Info($"eval started cases={cases.Count} judge={judge}");
            var results = await runner.RunAsync(cases, endpoints, judgeEndpoint, pause, new Progress<string>(Progress), CancellationToken.None);

            await File.WriteAllTextAsync(Path.Combine(outDir, "report.md"), EvalRunner.BuildReport(results, endpoints[0].Label));
            await File.WriteAllTextAsync(Path.Combine(outDir, "results.json"), JsonSerializer.Serialize(results.Select(r => new
            {
                r.Case.Id,
                r.Case.Category,
                r.Run,
                r.Passed,
                r.Error,
                r.Failures,
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
            }), new JsonSerializerOptions { WriteIndented = true }));
            var summary = $"done: {results.Count(r => r.Passed)}/{results.Count} passed";
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
