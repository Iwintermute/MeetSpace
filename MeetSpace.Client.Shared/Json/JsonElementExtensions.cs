using System;
using System.Text.Json;
namespace MeetSpace.Client.Shared.Json;

public static class JsonElementExtensions
{
    public static bool TryGetAnyProperty(this JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value.Clone();
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static string? GetString(this JsonElement element, params string[] names)
    {
        if (!element.TryGetAnyProperty(out var value, names))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    public static long? GetInt64(this JsonElement element, params string[] names)
    {
        if (!element.TryGetAnyProperty(out var value, names))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
            return parsed;

        return null;
    }

    public static bool? GetBoolean(this JsonElement element, params string[] names)
    {
        if (!element.TryGetAnyProperty(out var value, names))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    public static JsonElement? GetObject(this JsonElement element, params string[] names)
    {
        if (!element.TryGetAnyProperty(out var value, names))
            return null;

        return value.ValueKind == JsonValueKind.Object ? value : null;
    }

    public static JsonElement? GetArray(this JsonElement element, params string[] names)
    {
        if (!element.TryGetAnyProperty(out var value, names))
            return null;

        return value.ValueKind == JsonValueKind.Array ? value : null;
    }
}