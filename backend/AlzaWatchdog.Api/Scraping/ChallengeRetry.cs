using AlzaWatchdog.Api.Workers;

namespace AlzaWatchdog.Api.Scraping;

/// <summary>
/// One retry of a request Cloudflare turned away with a challenge.
///
/// Measured against the live site: a request that arrives without Cloudflare's
/// cookies is challenged on something close to a coin flip, and a second request
/// moments later usually goes through — the refusal itself is what hands back the
/// cookies. Treating the first challenge as final therefore throws away a request
/// that would have worked.
///
/// Exactly one retry, and only for a challenge. A 404 is an answer, and a second
/// refusal means we really are being turned away, so pushing further would only
/// lean harder on a site that has already said no twice.
/// </summary>
public static class ChallengeRetry
{
    public static async Task<ScrapeResult> FetchAsync(
        IAlzaScraper scraper,
        string url,
        TimeSpan delay,
        ILogger logger,
        CancellationToken ct)
    {
        var result = await scraper.FetchAsync(url, ct);

        if (result.Status != ScrapeStatus.Blocked || delay <= TimeSpan.Zero)
            return result;

        logger.LogInformation("Request was challenged; retrying once in {Delay}.", delay);
        await Task.Delay(delay, ct);

        result = await scraper.FetchAsync(url, ct);

        // Logged either way: this line is the only evidence of whether the retry
        // is worth making.
        logger.LogInformation("Retry after the challenge: {Status}.", result.Status);
        return result;
    }
}
