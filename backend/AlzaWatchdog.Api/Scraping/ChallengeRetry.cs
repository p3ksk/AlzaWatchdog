using AlzaWatchdog.Api.Workers;

namespace AlzaWatchdog.Api.Scraping;

/// <summary>
/// One retry of a request Cloudflare turned away, and only for a challenge — a
/// 404 is an answer, and a second refusal means we really are being turned away.
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

        // Logged either way: the only evidence of whether the retry earns its place.
        logger.LogInformation("Retry after the challenge: {Status}.", result.Status);
        return result;
    }
}
