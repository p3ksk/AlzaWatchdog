using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Scraping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Api.Workers;

/// <summary>
/// The watchdog itself: re-checks every tracked product on an interval.
///
/// alza.sk rate-limits aggressively, so the sweep is deliberately slow and serial.
/// Requests are grouped by product code — a product watched by ten users still costs
/// exactly one request — and spaced with jitter. A block aborts the whole sweep
/// rather than pushing through it.
/// </summary>
public class PriceCheckWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WatchdogOptions> options,
    ILogger<PriceCheckWorker> logger) : BackgroundService
{
    private readonly WatchdogOptions _options = options.Value;

    /// <summary>Floor on the sleep between sweeps, so the loop can never spin.</summary>
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromSeconds(5);

    /// <summary>Overshoot past an item's due time, to stay clear of the boundary.</summary>
    private static readonly TimeSpan DueGrace = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
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
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Price check sweep failed; retrying after the normal interval.");
                wait = _options.CheckInterval;
            }

            logger.LogInformation("Next sweep in {Wait}.", wait);

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
    private async Task<bool> RunSweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scraper = scope.ServiceProvider.GetRequiredService<IAlzaScraper>();
        var updater = scope.ServiceProvider.GetRequiredService<PriceUpdateService>();

        var due = DateTimeOffset.UtcNow - _options.CheckInterval;

        // Group by product code so shared products cost one request, and skip anything
        // checked recently — on restart this stops us re-scraping the whole list.
        var products = await db.TrackedItems
            .Where(i => i.IsActive)
            .GroupBy(i => i.ProductCode)
            .Select(g => new
            {
                ProductCode = g.Key,
                // CanonicalUrl is required and groups are non-empty, so Min is never null.
                Url = g.Min(i => i.CanonicalUrl)!,
                LastCheckedAt = g.Min(i => i.LastCheckedAt),
            })
            .Where(p => p.LastCheckedAt == null || p.LastCheckedAt < due)
            .ToListAsync(ct);

        if (products.Count == 0)
        {
            logger.LogDebug("Nothing due for a price check.");
            return false;
        }

        logger.LogInformation("Checking {Count} product(s).", products.Count);

        var first = true;
        foreach (var product in products)
        {
            if (ct.IsCancellationRequested)
                break;

            if (!first)
                await Task.Delay(NextDelay(), ct);
            first = false;

            var result = await scraper.FetchAsync(product.Url, ct);

            var items = await db.TrackedItems
                .Where(i => i.IsActive && i.ProductCode == product.ProductCode)
                .ToListAsync(ct);

            var now = DateTimeOffset.UtcNow;
            foreach (var item in items)
                updater.Apply(db, item, result, now);

            await db.SaveChangesAsync(ct);

            if (result.Status == ScrapeStatus.Blocked)
            {
                logger.LogWarning(
                    "Blocked by alza.sk; abandoning this sweep and backing off for {Backoff}.",
                    _options.BlockedBackoff);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// How long to sleep before the next sweep: until the earliest item actually
    /// falls due, rather than a fixed period from process start.
    ///
    /// Those are not the same thing, and treating them as the same was a real bug.
    /// An item checked a few minutes after a sweep is still short of due at the
    /// following one, gets skipped, and then waits a whole extra interval — so a
    /// six-hour setting silently became up to twelve. Waking when something is due
    /// also makes the "next scan" time shown in the UI true rather than a guess.
    /// </summary>
    private async Task<TimeSpan> TimeUntilNextDueAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lastChecked = await db.TrackedItems
            .AsNoTracking()
            .Where(i => i.IsActive)
            .Select(i => i.LastCheckedAt)
            .ToListAsync(ct);

        if (lastChecked.Count == 0)
            return _options.CheckInterval;

        // Anything never checked is due immediately.
        if (lastChecked.Any(t => t is null))
            return MinimumDelay;

        // Aim a moment past the due time. Landing exactly on it would leave the
        // sweep's own "older than CheckInterval" test true only by sub-millisecond
        // margins — the same boundary that caused the skipping in the first place.
        var nextDue = lastChecked.Min()!.Value + _options.CheckInterval + DueGrace;
        var wait = nextDue - DateTimeOffset.UtcNow;

        if (wait < MinimumDelay)
            return MinimumDelay;

        return wait > _options.CheckInterval ? _options.CheckInterval : wait;
    }

    private TimeSpan NextDelay() =>
        _options.DelayBetweenRequests
        + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * _options.RequestJitter.TotalMilliseconds);
}
