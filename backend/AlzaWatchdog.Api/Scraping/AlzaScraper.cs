using System.Net;

namespace AlzaWatchdog.Api.Scraping;

/// <summary>
/// Fetches an alza.sk product page and hands the HTML to <see cref="AlzaProductParser"/>.
///
/// The transport configuration in Program.cs is load-bearing, not cosmetic — see the
/// comments on the typed-client registration there before changing anything about how
/// this client makes requests.
/// </summary>
public class AlzaScraper(HttpClient http, ILogger<AlzaScraper> logger) : IAlzaScraper
{
    public async Task<ScrapeResult> FetchAsync(string canonicalUrl, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(canonicalUrl, ct);

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("alza.sk blocked the request for {Url} ({Status})", canonicalUrl, (int)response.StatusCode);
                return ScrapeResult.Failure(ScrapeStatus.Blocked, $"Blocked by alza.sk ({(int)response.StatusCode}).");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return ScrapeResult.Failure(ScrapeStatus.ProductNotFound, "alza.sk has no product at this URL (404).");

            if (!response.IsSuccessStatusCode)
                return ScrapeResult.Failure(ScrapeStatus.TransientError, $"alza.sk returned {(int)response.StatusCode}.");

            var html = await response.Content.ReadAsStringAsync(ct);
            var result = AlzaProductParser.Parse(html);

            if (result.Status == ScrapeStatus.ParseFailed)
                logger.LogWarning("Could not parse product data from {Url}", canonicalUrl);

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            // HttpClient surfaces its own timeout as a cancellation.
            return ScrapeResult.Failure(ScrapeStatus.TransientError, "Request to alza.sk timed out.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Network error fetching {Url}", canonicalUrl);
            return ScrapeResult.Failure(ScrapeStatus.TransientError, $"Network error: {ex.Message}");
        }
    }
}
