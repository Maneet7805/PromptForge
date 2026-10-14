using System.Net;
using PromptForge.Core.Diffing;
using PromptForge.Core.Eval;
using PromptForge.Core.Llm;
using PromptForge.Core.Optimization;
using PromptForge.Core.Settings;

namespace PromptForge.Core.Tests;

public class ErrorMappingTests
{
    private static LlmException Map(HttpStatusCode status, string body, string? retryAfter = null)
    {
        var response = new HttpResponseMessage(status);
        if (retryAfter is not null) response.Headers.TryAddWithoutValidation("retry-after", retryAfter);
        return OpenAiCompatibleClient.MapError(response, body);
    }

    [Fact]
    public void Groq_tpm_limit_parses_wait_time()
    {
        var ex = Map(HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Rate limit reached for model `openai/gpt-oss-120b` on tokens per minute (TPM): Limit 8000, Used 7000, Requested 2000. Please try again in 7.5s."}}""");
        Assert.Equal(LlmErrorKind.RateLimit, ex.Kind);
        Assert.Equal(TimeSpan.FromSeconds(7.5), ex.RetryAfter);
    }

    [Fact]
    public void Request_larger_than_the_whole_limit_is_too_large_not_rate_limited()
    {
        var ex = Map(HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Request too large for model on tokens per minute (TPM): Limit 8000, Requested 12000."}}""");
        Assert.Equal(LlmErrorKind.TooLarge, ex.Kind);
    }

    [Theory]
    [InlineData("450ms", 0.45)]
    [InlineData("1m3s", 63)]
    [InlineData("2s", 2)]
    public void Try_again_formats(string text, double seconds)
    {
        var ex = Map(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"Please try again in " + text + ".\"}}");
        Assert.Equal(seconds, ex.RetryAfter!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void Retry_after_header_wins()
    {
        Assert.Equal(TimeSpan.FromSeconds(12), Map(HttpStatusCode.TooManyRequests, "{}", "12").RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Invalid API Key\"}}", LlmErrorKind.Auth)]
    [InlineData(HttpStatusCode.NotFound, "{\"error\":{\"message\":\"The model `x` does not exist\"}}", LlmErrorKind.ModelNotFound)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"response_format json_object is not supported\"}}", LlmErrorKind.JsonModeUnsupported)]
    [InlineData(HttpStatusCode.BadRequest, "[{\"error\":{\"message\":\"API key not valid. Please pass a valid API key.\"}}]", LlmErrorKind.Auth)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "upstream down", LlmErrorKind.Server)]
    public void Status_codes_map_to_kinds(HttpStatusCode status, string body, LlmErrorKind kind) =>
        Assert.Equal(kind, Map(status, body).Kind);
}

