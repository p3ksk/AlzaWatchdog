using AlzaWatchdog.Api.Scraping;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlzaWatchdog.Tests;

/// <summary>
/// The rule shared by the sweep and by adding a product: one retry, and only for
/// a Cloudflare challenge.
/// </summary>
public class ChallengeRetryTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(1);

    private static Task<ScrapeResult> Run(QueuedScraper scraper, TimeSpan? delay = null) =>
        ChallengeRetry.FetchAsync(
            scraper, "https://www.alza.sk/x-d1.htm", delay ?? Delay, NullLogger.Instance, CancellationToken.None);

    [Fact]
    public async Task Does_not_retry_a_request_that_worked()
    {
        var scraper = new QueuedScraper(new ScrapeResult(ScrapeStatus.Success));

        var result = await Run(scraper);

        Assert.Equal(ScrapeStatus.Success, result.Status);
        Assert.Equal(1, scraper.Calls);
    }

    [Fact]
    public async Task Retries_a_challenge_once_and_returns_what_the_retry_got()
    {
        var scraper = new QueuedScraper(
            new ScrapeResult(ScrapeStatus.Blocked), new ScrapeResult(ScrapeStatus.Success));

        var result = await Run(scraper);

        Assert.Equal(ScrapeStatus.Success, result.Status);
        Assert.Equal(2, scraper.Calls);
    }

    [Fact]
    public async Task Gives_up_after_a_second_challenge()
    {
        var scraper = new QueuedScraper(
            new ScrapeResult(ScrapeStatus.Blocked), new ScrapeResult(ScrapeStatus.Blocked));

        var result = await Run(scraper);

        // Two refusals mean we really are being turned away; a third request would
        // only lean harder on a site that has already said no twice.
        Assert.Equal(ScrapeStatus.Blocked, result.Status);
        Assert.Equal(2, scraper.Calls);
    }

    [Theory]
    [InlineData(ScrapeStatus.ProductNotFound)]
    [InlineData(ScrapeStatus.ParseFailed)]
    [InlineData(ScrapeStatus.TransientError)]
    public async Task Retries_nothing_but_a_challenge(ScrapeStatus status)
    {
        // A 404 is an answer, not a refusal, and the transient case is already
        // covered by the resilience handler on the client itself.
        var scraper = new QueuedScraper(new ScrapeResult(status));

        var result = await Run(scraper);

        Assert.Equal(status, result.Status);
        Assert.Equal(1, scraper.Calls);
    }

    [Fact]
    public async Task A_zero_delay_turns_the_retry_off()
    {
        var scraper = new QueuedScraper(new ScrapeResult(ScrapeStatus.Blocked));

        var result = await Run(scraper, TimeSpan.Zero);

        Assert.Equal(ScrapeStatus.Blocked, result.Status);
        Assert.Equal(1, scraper.Calls);
    }

    private sealed class QueuedScraper(params ScrapeResult[] results) : IAlzaScraper
    {
        private readonly Queue<ScrapeResult> _results = new(results);

        public int Calls { get; private set; }

        public Task<ScrapeResult> FetchAsync(string url, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : throw new InvalidOperationException("More requests were made than the test prepared."));
        }
    }
}
