using System.Text.Json.Nodes;
using PromptForge.Core.Optimization;

namespace PromptForge.Core.Llm;

/// <summary>
/// A known OpenAI-compatible provider. Every supported service speaks the same
/// /chat/completions API, so adding one is data, not code.
/// Model IDs and limits verified against provider docs on 2026-10-13.
/// </summary>
public sealed record ProviderPreset
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string BaseUrl { get; init; }
    public string DefaultModel { get; init; } = "";
    public string DefaultFallbackModel { get; init; } = "";
    public string? KeyUrl { get; init; }
    public bool RequiresKey { get; init; } = true;
    public bool SupportsJsonMode { get; init; } = true;
    public string? DefaultTranscriptionModel { get; init; }
    public string Notes { get; init; } = "";

    /// <summary>Adds provider-specific parameters (reasoning controls) to a chat request body.</summary>
    public Action<JsonObject, OptimizeMode>? ApplyExtras { get; init; }

    public bool SupportsTranscription => DefaultTranscriptionModel is not null;
}

public static class ProviderPresets
{
    public static readonly ProviderPreset Groq = new()
    {
        Id = "groq",
        DisplayName = "Groq (free tier)",
        BaseUrl = "https://api.groq.com/openai/v1",
        DefaultModel = "openai/gpt-oss-120b",
        // Groq rate-limits per model, so the fallback has its own separate allowance.
        DefaultFallbackModel = "openai/gpt-oss-20b",
        KeyUrl = "https://console.groq.com/keys",
        DefaultTranscriptionModel = "whisper-large-v3-turbo",
        Notes = "Free plan: 30 requests/min, 1,000 requests/day, 8K tokens/min per model. Fast.",
        ApplyExtras = (body, mode) =>
        {
            // gpt-oss models are reasoning models: keep reasoning short and out of the response.
            var model = body["model"]?.GetValue<string>() ?? "";
            if (model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
            {
                body["reasoning_effort"] = mode == OptimizeMode.Detailed ? "medium" : "low";
                body["include_reasoning"] = false;
            }
        },
    };

    public static readonly ProviderPreset Gemini = new()
    {
        Id = "gemini",
        DisplayName = "Google Gemini (free tier)",
        BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
        DefaultModel = "gemini-3.5-flash-lite",
        DefaultFallbackModel = "gemini-3.1-flash-lite",
        KeyUrl = "https://aistudio.google.com/apikey",
        Notes = "Free tier prompts may be used by Google to improve its products. Flash-Lite allows roughly 500 requests/day.",
        ApplyExtras = (body, _) => body["reasoning_effort"] = "low",
    };

    public static readonly ProviderPreset Ollama = new()
    {
        Id = "ollama",
        DisplayName = "Ollama (local, private)",
        BaseUrl = "http://localhost:11434/v1",
        RequiresKey = false,
        Notes = "Runs on your PC: nothing leaves the machine, but small local models rewrite less well and use a lot of RAM. Enter the name of a model you have pulled.",
    };

    public static readonly ProviderPreset Custom = new()
    {
        Id = "custom",
        DisplayName = "Custom OpenAI-compatible API",
        BaseUrl = "",
        DefaultTranscriptionModel = null,
        Notes = "Any service with an OpenAI-compatible /chat/completions endpoint (OpenAI, OpenRouter, Anthropic's compatibility endpoint, LM Studio...).",
    };

    public static IReadOnlyList<ProviderPreset> All { get; } = [Groq, Gemini, Ollama, Custom];

    public static ProviderPreset Get(string? id) =>
        All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? Groq;
}