public class HotkeyChordTests
{
    [Theory]
    [InlineData("Ctrl+Alt+P", true, true, false, "P")]
    [InlineData("ctrl + shift + f8", true, false, true, "f8")]
    [InlineData("Alt+1", false, true, false, "D1")]
    public void Parses(string text, bool ctrl, bool alt, bool shift, string key)
    {
        Assert.True(HotkeyChord.TryParse(text, out var chord));
        Assert.Equal((ctrl, alt, shift, key), (chord.Ctrl, chord.Alt, chord.Shift, chord.Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    public void Rejects_invalid(string text) => Assert.False(HotkeyChord.TryParse(text, out _));

    [Fact]
    public void Requires_ctrl_or_alt()
    {
        HotkeyChord.TryParse("Shift+P", out var chord);
        Assert.NotNull(chord.Problem());
        HotkeyChord.TryParse("Ctrl+Alt+Space", out chord);
        Assert.Null(chord.Problem());
    }

    [Fact]
    public void Round_trips_to_display_text()
    {
        HotkeyChord.TryParse("alt+ctrl+1", out var chord);
        Assert.Equal("Ctrl+Alt+1", chord.ToString());
    }
}

public class TextDiffTests
{
    [Fact]
    public void Word_level_segments_reconstruct_both_texts()
    {
        var a = "fix my login page it doesnt work";
        var b = "Fix my login page. It doesn't work after adding JWT.";
        var segments = TextDiff.Compute(a, b);

        Assert.Equal(a, string.Concat(segments.Where(s => s.Kind != DiffKind.Inserted).Select(s => s.Text)));
        Assert.Equal(b, string.Concat(segments.Where(s => s.Kind != DiffKind.Deleted).Select(s => s.Text)));
        Assert.Contains(segments, s => s.Kind == DiffKind.Inserted && s.Text.Contains("JWT"));
    }

    [Fact]
    public void Identical_text_is_one_equal_segment()
    {
        var segments = TextDiff.Compute("same text", "same text");
        Assert.Single(segments);
        Assert.Equal(0, TextDiff.ChangedFraction(segments));
    }

    [Fact]
    public void Handles_several_thousand_words()
    {
        var words = Enumerable.Range(0, 4000).Select(i => "word" + i);
        var a = string.Join(" ", words);
        var b = a.Replace("word2000", "changed");
        var segments = TextDiff.Compute(a, b);
        Assert.Contains(segments, s => s.Kind == DiffKind.Inserted && s.Text == "changed");
    }
}

public class SettingsTests
{
    [Fact]
    public void Round_trips_and_defaults_to_groq()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var settings = new AppSettings { DefaultMode = OptimizeMode.Light, PersonalContext = "CS student" };
            SettingsStore.Save(path, settings);
            var loaded = SettingsStore.Load(path);
            Assert.Equal(OptimizeMode.Light, loaded.DefaultMode);
            Assert.Equal("CS student", loaded.PersonalContext);
            Assert.Equal("groq", loaded.Primary.Provider);
            Assert.Contains("\"Light\"", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_file_gives_defaults_and_is_kept_aside()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var loaded = SettingsStore.Load(path);
            Assert.Equal(OptimizeMode.Standard, loaded.DefaultMode);
            Assert.True(File.Exists(path + ".bad"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bad");
        }
    }

    [Fact]
    public void Resolver_orders_endpoints_and_requires_keys()
    {
        var secrets = new MemorySecrets();
        var settings = new AppSettings();
        Assert.Throws<MissingKeyException>(() => EndpointResolver.Resolve(settings, secrets));

        secrets.Set("groq", "gsk_test");
        var endpoints = EndpointResolver.Resolve(settings, secrets);
        Assert.Equal(["openai/gpt-oss-120b", "openai/gpt-oss-20b"], endpoints.Select(e => e.Model));
        Assert.All(endpoints, e => Assert.Equal("https://api.groq.com/openai/v1", e.BaseUrl));
    }

    [Fact]
    public void Ollama_needs_no_key_and_custom_uses_its_own_url()
    {
        var secrets = new MemorySecrets();
        var settings = new AppSettings
        {
            Primary = new EndpointSettings { Provider = "ollama", Model = "qwen3:4b" },
            Fallback = new EndpointSettings { Provider = "custom", Model = "m", BaseUrl = "https://example.com/v1/" },
        };
        secrets.Set("custom", "abc");
        var endpoints = EndpointResolver.Resolve(settings, secrets);
        Assert.Equal("http://localhost:11434/v1", endpoints[0].BaseUrl);
        Assert.Equal("https://example.com/v1/", endpoints[1].BaseUrl);
    }

    internal sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string providerId) => _values.GetValueOrDefault(providerId);
        public void Set(string providerId, string? value) { if (value is null) _values.Remove(providerId); else _values[providerId] = value; }
        public void Clear() => _values.Clear();
    }
}

public class EvalGradingTests
{
    private static OptimizeResult Result(string draft, string prompt, Verdict verdict = Verdict.Rewritten) => new()
    {
        Original = draft, Prompt = prompt, TaskType = TaskType.General, Verdict = verdict, Model = "m",
    };

    [Fact]
    public void Flags_placeholders_filler_and_expansion()
    {
        var c = new EvalCase { Id = "t", Category = "simple", Draft = "capital of france?", MaxLengthRatio = 3 };
        var failures = EvalRunner.Grade(c, Result(c.Draft,
            "You are a world-class geographer. What is the capital of [insert country]? Explain step by step with sources and context."));
        Assert.Contains(failures, f => f.Contains("placeholder"));
        Assert.Contains(failures, f => f.Contains("filler"));
        Assert.Contains(failures, f => f.Contains("over-expanded"));
    }

    [Fact]
    public void Clean_result_passes()
    {
        var c = new EvalCase { Id = "t", Category = "coding", Draft = "fix getUser in users.ts", MaxLengthRatio = 20 };
        Assert.Empty(EvalRunner.Grade(c, Result(c.Draft, "Fix the bug in getUser in users.ts. Explain the cause and show the changed code.")));
    }

    [Fact]
    public void Placeholder_already_in_draft_is_not_flagged()
    {
        var c = new EvalCase { Id = "t", Category = "writing", Draft = "fill this template: Dear [your name], thanks" };
        Assert.Empty(EvalRunner.Grade(c, Result(c.Draft, "Fill in this template: Dear [your name], thanks")));
    }
}

public class EvalCasesFileTests
{
    [Fact]
    public void Shipped_eval_cases_parse_and_have_unique_ids()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "eval", "cases.json"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var cases = EvalRunner.LoadCases(Path.Combine(dir!, "eval", "cases.json"));
        Assert.True(cases.Count >= 40);
        Assert.Equal(cases.Count, cases.Select(c => c.Id).Distinct().Count());
        Assert.Contains(cases, c => c.Mode == OptimizeMode.Detailed);
        Assert.Contains(cases, c => c.Repeat == 3);
    }
}

public class EvalGradingNumberTests
{
    [Fact]
    public void Flags_numbers_and_missing_questions()
    {
        var c = new EvalCase { Id = "t", Category = "vague", Draft = "explain databases", ExpectQuestions = true };
        var result = new OptimizeResult { Original = c.Draft, Prompt = "Explain databases in 3 paragraphs.", TaskType = TaskType.Study, Verdict = Verdict.Rewritten, Model = "m" };
        var failures = EvalRunner.Grade(c, result);
        Assert.Contains(failures, f => f.Contains("no questions"));
        Assert.Contains(failures, f => f.Contains("added numbers") && f.Contains('3'));
    }

