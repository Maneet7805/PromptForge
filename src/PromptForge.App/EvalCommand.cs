using System.Text.Json;
using PromptForge.Core.Eval;
using PromptForge.Core.Settings;

namespace PromptForge.App;

/// <summary>
/// PromptForge.exe --eval cases.json outputDir [--judge] [--pause-seconds N]
/// Runs the optimiser over a test set using the saved settings and API key, and writes report.md
/// and results.json. The key never leaves this PC's encrypted store.
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
        var casesPath = args[0];
        var outDir = args[1];
        var judge = args.Contains("--judge");
        var pauseIndex = Array.IndexOf(args, "--pause-seconds");
        var pause = TimeSpan.FromSeconds(pauseIndex >= 0 && pauseIndex + 1 < args.Length && double.TryParse(args[pauseIndex + 1], out var p) ? p : 6);

        Directory.CreateDirectory(outDir);
        var progressFile = Path.Combine(outDir, "progress.txt");
        try
        {
            using var services = new AppServices();
            var cases = EvalRunner.LoadCases(casesPath);
            var endpoints = EndpointResolver.Resolve(services.Settings, services.Secrets);
            var judgeEndpoint = judge ? endpoints.Count > 1 ? endpoints[1] : endpoints[0] : null;
            var runner = new EvalRunner(services.Optimizer, services.Client);
            var progress = new Progress<string>(msg => File.WriteAllText(progressFile, $"{DateTime.Now:HH:mm:ss} {msg}"));

            var results = Task.Run(() => runner.RunAsync(cases, endpoints, judgeEndpoint, pause, progress, CancellationToken.None)).GetAwaiter().GetResult();

            File.WriteAllText(Path.Combine(outDir, "report.md"), EvalRunner.BuildReport(results, endpoints[0].Label));
            File.WriteAllText(Path.Combine(outDir, "results.json"), JsonSerializer.Serialize(results.Select(r => new
            {
                r.Case.Id,
                r.Case.Category,
                r.Run,
                r.Passed,
                r.Error,
                r.Failures,
                Output = r.Result?.Prompt,
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
            File.WriteAllText(progressFile, $"{DateTime.Now:HH:mm:ss} done: {results.Count(r => r.Passed)}/{results.Count} passed");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(progressFile, $"{DateTime.Now:HH:mm:ss} failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
