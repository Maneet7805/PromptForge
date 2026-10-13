using PromptForge.Core.Llm;

namespace PromptForge.Core.Settings;

/// <summary>Stores API keys by provider id. The Windows app implements this with DPAPI.</summary>
public interface ISecretStore
{
    string? Get(string providerId);
    void Set(string providerId, string? value);
    void Clear();
}

public sealed class MissingKeyException(string providerName, string? keyUrl)
    : Exception($"Add your {providerName} API key in Settings → Model." + (keyUrl is null ? "" : $" Get a free one at {keyUrl}"))
{
    public string? KeyUrl { get; } = keyUrl;
}

public static class EndpointResolver
{
    /// <summary>Primary first, then the fallback if it's enabled and different. Endpoints without a needed key are skipped.</summary>
    public static IReadOnlyList<LlmEndpoint> Resolve(AppSettings settings, ISecretStore secrets)
    {
        var list = new List<LlmEndpoint>();
        MissingKeyException? missing = null;

        foreach (var cfg in new[] { settings.Primary, settings.Fallback })
        {
            if (cfg is null || !cfg.Enabled || string.IsNullOrWhiteSpace(cfg.Model)) continue;
            var endpoint = Build(cfg.Provider, cfg.Model, cfg.BaseUrl, secrets, out var keyProblem);
            if (endpoint is null)
            {
                missing ??= keyProblem;
                continue;
            }
            if (!list.Any(e => e.BaseUrl == endpoint.BaseUrl && e.Model == endpoint.Model))
                list.Add(endpoint);
        }

        if (list.Count == 0 && missing is not null) throw missing;
        return list;
    }

    public static LlmEndpoint? ResolveVoice(AppSettings settings, ISecretStore secrets) =>
        Build(settings.Voice.Provider, settings.Voice.Model, settings.Voice.BaseUrl, secrets, out _);

    public static LlmEndpoint? Build(string providerId, string model, string? baseUrl, ISecretStore secrets, out MissingKeyException? keyProblem)
    {
        keyProblem = null;
        var preset = ProviderPresets.Get(providerId);
        var url = string.IsNullOrWhiteSpace(baseUrl) || preset.Id is not ("custom" or "ollama") ? preset.BaseUrl : baseUrl.Trim();
        if (string.IsNullOrWhiteSpace(url)) return null;
        var key = secrets.Get(preset.Id);
        if (preset.RequiresKey && string.IsNullOrWhiteSpace(key))
        {
            keyProblem = new MissingKeyException(preset.DisplayName, preset.KeyUrl);
            return null;
        }
        return new LlmEndpoint(preset, url, model.Trim(), key);
    }
}
