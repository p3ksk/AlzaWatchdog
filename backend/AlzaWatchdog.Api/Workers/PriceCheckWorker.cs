using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Scraping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Api.Workers;

/// <summary>
/// The watchdog itself: re-checks every tracked product on an interval.
///
/// Deliberately slow and serial: one request per product however many lists watch
/// it, spaced with jitter. A block aborts the sweep, save for one retry of the
/// opening request, which is challenged often enough that treating it as fatal
/// loses sweeps to a site that is answering perfectly well.
/// </summary>
public class PriceCheckWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WatchdogOptions> options,
    WorkerStatusRegistry status,
    ILogger<PriceCheckWorker> logger) : BackgroundService
{
    private readonly WatchdogOptions _options = options.Value;

    public const string WorkerName = "Price check";

    private int _runs;
    private string? _lastOutcome;
    private DateTimeOffset? _lastRunAt;

    /// <summary>Floor on the sleep between sweeps, so the loop can never spin.</summary>
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Report(null);

        if (!_options.Enabled)
        {
            logger.LogInformation("Price check worker is disabled by configuration.");
            return;
        }

        logger.LogInformation(
            "Price check worker started; sweeping every {Interval}.", _options.CheckInterval);

        while (!ct.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                var blocked = await RunSweepAsync(ct);

                // Standing down longer than usual keeps a restart loop or a bad
                // patch from turning into sustained hammering.
                wait = blocked ? _options.BlockedBackoff : await TimeUntilNextDueAsync(ct);

                _runs++;
                _lastRunAt = DateTimeOffset.UtcNow;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Price check sweep failed; retrying after the normal interval.");
                _lastOutcome = $"Failed: {ex.Message}";
                _lastRunAt = DateTimeOffset.UtcNow;
                wait = _options.CheckInterval;
            }

            logger.LogInformation("Next sweep in {Wait}.", wait);
            Report(DateTimeOffset.UtcNow + wait);

            try
            {
                await Task.Delay(wait, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <returns>True when the sweep was cut short because alza.sk blocked us.</returns>
    internal async Task<bool> RunSweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scraper = scope.ServiceProvider.GetRequiredService<IAlzaScraper>();
        var updater = scope.ServiceProvider.GetRequiredService<PriceUpdateService>();

        // Anything falling due within the window comes along with this sweep, so
        // products stay batched instead of each drifting onto its own schedule.
        var due = DateTimeOffset.UtcNow - _options.CheckInterval + _options.SweepWindow;

        // A product nobody watches is kept for its history and checked like any
        // other; one request an interval is not worth a second schedule.
        var products = await db.Products
            .Where(p => p.IsActive && (p.LastCheckedAt == null || p.LastCheckedAt < due))
            .OrderBy(p => p.LastCheckedAt)
            .ToListAsync(ct);

        if (products.Count == 0)
        {
            logger.LogDebug("Nothing due for a price check.");
            _lastOutcome = "Nothing was due";
            return false;
        }

        logger.LogInformation("Checking {Count} product(s).", products.Count);
        _lastOutcome = $"Checked {products.Count} product(s)";

        var first = true;
        foreach (var product in products)
        {
            if (ct.IsCancellationRequested)
                break;

            var opening = first;

            if (!first)
                await Task.Delay(NextDelay(), ct);
            first = false;

            // Only the opening request retries: it goes out with a cold cookie jar,
            // the state Cloudflare challenges. A later block is a different signal.
            var result = opening
                ? await ChallengeRetry.FetchAsync(
                    scraper, product.CanonicalUrl, _options.ChallengeRetryDelay, logger, ct)
                : await scraper.FetchAsync(product.CanonicalUrl, ct);

            updater.Apply(db, product, result, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);

            if (result.Status == ScrapeStatus.Blocked)
            {
                logger.LogWarning(
                    "Blocked by alza.sk; abandoning this sweep and backing off for {Backoff}.",
                    _options.BlockedBackoff);
                _lastOutcome = $"Blocked by alza.sk on {product.ProductCode}; backing off";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sleeps until the earliest product actually falls due, not a fixed period
    /// from process start. Treating those as the same made a six-hour interval
    /// behave like twelve, because an item checked minutes after a sweep was still
    /// short of due at the next one and waited a whole extra interval.
    /// </summary>
    private async Task<TimeSpan> TimeUntilNextDueAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lastChecked = await db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.LastCheckedAt)
            .ToListAsync(ct);

        if (lastChecked.Count == 0)
            return _options.CheckInterval;

        // Anything never checked is due immediately.
        if (lastChecked.Any(t => t is null))
            return MinimumDelay;

        // A window early, matching what the sweep selects by, so whatever the
        // worker wakes for is certain to qualify.
        var nextDue = lastChecked.Min()!.Value + _options.CheckInterval - _options.SweepWindow;
        var wait = nextDue - DateTimeOffset.UtcNow;

        // Two sweeps in a row are two requests, so they are held to the same pause
        // as two requests inside one sweep.
        var floor = _options.DelayBetweenRequests > MinimumDelay
            ? _options.DelayBetweenRequests
            : MinimumDelay;

        if (wait < floor)
            return floor;

        return wait > _options.CheckInterval ? _options.CheckInterval : wait;
    }

    private void Report(DateTimeOffset? nextRunAt) =>
        status.Set(new WorkerStatus(
            WorkerName,
            "Re-checks each product's price on a schedule, one request per product.",
            _options.Enabled,
            _lastRunAt,
            nextRunAt,
            _lastOutcome,
            _runs,
            [
                new("Check interval", Describe(_options.CheckInterval),
                    "How old a product's last check has to be before the worker fetches it again. The worker sleeps until the earliest product is actually due, not on a fixed timer."),
                new("Between requests", $"{Describe(_options.DelayBetweenRequests)} + up to {Describe(_options.RequestJitter)} jitter",
                    "Pause after each product page, plus a random extra, so a sweep reaches alza.sk as a trickle rather than a burst. It also floors the gap between two sweeps, since those are two requests just the same."),
                new("Swept together", Describe(_options.SweepWindow),
                    "Products falling due within this long of each other are checked in one sweep. Without it each product drifts onto its own schedule, sweeps end up carrying a single product, and the pause above — which only applies between products in one sweep — never happens."),
                new("Backoff when blocked", Describe(_options.BlockedBackoff),
                    "When Cloudflare refuses a request the rest of the sweep is abandoned and the worker waits this long. Products it never reached stay due, so nothing is skipped."),
                new("Retry opening challenge", _options.ChallengeRetryDelay > TimeSpan.Zero
                        ? $"after {Describe(_options.ChallengeRetryDelay)}"
                        : "off",
                    "A sweep's first request carries no Cloudflare cookies yet and is the one most likely to be challenged. That first refusal buys one retry instead of costing the whole sweep; a refusal later in the sweep does not."),
                new("Retry when adding", _options.InteractiveChallengeRetryDelay > TimeSpan.Zero
                        ? $"after {Describe(_options.InteractiveChallengeRetryDelay)}"
                        : "off",
                    "The same single retry for the scrape someone triggers by adding a product. Shorter, because a person is waiting on it rather than a background sweep."),
                new("Pause after failures", _options.MaxConsecutiveFailures.ToString(),
                    "After this many failed checks in a row a product is deactivated and stops being fetched, until someone resumes it from their list."),
            ]));

    internal static string Describe(TimeSpan value) => value switch
    {
        { TotalDays: >= 1 } => $"{value.TotalDays:0.#} d",
        { TotalHours: >= 1 } => $"{value.TotalHours:0.#} h",
        { TotalMinutes: >= 1 } => $"{value.TotalMinutes:0.#} min",
        _ => $"{value.TotalSeconds:0.#} s",
    };

    private TimeSpan NextDelay() =>
        _options.DelayBetweenRequests
        + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * _options.RequestJitter.TotalMilliseconds);
}
