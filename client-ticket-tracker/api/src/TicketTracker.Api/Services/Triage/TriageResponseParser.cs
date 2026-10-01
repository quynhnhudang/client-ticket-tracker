using System.Text.Json;
using TicketTracker.Api.Domain;

namespace TicketTracker.Api.Services.Triage;

/// <summary>
/// Turns the model's reply into a TriageSuggestion, or null if it isn't valid.
/// Pure and unit-tested, because model output is untrusted input like any other.
/// </summary>
internal static class TriageResponseParser
{
    public const int MaxReasonLength = 300;

    public static TriageSuggestion? Parse(string? modelText)
    {
        if (string.IsNullOrWhiteSpace(modelText))
            return null;

        // Models sometimes wrap JSON in prose or ```json fences. Take the outermost {...}.
        var start = modelText.IndexOf('{');
        var end = modelText.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(modelText[start..(end + 1)]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!TryGetEnum<TicketCategory>(root, "category", out var category) ||
                !TryGetEnum<TicketPriority>(root, "priority", out var priority))
                return null;

            var reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()!.Trim()
                : "";
            if (reason.Length > MaxReasonLength)
                reason = reason[..(MaxReasonLength - 1)] + "…";

            return new TriageSuggestion(category, priority, reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Accepts enum names only, case-insensitively ("Bug", "urgent", "Data Fix").
    /// Anything containing a digit is rejected: Enum.TryParse would otherwise accept
    /// "7" (not a defined value) or "1" (a value the model never named); commas too.
    /// </summary>
    private static bool TryGetEnum<T>(JsonElement root, string name, out T value) where T : struct, Enum
    {
        value = default;
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
            return false;

        var text = prop.GetString()!.Replace(" ", "");
        return !text.Any(c => char.IsDigit(c) || c == ',') // "Bug,Question" would parse as a combination
               && Enum.TryParse(text, ignoreCase: true, out value)
               && Enum.IsDefined(value);
    }
}
