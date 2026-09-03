using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AlzaWatchdog.Api.Scraping;

/// <summary>A product identified from a user-supplied URL.</summary>
public record AlzaProductRef(string ProductCode, string CanonicalUrl);

public static partial class AlzaUrl
{
    /// <summary>
    /// Hosts we accept. Adding "www.alza.cz" here is the only change needed to
    /// support another locale — nothing else keys off the host.
    /// </summary>
    private static readonly string[] AllowedHosts = ["www.alza.sk", "alza.sk"];

    private const string CanonicalHost = "www.alza.sk";

    /// <summary>Product detail pages end in "-d<digits>.htm", e.g. "...-d10818009.htm".</summary>
    [GeneratedRegex(@"-d(?<code>\d+)\.htm$", RegexOptions.IgnoreCase)]
    private static partial Regex ProductCodePattern();

    /// <summary>
    /// Validates a user-supplied URL and reduces it to a product code plus a
    /// canonical URL with query string and fragment removed, so the same product
    /// pasted with tracking parameters resolves to one entry.
    /// </summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out AlzaProductRef? product)
    {
        product = null;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var candidate = input.Trim();

        // Be forgiving about a pasted URL that omits the scheme.
        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = "https://" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (!AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            return false;

        var match = ProductCodePattern().Match(uri.AbsolutePath);
        if (!match.Success)
            return false;

        var code = match.Groups["code"].Value;
        product = new AlzaProductRef(code, $"https://{CanonicalHost}{uri.AbsolutePath}");
        return true;
    }
}