    [Fact]
    public void Numbers_from_the_draft_are_fine()
    {
        var c = new EvalCase { Id = "t", Category = "simple", Draft = "what does http 418 mean" };
        var result = new OptimizeResult { Original = c.Draft, Prompt = "What does HTTP status 418 mean?", TaskType = TaskType.General, Verdict = Verdict.LightEdit, Model = "m" };
        Assert.Empty(EvalRunner.Grade(c, result));
    }

    [Fact]
    public void Flags_a_rewrite_written_in_the_assistants_voice()
    {
        var c = new EvalCase { Id = "t", Category = "voice", Draft = "fix the issues you found" };
        var result = new OptimizeResult
        {
            Original = c.Draft,
            Prompt = "Please list the specific issues you want fixed. Once I have this information, I can address all the listed issues.",
            TaskType = TaskType.Coding, Verdict = Verdict.Rewritten, Model = "m",
        };
        Assert.Contains(EvalRunner.Grade(c, result), f => f.Contains("AI's reply"));
    }

    [Fact]
    public void A_normal_request_is_not_flagged_as_assistant_voice()
    {
        var c = new EvalCase { Id = "t", Category = "voice", Draft = "fix the bugs in app.py" };
        var result = new OptimizeResult
        {
            Original = c.Draft,
            Prompt = "Fix the bugs in app.py. If you need the error message, ask me for it before guessing.",
            TaskType = TaskType.Debugging, Verdict = Verdict.Rewritten, Model = "m",
        };
        Assert.DoesNotContain(EvalRunner.Grade(c, result), f => f.Contains("AI's reply"));
    }

    [Fact]
    public void Report_counts_dropped_requirements_from_the_judge()
    {
        var c = new EvalCase { Id = "t", Category = "stated-rule", Draft = "decide which issues to build, only ones that work in every situation" };
        var result = new OptimizeResult { Original = c.Draft, Prompt = "Decide which issues to build.", TaskType = TaskType.Planning, Verdict = Verdict.Rewritten, Model = "m" };
        var judge = new JudgeVerdict(false, false, "", true, true, "dropped the rule", DroppedRequirements: true, DroppedExamples: "only ones that work in every situation");
        var report = EvalRunner.BuildReport([new EvalCaseResult { Case = c, Result = result, Judge = judge }], "m");
        Assert.Contains("| Judge: dropped a stated requirement | 1/1 |", report);
        Assert.Contains("dropped=True only ones that work in every situation", report);
    }

    [Fact]
    public void Report_counts_changed_intent_from_the_judge()
    {
        var c = new EvalCase { Id = "t", Category = "intent", Draft = "also just merging to main deploy it to my laptop?" };
        var result = new OptimizeResult { Original = c.Draft, Prompt = "Explain how to merge to main and deploy to my laptop.", TaskType = TaskType.Planning, Verdict = Verdict.Rewritten, Model = "m" };
        var judge = new JudgeVerdict(false, false, "", true, true, "question became instruction", ChangedIntent: true);
        var report = EvalRunner.BuildReport([new EvalCaseResult { Case = c, Result = result, Judge = judge }], "m");
        Assert.Contains("| Judge: changed the kind of request | 1/1 |", report);
    }

    [Fact]
    public void Repair_message_asks_for_details_back_without_meta_instructions()
    {
        var message = UserMessageBuilder.BuildRepairMessage(["merge and deploy"]);
        Assert.Contains("- merge and deploy", message);
        Assert.Contains("Don't add an instruction telling the AI to use or include them", message);
    }
}
