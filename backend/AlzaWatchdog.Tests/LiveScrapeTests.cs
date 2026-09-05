using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using AlzaWatchdog.Api.Scraping;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlzaWatchdog.Tests;

/// <summary>
/// Hits the real alza.sk. Skipped by default so the suite stays offline and
/// polite; run it by hand after touching anything about how the scraper talks to
/// the network:
///
///   dotnet test --filter FullyQualifiedName~LiveScrapeTests
///
/// This is the only test that can catch Cloudflare changing its mind about our
/// TLS fingerprint, which is the failure mode most likely to break the app in
/// production while every offline test still passes.
/// </summary>
public class LiveScrapeTests
{
    private const string Skip = "Hits alza.sk; run manually.";
    private const string ProductUrl = "https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm";

    [Fact(Skip = Skip)]
    public async Task Fetches_a_real_product_page()
    {
        var scraper = new AlzaScraper(CreateClient(), NullLogger<AlzaScraper>.Instance);

        var result = await scraper.FetchAsync(ProductUrl);

        Assert.Equal(ScrapeStatus.Success, result.Status);
        Assert.Equal("CUDY N300 WiFi Router", result.Name);
        Assert.NotNull(result.Price);
        Assert.Equal("EUR", result.Currency);
    }

    [Fact(Skip = Skip)]
    public async Task Survives_several_requests_in_a_row()
    {
        var scraper = new AlzaScraper(CreateClient(), NullLogger<AlzaScraper>.Instance);

        for (var i = 0; i < 3; i++)
        {
            var result = await scraper.FetchAsync(ProductUrl);
            Assert.NotEqual(ScrapeStatus.Blocked, result.Status);
            await Task.Delay(TimeSpan.FromSeconds(4));
        }
    }

    /// <summary>
    /// Mirrors the transport configured in Program.cs. Keep the two in step —
    /// notably the pinned TLS version and the sec-ch-ua/Sec-Fetch header set,
    /// without which every request returns 403. A client here that is missing what
    /// the app sends does not test the app: it reproduces a failure the app does
    /// not have, which is worse than no test at all.
    /// </summary>
    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            // None, not All: All adds an Accept-Encoding header, which is refused
            // outright from some IPs. See the note in Program.cs — and keep the two
            // in step, or this test stops testing what the app actually does.
            AutomaticDecompression = DecompressionMethods.None,
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls13,
            },
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        // TryAddWithoutValidation, as in Program.cs: Add would reformat these and
        // send different bytes than the app does.
        var headers = client.DefaultRequestHeaders;
        headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        headers.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        headers.TryAddWithoutValidation("Accept-Language", "sk-SK,sk;q=0.9,en;q=0.8");
        headers.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Linux\"");
        headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

        return client;
    }
}
