using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiraAssistant.Infrastructure.Logging;

namespace AiraAssistant.Infrastructure;

/// <summary>
/// DPAPI-protected local secrets (%LOCALAPPDATA%\Aira\secrets.dat).
/// API keys stay out of plain appsettings.json: the file holds base64 blobs
/// encrypted for the current Windows user (ProtectedData.CurrentUser), so
/// copying the folder to another machine/user yields garbage. Fail-safe:
/// a missing or corrupt store behaves like an empty one.
/// </summary>
public sealed class SecretStore
{
    public const string OpenAiKeyName = "OpenAiKey";
    public const string AnthropicKeyName = "AnthropicKey";

    // App-specific salt so the blobs are bound to this application.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AiraAssistant::DPAPI::v1::Entropy");

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    public SecretStore(ILogger logger)
    {
        _logger = logger;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aira");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "secrets.dat");
    }

    public string? Get(string name)
    {
        lock (_gate)
        {
            if (!TryLoad(out var store) || !store.TryGetValue(name, out var protected64))
            {
                return null;
            }
            try
            {
                var raw = ProtectedData.Unprotect(Convert.FromBase64String(protected64), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch (Exception ex)
            {
                _logger.Warning($"SecretStore: could not decrypt '{name}' ({ex.Message}).");
                return null;
            }
        }
    }

    public void Set(string name, string value)
    {
        lock (_gate)
        {
            TryLoad(out var store);
            if (string.IsNullOrWhiteSpace(value))
            {
                store.Remove(name); // empty clears the entry
            }
            else
            {
                var raw = Encoding.UTF8.GetBytes(value);
                store[name] = Convert.ToBase64String(ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser));
            }
            Save(store);
        }
    }

    public bool Has(string name)
    {
        lock (_gate)
        {
            return TryLoad(out var store) && store.ContainsKey(name);
        }
    }

    private bool TryLoad(out Dictionary<string, string> store)
    {
        store = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(_path)) return true;
            var json = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (parsed is not null)
            {
                store = parsed;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"SecretStore: unreadable secrets file treated as empty ({ex.Message}).");
            return true; // treat as empty rather than throwing
        }
    }

    private void Save(Dictionary<string, string> store)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch (Exception ex)
        {
            _logger.Warning($"SecretStore: could not persist secrets ({ex.Message}).");
        }
    }
}
