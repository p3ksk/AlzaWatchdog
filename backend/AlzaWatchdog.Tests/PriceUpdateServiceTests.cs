using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Scraping;
using AlzaWatchdog.Api.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Tests;

public class PriceUpdateServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteAppDbContext _db;
    private readonly PriceUpdateService _updater;
    private readonly WatchdogOptions _options = new() { MaxConsecutiveFailures = 3 };
    private readonly Product _item;

    public PriceUpdateServiceTests()
    {
        // A real (in-memory) SQLite database, so the decimal-as-text conversion is
        // exercised rather than bypassed by the EF in-memory provider.
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();

        _updater = new PriceUpdateService(Options.Create(_options));

        var user = new User { Id = Guid.NewGuid(), CreatedAt = Now(0), LastSeenAt = Now(0) };
        var list = new WatchList
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "Test list",
            CreatedAt = Now(0),
        };

        // Prices and history belong to the product now, so that is what the
        // update service is pointed at.
        _item = new Product
        {
            Id = Guid.NewGuid(),
            ProductCode = "10818009",
            CanonicalUrl = "https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm",
            CreatedAt = Now(0),
        };

        _db.Users.Add(user);
        _db.WatchLists.Add(list);
        _db.Products.Add(_item);
        _db.TrackedItems.Add(new TrackedItem
        {
            Id = Guid.NewGuid(),
            WatchListId = list.Id,
            ProductId = _item.Id,
            CreatedAt = Now(0),
        });
        _db.SaveChanges();
    }

    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Now(int minutes) => Origin.AddMinutes(minutes);

    // Named arguments on purpose: ScrapeResult is a positional record, and every
    // new price field would otherwise silently shift what these tests pass.
    private static ScrapeResult Priced(
        decimal price,
        string availability = "InStock",
        decimal? plusPrice = null,
        decimal? couponPrice = null) =>
        new(
            ScrapeStatus.Success,
            Name: "CUDY N300 WiFi Router",
            Price: price,
            PlusPrice: plusPrice,
            CouponPrice: couponPrice,
            Currency: "EUR",
            Availability: availability);

    private void Apply(ScrapeResult result, int atMinute)
    {
        _updater.Apply(_db, _item, result, Now(atMinute));
        _db.SaveChanges();
    }

    private List<PriceSnapshot> Snapshots() =>
        _db.PriceSnapshots.OrderBy(s => s.CapturedAt).ToList();

    [Fact]
    public void First_successful_check_records_a_snapshot()
    {
        Apply(Priced(18.90m), 0);

        var snapshot = Assert.Single(Snapshots());
        Assert.Equal(18.90m, snapshot.Price);
        Assert.Equal(18.90m, _item.LastPrice);
        Assert.Equal("EUR", _item.Currency);
        Assert.Equal(Now(0), _item.LastCheckedAt);
    }

    [Fact]
    public void Unchanged_price_advances_the_check_time_without_adding_history()
    {
        Apply(Priced(18.90m), 0);
        Apply(Priced(18.90m), 10);
        Apply(Priced(18.90m), 20);

        // The whole point of the snapshot rule: history is a list of transitions,
        // not one row per poll.
        Assert.Single(Snapshots());
        Assert.Equal(Now(20), _item.LastCheckedAt);
    }

    [Fact]
    public void Changed_price_appends_history()
    {
        Apply(Priced(18.90m), 0);
        Apply(Priced(16.50m), 10);

        Assert.Equal([18.90m, 16.50m], Snapshots().Select(s => s.Price));
        Assert.Equal(16.50m, _item.LastPrice);
    }

    [Fact]
    public void Availability_change_alone_appends_history()
    {
        Apply(Priced(18.90m), 0);
        Apply(Priced(18.90m, "OutOfStock"), 10);

        Assert.Equal(2, Snapshots().Count);
        Assert.Equal("OutOfStock", _item.LastAvailability);
    }

    [Fact]
    public void A_members_price_appearing_on_its_own_appends_history()
    {
        Apply(Priced(18.90m), 0);
        Apply(Priced(18.90m, plusPrice: 16.90m), 10);

        // The shelf price did not move, but what you can actually pay did.
        Assert.Equal(2, Snapshots().Count);
        Assert.Equal(16.90m, Snapshots()[^1].PlusPrice);
        Assert.Equal(16.90m, _item.LastPlusPrice);
    }

    [Fact]
    public void A_coupon_price_appearing_on_its_own_appends_history()
    {
        Apply(Priced(203.90m), 0);
        Apply(Priced(203.90m, couponPrice: 142.73m), 10);

        Assert.Equal(2, Snapshots().Count);
        Assert.Equal(142.73m, Snapshots()[^1].CouponPrice);
        Assert.Equal(142.73m, _item.LastCouponPrice);
    }

    [Fact]
    public void A_discount_disappearing_appends_history_too()
    {
        Apply(Priced(203.90m, couponPrice: 142.73m), 0);
        Apply(Priced(203.90m), 10);

        Assert.Equal(2, Snapshots().Count);
        Assert.Null(_item.LastCouponPrice);
    }

    [Fact]
    public void Unchanged_discounts_still_add_no_history()
    {
        Apply(Priced(12.20m, plusPrice: 10.98m, couponPrice: 11.50m), 0);
        Apply(Priced(12.20m, plusPrice: 10.98m, couponPrice: 11.50m), 10);
        Apply(Priced(12.20m, plusPrice: 10.98m, couponPrice: 11.50m), 20);

        Assert.Single(Snapshots());
    }

    [Fact]
    public void Repeated_failures_deactivate_the_item()
    {
        Apply(Priced(18.90m), 0);

        for (var i = 1; i <= _options.MaxConsecutiveFailures; i++)
            Apply(ScrapeResult.Failure(ScrapeStatus.TransientError, "boom"), i);

        Assert.False(_item.IsActive);
        Assert.Equal("boom", _item.LastError);

        // The last good price stays on the item so the UI still has something to show.
        Assert.Equal(18.90m, _item.LastPrice);
        Assert.Single(Snapshots());
    }

    [Fact]
    public void A_success_clears_an_earlier_failure()
    {
        Apply(ScrapeResult.Failure(ScrapeStatus.TransientError, "boom"), 0);
        Assert.Equal(1, _item.ConsecutiveFailures);

        Apply(Priced(18.90m), 10);

        Assert.Null(_item.LastError);
        Assert.Equal(0, _item.ConsecutiveFailures);
    }

    [Fact]
    public void Being_blocked_leaves_the_item_due()
    {
        Apply(Priced(18.90m), 0);
        var checkedAt = _item.LastCheckedAt;

        Apply(ScrapeResult.Failure(ScrapeStatus.Blocked, "blocked"), 10);

        // We never saw the product, so it must stay due. Advancing this would hide
        // it from the next sweep for a whole interval — a run of blocks would
        // otherwise drain the list one product at a time.
        Assert.Equal(checkedAt, _item.LastCheckedAt);
        Assert.Equal("blocked", _item.LastError);
        Assert.Single(Snapshots());
    }

    [Fact]
    public void An_ordinary_failure_still_counts_as_a_check()
    {
        Apply(Priced(18.90m), 0);

        Apply(ScrapeResult.Failure(ScrapeStatus.ProductNotFound, "gone"), 10);

        // A 404 is information about the product, unlike a block, so the check
        // genuinely happened and the schedule should move on.
        Assert.Equal(Now(10), _item.LastCheckedAt);
    }

    [Fact]
    public void Being_blocked_never_deactivates_an_item()
    {
        // A block says nothing about this product — it is about us. Counting it
        // would let one Cloudflare episode switch off the user's entire list.
        for (var i = 0; i < _options.MaxConsecutiveFailures * 3; i++)
            Apply(ScrapeResult.Failure(ScrapeStatus.Blocked, "blocked"), i);

        Assert.True(_item.IsActive);
        Assert.Equal(0, _item.ConsecutiveFailures);
    }

    [Fact]
    public void Prices_survive_the_round_trip_through_sqlite_exactly()
    {
        Apply(Priced(1234.56m), 0);

        // Re-read from the database rather than the tracked instance.
        using var fresh = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite(_connection).Options);

        Assert.Equal(1234.56m, fresh.Products.Single().LastPrice);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
