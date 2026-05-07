using System.Collections.Generic;
using System.Text.Json;
using LensHH.Core.IO;

namespace LensHH.RenderApp;

public class RenderRequest
{
    public string Analysis { get; set; } = "";
    public LhltFile System { get; set; } = new();
    public Dictionary<string, JsonElement>? Params { get; set; }
}

public class RenderResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public static class ParamHelper
{
    public static int GetInt(Dictionary<string, JsonElement>? p, string key, int defaultValue)
    {
        if (p != null && p.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Number)
            return el.GetInt32();
        return defaultValue;
    }

    public static double GetDouble(Dictionary<string, JsonElement>? p, string key, double defaultValue)
    {
        if (p != null && p.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Number)
            return el.GetDouble();
        return defaultValue;
    }

    public static bool GetBool(Dictionary<string, JsonElement>? p, string key, bool defaultValue)
    {
        if (p != null && p.TryGetValue(key, out var el) &&
            (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False))
            return el.GetBoolean();
        return defaultValue;
    }

    public static string GetString(Dictionary<string, JsonElement>? p, string key, string defaultValue)
    {
        if (p != null && p.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? defaultValue;
        return defaultValue;
    }
}
