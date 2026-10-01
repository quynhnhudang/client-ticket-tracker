using System.Globalization;
using System.Text.RegularExpressions;

namespace TicketTracker.Api.Data.Queries;

/// <summary>
/// Turns raw search-box text into safe values for the SQL parameters.
///
/// Parameterisation stops SQL injection: the value can never become SQL code.
/// But a parameter's *value* is still interpreted by LIKE and CONTAINS:
///   - In LIKE, % _ and [ are wildcards even inside a parameter. Searching for
///     "50%" would mean "starts with 50", and "_" would match any single character.
///   - CONTAINS has its own query language, so a stray quote or a word like
///     NEAR can cause a syntax error (a 500) instead of a search.
/// So both need cleaning here, on top of parameterisation.
///
/// Pure functions with no database access, so they're unit tested directly.
/// </summary>
internal static partial class SearchInput
{
    public const int MaxQueryLength = 200;
    public const int MaxKeywordTerms = 8;

    /// <summary>"123" or "#123" is a ticket ID. Anything else ("123abc", "-5", "1 2") is not.</summary>
    public static bool TryParseTicketId(string input, out int id)
    {
        var s = input.Trim();
        if (s.StartsWith('#'))
            s = s[1..];

        // NumberStyles.None: digits only. No sign, no spaces, no thousands separators.
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }

    /// <summary>
    /// Escapes LIKE wildcards so they match literally. Must be used with  ESCAPE '\'  in the SQL.
    /// The backslash itself is escaped first; otherwise the backslashes added for
    /// the other characters would get escaped a second time.
    /// </summary>
    public static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\")
             .Replace("%", @"\%")
             .Replace("_", @"\_")
             .Replace("[", @"\[");

    /// <summary>
    /// Builds a CONTAINS condition in which every word must appear, each as a prefix:
    ///   "Login  timeout!!"  ->  "login*" AND "timeout*"
    /// Only runs of letters/digits survive, so no quote, parenthesis or operator
    /// from the user can reach the full-text parser. One-letter words are dropped
    /// ("a*" would match a large share of all words). Returns null if nothing usable is left.
    /// </summary>
    public static string? BuildFullTextTerm(string input)
    {
        var words = WordPattern().Matches(input)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length >= 2)
            .Distinct()
            .Take(MaxKeywordTerms)
            .ToList();

        return words.Count == 0
            ? null
            : string.Join(" AND ", words.Select(w => $"\"{w}*\""));
    }

    // \p{L} = any letter (including accented), \p{N} = any digit.
    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
