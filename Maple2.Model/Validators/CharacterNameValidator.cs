using System.Text.RegularExpressions;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

public static partial class NameValidator {
    // Regex patterns for valid names
    private static readonly Regex ValidNamePatternAscii = NamePatternAsciiRegex();
    private static readonly Regex ValidNamePatternUnicode = NamePatternUnicodeRegex();

    /// <summary>
    /// Validates a name according to all rules.
    /// </summary>
    /// <param name="name">The name to validate</param>
    /// <returns>True if valid, false if invalid</returns>
    public static bool ValidName(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return false; // Null or whitespace
        }
        // Select pattern
        Regex pattern = Constant.AllowUnicodeInNames ? ValidNamePatternUnicode : ValidNamePatternAscii;
        if (!pattern.IsMatch(name)) {
            return false;
        }

        // Check for names that are only special characters
        if (name.All(c => !char.IsLetterOrDigit(c))) {
            return false;
        }

        return true; // Valid name
    }

    // ASCII only: A-Z, a-z, 0-9 (no dash, no underscore)
    [GeneratedRegex(@"^[A-Za-z0-9]+$", RegexOptions.Compiled)]
    private static partial Regex NamePatternAsciiRegex();

    // Unicode: \p{L} (all letters, including accents), 0-9 (no dash, no underscore)
    [GeneratedRegex(@"^[\p{L}0-9]+$", RegexOptions.Compiled)]
    private static partial Regex NamePatternUnicodeRegex();
}
