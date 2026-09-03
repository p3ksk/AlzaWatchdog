using System.Globalization;

namespace AlzaWatchdog.Api.Scraping;

/// <summary>
/// Parses the prices printed in alza.sk's markup, which follow Slovak
/// conventions: a comma decimal separator and spaces for thousands — often
/// non-breaking or narrow ones, e.g. "1 149,00 €" and "10,98 €".
/// </summary>
public static class PriceText
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        Span<char> buffer = stackalloc char[text.Length];
        var length = 0;
        var seenComma = false;

        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c))
            {
                buffer[length++] = c;
            }
            else if (c is ',')
            {
                // Only the first comma is a decimal point; anything after it in a
                // well-formed price is not something we want to keep guessing at.
                if (seenComma)
                    return false;

                seenComma = true;
                buffer[length++] = '.';
            }
            // Everything else — currency symbols, spaces of every width, dots used
            // as thousand separators, stray markup whitespace — is separator noise.
        }

        if (length == 0)
            return false;

        return decimal.TryParse(
            buffer[..length], NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
