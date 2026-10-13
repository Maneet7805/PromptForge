using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PromptForge.Core.Settings;

namespace PromptForge.App.Infrastructure;

/// <summary>
/// API keys encrypted with Windows DPAPI (CurrentUser scope): only your Windows account on this PC
/// can decrypt them. Stored separately from settings.json so the settings file holds no secrets.
/// </summary>
internal sealed class DpapiSecretStore(string path) : ISecretStore
{
    private static readonly byte[] Entropy = "PromptForge.v1.api-keys"u8.ToArray();
    private readonly object _gate = new();

    public string? Get(string providerId)
    {
        lock (_gate)
        {
            var all = Read();
            if (!all.TryGetValue(providerId, out var protectedBase64)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                Log.Warn($"stored key for {providerId} could not be decrypted ({ex.GetType().Name})");
                return null;
            }
        }
    }

    public void Set(string providerId, string? value)
    {
        lock (_gate)
        {
            var all = Read();
            if (string.IsNullOrWhiteSpace(value))
            {
                all.Remove(providerId);
            }
            else
            {
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), Entropy, DataProtectionScope.CurrentUser);
                all[providerId] = Convert.ToBase64String(bytes);
            }
            Write(all);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private Dictionary<string, string> Read()
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Write(Dictionary<string, string> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all));
        File.Move(temp, path, overwrite: true);
    }
}
