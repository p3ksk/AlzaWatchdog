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
/// rather than pushing through it, save for one retry of the opening request,
/// which is challenged often enough that treating it as fatal loses sweeps to a
/// site that is answering perfectly well.
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

    /// <summary>Overshoot past an item's due time, to stay clear of the boundary.</summary>
    private static readonly TimeSpan DueGrace = TimeSpan.FromSeconds(2);

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

        var due = DateTimeOffset.UtcNow - _options.CheckInterval;

        // Products are stored once and shared by every list that watches them, so
        // "one request per product however many people track it" is now a property
        // of the schema rather than something this query has to arrange.
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

            var result = await scraper.FetchAsync(product.CanonicalUrl, ct);

            // A sweep's opening request goes out with a cold cookie jar, which is
            // the state Cloudflare challenges: measured from this host, ten
            // consecutive fetches holding __cf_bm and _cfuvid were all let
            // through, while cookie-less ones in the same minute were turned away.
            // Standing down for half an hour over that costs a whole sweep, so the
            // opening challenge buys exactly one retry — by which point the jar has
            // whatever the refusal handed back. A block once the jar is warm is a
            // different signal and still stands the sweep down.
            if (result.Status == ScrapeStatus.Blocked && opening && _options.ChallengeRetryDelay > TimeSpan.Zero)
            {
                logger.LogInformation(
                    "Opening request was challenged; retrying once in {Delay}.",
                    _options.ChallengeRetryDelay);

                await Task.Delay(_options.ChallengeRetryDelay, ct);
                result = await scraper.FetchAsync(product.CanonicalUrl, ct);

                // Logged either way: this line is the only evidence of whether the
                // retry is worth making.
                logger.LogInformation("Retry after the opening challenge: {Status}.", result.Status);
            }

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

        // Aim a moment past the due time. Landing exactly on it would leave the
        // sweep's own "older than CheckInterval" test true only by sub-millisecond
        // margins — the same boundary that caused the skipping in the first place.
        var nextDue = lastChecked.Min()!.Value + _options.CheckInterval + DueGrace;
        var wait = nextDue - DateTimeOffset.UtcNow;

        if (wait < MinimumDelay)
            return MinimumDelay;

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
                    "Pause after each product page, plus a random extra, so a sweep reaches alza.sk as a trickle rather than a burst."),
                new("Backoff when blocked", Describe(_options.BlockedBackoff),
                    "When Cloudflare refuses a request the rest of the sweep is abandoned and the worker waits this long. Products it never reached stay due, so nothing is skipped."),
                new("Retry opening challenge", _options.ChallengeRetryDelay > TimeSpan.Zero
                        ? $"after {Describe(_options.ChallengeRetryDelay)}"
                        : "off",
                    "A sweep's first request carries no Cloudflare cookies yet and is the one most likely to be challenged. That first refusal buys one retry instead of costing the whole sweep; a refusal later in the sweep does not."),
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
