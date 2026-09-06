using System.Text.Json;

namespace Md.App.Logic.Preview;

/// <summary>
/// <c>ExecuteScriptAsync</c> hands back the result as JSON, not as a value: <c>1</c> comes back as
/// the one character <c>1</c>, the string <c>"1"</c> as the three characters <c>"1"</c> (the
/// render-complete flag is a string attribute, which is why Android compares against
/// <c>"\"1\""</c>), and a failed or void script as the four characters <c>null</c>. Decoding it by
/// hand is how a port ends up writing the literal word <c>null</c> into a file, so everything goes
/// through <see cref="JsonDocument"/> here.
///
/// Nothing throws: a script that fails, a WebView2 that is gone and a page that returned something
/// unexpected are all "no answer", and every caller has a defined behaviour for that.
/// </summary>
public static class JsonScript
{
    /// <summary>The JSON number, or null for <c>null</c>, a string, a non-finite value or malformed JSON.</summary>
    public static double? Number(string? json)
    {
        var value = Parse(json);
        if (value is null || value.Value.ValueKind != JsonValueKind.Number) return null;
        return value.Value.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
    }

    /// <summary>The JSON string, or null for <c>null</c>, a number, an object or malformed JSON.</summary>
    public static string? String(string? json)
    {
        var value = Parse(json);
        return value is not null && value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
    }

    /// <summary>
    /// The scroll-sync message (§4.6): <c>{"fraction":0.42,"echo":false}</c> from
    /// <c>WebMessageReceived.WebMessageAsJson</c>. Null when it is not one — the page can post
    /// nothing else today, but the channel is open and the host must not crash on a surprise.
    /// </summary>
    public static (double Fraction, bool Echo)? ScrollMessage(string? json)
    {
        var value = Parse(json);
        if (value is null || value.Value.ValueKind != JsonValueKind.Object) return null;
        if (!value.Value.TryGetProperty("fraction", out var fraction) || fraction.ValueKind != JsonValueKind.Number) return null;
        if (!fraction.TryGetDouble(out var f) || !double.IsFinite(f)) return null;

        var echo = value.Value.TryGetProperty("echo", out var e) && e.ValueKind == JsonValueKind.True;
        return (f, echo);
    }

    static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            // Clone: the document owns the element's buffer and is disposed on the way out.
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
