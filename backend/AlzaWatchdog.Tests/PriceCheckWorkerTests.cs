using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Scraping;
using AlzaWatchdog.Api.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Tests;

/// <summary>
/// Covers what a sweep does when Cloudflare turns it away.
///
/// The opening request of a sweep carries no Cloudflare cookies and gets
/// challenged often enough that treating it as fatal loses whole sweeps to a site
/// that is answering normally. It buys one retry; a refusal further in does not.
/// </summary>
public class PriceCheckWorkerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;
    private readonly QueuedScraper _scraper = new();

    private readonly WatchdogOptions _options = new()
    {
        CheckInterval = TimeSpan.FromHours(6),
        DelayBetweenRequests = TimeSpan.Zero,
        RequestJitter = TimeSpan.Zero,
        ChallengeRetryDelay = TimeSpan.FromMilliseconds(1),
        SweepWindow = TimeSpan.FromMinutes(5),
    };

    public PriceCheckWorkerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext, SqliteAppDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IOptions<WatchdogOptions>>(Options.Create(_options));
        services.AddSingleton<IAlzaScraper>(_scraper);
        services.AddScoped<PriceUpdateService>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    private void AddProduct(string code, DateTimeOffset? lastCheckedAt = null, bool watched = true)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Product
        {
            Id = Guid.NewGuid(),
            ProductCode = code,
            CanonicalUrl = $"https://www.alza.sk/x-d{code}.htm",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            LastCheckedAt = lastCheckedAt,
        };

        db.Products.Add(product);

        if (watched)
        {
            var userId = Guid.NewGuid();
            var listId = Guid.NewGuid();

            db.Users.Add(new User { Id = userId, CreatedAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow });
            db.WatchLists.Add(new WatchList { Id = listId, UserId = userId, Name = "List", CreatedAt = DateTimeOffset.UtcNow });
            db.TrackedItems.Add(new TrackedItem
            {
                Id = Guid.NewGuid(),
                WatchListId = listId,
                ProductId = product.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        db.SaveChanges();
    }

    private PriceCheckWorker CreateWorker() => new(
        _services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(_options),
        new WorkerStatusRegistry(),
        NullLogger<PriceCheckWorker>.Instance);

    private static ScrapeResult Blocked() => new(ScrapeStatus.Blocked, Error: "Cloudflare challenge (403)");

    private static ScrapeResult Success(decimal price) =>
        new(ScrapeStatus.Success, Name: "Thing", Price: price, Currency: "EUR", Availability: "InStock");

    [Fact]
    public async Task Retries_the_opening_request_once_when_it_is_challenged()
    {
        AddProduct("111");
        AddProduct("222");
        _scraper.Enqueue(Blocked(), Success(10m), Success(20m));

        var blocked = await CreateWorker().RunSweepAsync(CancellationToken.None);

        Assert.False(blocked);
        Assert.Equal(3, _scraper.Calls);

        using var scope = _services.CreateScope();
        var prices = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Products.OrderBy(p => p.ProductCode).Select(p => p.LastPrice).ToListAsync();

        Assert.Equal([10m, 20m], prices);
    }

    [Fact]
    public async Task Gives_up_when_the_retry_is_challenged_too()
    {
        AddProduct("111");
        AddProduct("222");
        _scraper.Enqueue(Blocked(), Blocked(), Success(10m));

        var blocked = await CreateWorker().RunSweepAsync(CancellationToken.None);

        Assert.True(blocked);

        // Two attempts at the first product and then nothing: the second product
        // must not be touched once we know we are being turned away.
        Assert.Equal(2, _scraper.Calls);
    }

    [Fact]
    public async Task Does_not_retry_a_block_once_the_sweep_is_under_way()
    {
        AddProduct("111");
        AddProduct("222");
        AddProduct("333");
        _scraper.Enqueue(Success(10m), Blocked(), Success(30m));

        var blocked = await CreateWorker().RunSweepAsync(CancellationToken.None);

        Assert.True(blocked);

        // A block after a success means something changed, so it stands the sweep
        // down immediately rather than spending another request on it.
        Assert.Equal(2, _scraper.Calls);
    }

    [Fact]
    public async Task A_blocked_product_stays_due()
    {
        AddProduct("111");
        _scraper.Enqueue(Blocked(), Blocked());

        await CreateWorker().RunSweepAsync(CancellationToken.None);

        using var scope = _services.CreateScope();
        var product = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Products.SingleAsync();

        // Stamping LastCheckedAt on a block would defer the product by a whole
        // interval, so a run of blocks would quietly drain the sweep.
        Assert.Null(product.LastCheckedAt);
    }

    [Fact]
    public async Task Checks_a_product_nobody_watches_like_any_other()
    {
        // Kept for its history when the last list dropped it, and checked on the
        // same schedule as everything else — a second interval for these was more
        // machinery than one request every six hours is worth.
        AddProduct("111", DateTimeOffset.UtcNow.AddHours(-7), watched: false);
        _scraper.Enqueue(Success(10m));

        await CreateWorker().RunSweepAsync(CancellationToken.None);

        Assert.Equal(1, _scraper.Calls);

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The point of keeping the product at all: whoever tracks it next inherits
        // a chart rather than starting from nothing.
        Assert.Single(db.PriceSnapshots.ToList());
        Assert.Single(db.Products.ToList());
    }

    [Fact]
    public async Task Leaves_a_product_that_is_not_yet_due()
    {
        AddProduct("111", DateTimeOffset.UtcNow.AddHours(-1));

        var blocked = await CreateWorker().RunSweepAsync(CancellationToken.None);

        Assert.False(blocked);
        Assert.Equal(0, _scraper.Calls);
    }

    [Fact]
    public async Task Sweeps_products_due_near_each_other_together()
    {
        // Staggered by seconds, exactly as a previous sweep would have left them.
        // Each is due at a slightly different moment, and checking them in three
        // separate sweeps would skip the pause between requests altogether.
        var checkedAt = DateTimeOffset.UtcNow.AddHours(-6);
        AddProduct("111", checkedAt);
        AddProduct("222", checkedAt.AddSeconds(6));
        AddProduct("333", checkedAt.AddSeconds(12));
        _scraper.Enqueue(Success(10m), Success(20m), Success(30m));

        await CreateWorker().RunSweepAsync(CancellationToken.None);

        Assert.Equal(3, _scraper.Calls);
    }

    [Fact]
    public async Task Leaves_a_product_that_is_not_nearly_due()
    {
        AddProduct("111", DateTimeOffset.UtcNow.AddHours(-6));
        // An hour short of due is well outside the window and must wait.
        AddProduct("222", DateTimeOffset.UtcNow.AddHours(-5));
        _scraper.Enqueue(Success(10m));

        await CreateWorker().RunSweepAsync(CancellationToken.None);

        Assert.Equal(1, _scraper.Calls);
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Hands back a prepared sequence of results and counts the calls.</summary>
    private sealed class QueuedScraper : IAlzaScraper
    {
        private readonly Queue<ScrapeResult> _results = new();

        public int Calls { get; private set; }

        public void Enqueue(params ScrapeResult[] results)
        {
            foreach (var result in results)
                _results.Enqueue(result);
        }

        public Task<ScrapeResult> FetchAsync(string url, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : throw new InvalidOperationException("The sweep made more requests than the test prepared."));
        }    }
}
