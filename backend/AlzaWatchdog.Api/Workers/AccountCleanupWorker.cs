using AlzaWatchdog.Api.Admin;
using AlzaWatchdog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Api.Workers;

/// <summary>
/// Deletes abandoned accounts on a schedule.
///
/// Two limits, because the two cases are not alike. An account with nothing on it
/// is debris — a visit that never became anything — and can go after a couple of
/// days. An account with products is somebody's watch list, and since the only way
/// back to it is a bookmarked URL, it may legitimately go untouched for months; it
/// gets a far longer grace period.
///
/// Deleting a user cascades to their lists, items and price history through the
/// foreign keys, so one delete is enough.
/// </summary>
public class AccountCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CleanupOptions> options,
    IOptions<AdminOptions> adminOptions,
    WorkerStatusRegistry status,
    ILogger<AccountCleanupWorker> logger) : BackgroundService
{
    private readonly CleanupOptions _options = options.Value;

    public const string WorkerName = "Account cleanup";

    private int _runs;
    private string? _lastOutcome;
    private DateTimeOffset? _lastRunAt;

    /// <summary>Deleted in batches so a large purge cannot build an unbounded SQL parameter list.</summary>
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Report(null);

        if (!_options.Enabled)
        {
            logger.LogInformation("Account cleanup is disabled by configuration.");
            return;
        }

        logger.LogInformation(
            "Account cleanup every {Interval}: empty accounts after {Empty}, accounts with products after {Inactive}.{DryRun}",
            _options.Interval,
            _options.EmptyAccountAge,
            _options.InactiveAccountAge,
            _options.DryRun ? " Dry run — nothing will be deleted." : string.Empty);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Account cleanup failed; retrying next interval.");
                _lastOutcome = $"Failed: {ex.Message}";
            }

            _runs++;
            _lastRunAt = DateTimeOffset.UtcNow;
            Report(DateTimeOffset.UtcNow + _options.Interval);

            try
            {
                await Task.Delay(_options.Interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Report(DateTimeOffset? nextRunAt) =>
        status.Set(new WorkerStatus(
            WorkerName,
            "Removes abandoned accounts, and any product left with nobody watching it.",
            _options.Enabled,
            _lastRunAt,
            nextRunAt,
            _lastOutcome,
            _runs,
            [
                new("Runs every", PriceCheckWorker.Describe(_options.Interval),
                    "How often the sweep looks for accounts to remove. It touches only the database, so it is cheap to run often."),
                new("Empty accounts after", PriceCheckWorker.Describe(_options.EmptyAccountAge),
                    "An account that never had a product added is deleted once it has gone unvisited this long — these are almost always someone who opened the page and left."),
                new("Accounts with products after", PriceCheckWorker.Describe(_options.InactiveAccountAge),
                    "An account that is actually tracking something survives far longer, because losing it means losing its price history for good."),
                new("Dry run", _options.DryRun ? "yes — nothing is deleted" : "no",
                    "When on, the worker logs exactly what it would delete and then deletes nothing. Use it to check the thresholds before letting it act."),
            ]));

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var emptyCutoff = now - _options.EmptyAccountAge;
        var inactiveCutoff = now - _options.InactiveAccountAge;

        // Narrow in SQL with whichever limit is the more lenient, so the in-memory
        // pass below always sees a superset. Taking the smaller age would drop rows
        // the other rule still wants if the two are ever configured the other way up.
        var widest = emptyCutoff > inactiveCutoff ? emptyCutoff : inactiveCutoff;

        var candidates = await db.Users
            .AsNoTracking()
            .Where(u => u.LastSeenAt < widest)
            .Select(u => new
            {
                u.Id,
                u.LastSeenAt,
                ItemCount = u.Lists.Sum(l => l.Items.Count),
            })
            .ToListAsync(ct);

        var admins = adminOptions.Value.ParsedKeys;

        var doomed = candidates
            // An administrator's own account must survive its own cleanup, however
            // long they have been away.
            .Where(u => !admins.Contains(u.Id))
            .Where(u => u.ItemCount == 0
                ? u.LastSeenAt < emptyCutoff
                : u.LastSeenAt < inactiveCutoff)
            .ToList();

        if (doomed.Count == 0)
        {
            logger.LogDebug("Account cleanup: nothing to remove.");
            _lastOutcome = "Nothing to remove";
            return;
        }

        var empty = doomed.Count(u => u.ItemCount == 0);

        if (_options.DryRun)
        {
            logger.LogInformation(
                "Account cleanup (dry run) would delete {Total} account(s): {Empty} empty, {WithProducts} with products.",
                doomed.Count, empty, doomed.Count - empty);
            _lastOutcome = $"Dry run: would delete {doomed.Count} account(s)";
            return;
        }

        var deleted = 0;
        foreach (var batch in doomed.Chunk(BatchSize))
        {
            var ids = batch.Select(u => u.Id).ToList();
            deleted += await db.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync(ct);
        }

        // Products outlive the lists that referenced them, so a purge can leave
        // rows nobody watches. They would otherwise be swept forever, costing
        // requests to alza.sk for nobody's benefit.
        var orphans = await db.Products
            .Where(p => !p.TrackedBy.Any())
            .ExecuteDeleteAsync(ct);

        // Logged at Information because it is destructive and irreversible: if an
        // account vanishes, this line is the only record that it was deliberate.
        logger.LogInformation(
            "Account cleanup deleted {Total} account(s): {Empty} empty (idle since before {EmptyCutoff:u}), " +
            "{WithProducts} with products (idle since before {InactiveCutoff:u}). " +
            "Also removed {Orphans} product(s) nobody watches.",
            deleted, empty, emptyCutoff, doomed.Count - empty, inactiveCutoff, orphans);

        _lastOutcome = $"Deleted {deleted} account(s) and {orphans} unwatched product(s)";
    }
}
