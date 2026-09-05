namespace AlzaWatchdog.Api.Scraping;

public interface IAlzaScraper
{
    Task<ScrapeResult> FetchAsync(string canonicalUrl, CancellationToken ct = default);

}
