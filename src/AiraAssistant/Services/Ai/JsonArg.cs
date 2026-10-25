using System;
using System.Text.Json;

namespace AiraAssistant.Services.Ai;

/// <summary>
/// Tiny wrapper around <see cref="JsonDocument"/> for the function router,
/// so we never crash if a tool argument is missing or has the wrong type.
/// </summary>
internal readonly struct JsonArg
{
    private readonly JsonElement _root;

    private JsonArg(JsonElement root) { _root = root; }

    public static JsonArg Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonArg(default);
        try
        {
            using var doc = JsonDocument.Parse(json);
            // Root may be a top-level value, but for our schema it's always an object.
            return new JsonArg(doc.RootElement.Clone());
        }
        catch
        {
            return new JsonArg(default);
        }
    }

    public string? GetString(string name)
    {
        if (_root.ValueKind != JsonValueKind.Object) return null;
        return _root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    public int GetInt(string name, int fallback)
    {
        if (_root.ValueKind != JsonValueKind.Object) return fallback;
        if (!_root.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var n) ? n : fallback,
            JsonValueKind.String => int.TryParse(v.GetString(), out var n) ? n : fallback,
            _ => fallback
        };
    }

    public bool GetBool(string name, bool fallback)
    {
        if (_root.ValueKind != JsonValueKind.Object) return fallback;
        if (!_root.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) ? b : fallback,
            _ => fallback
        };
    }
}
