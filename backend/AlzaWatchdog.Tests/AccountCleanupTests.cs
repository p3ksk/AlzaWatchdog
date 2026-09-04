using AlzaWatchdog.Api.Admin;
using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Tests;

/// <summary>
/// Exercises the cleanup rules against a real SQLite database. The deletion is
/// irreversible and relies on foreign-key cascades firing, so this uses a real
/// engine rather than an in-memory fake that would happily pretend they did.
/// </summary>
public class AccountCleanupTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteAppDbContext _db;

    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly CleanupOptions _options = new()
    {
        EmptyAccountAge = TimeSpan.FromDays(2),
        InactiveAccountAge = TimeSpan.FromDays(182),
    };

    public AccountCleanupTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();
    }

    /// <summary>Creates a user last seen <paramref name="daysAgo"/> ago, with or without a product.</summary>
    private Guid AddUser(double daysAgo, bool withProduct, Guid? id = null)
    {
        var userId = id ?? Guid.NewGuid();
        var seen = Now.AddDays(-daysAgo);

        var list = new WatchList { Id = Guid.NewGuid(), UserId = userId, Name = "List", CreatedAt = seen };
        _db.Users.Add(new User { Id = userId, CreatedAt = seen, LastSeenAt = seen });
        _db.WatchLists.Add(list);

        if (withProduct)
        {
            // A product per user here, so deleting one account cannot take another
            // account's product with it and make the cascade test lie.
            var product = new Product
            {
                Id = Guid.NewGuid(),
                ProductCode = userId.ToString("N")[..8],
                CanonicalUrl = "https://www.alza.sk/x-d1.htm",
                CreatedAt = seen,
            };
            _db.Products.Add(product);
            _db.TrackedItems.Add(new TrackedItem
            {
                Id = Guid.NewGuid(),
                WatchListId = list.Id,
                ProductId = product.Id,
                CreatedAt = seen,
            });
            _db.PriceSnapshots.Add(new PriceSnapshot
            {
                ProductId = product.Id, Price = 1m, Availability = "InStock", CapturedAt = seen,
            });
        }

        _db.SaveChanges();
        return userId;
    }

    /// <summary>Mirrors the worker's selection so the rules can be tested without a host.</summary>
    private List<Guid> SelectDoomed(params Guid[] adminKeys)
    {
        var emptyCutoff = Now - _options.EmptyAccountAge;
        var inactiveCutoff = Now - _options.InactiveAccountAge;
        var widest = emptyCutoff > inactiveCutoff ? emptyCutoff : inactiveCutoff;
        var admins = adminKeys.ToHashSet();

        return _db.Users
            .Where(u => u.LastSeenAt < widest)
            .Select(u => new { u.Id, u.LastSeenAt, ItemCount = u.Lists.Sum(l => l.Items.Count) })
            .ToList()
            .Where(u => !admins.Contains(u.Id))
            .Where(u => u.ItemCount == 0 ? u.LastSeenAt < emptyCutoff : u.LastSeenAt < inactiveCutoff)
            .Select(u => u.Id)
            .ToList();
    }

    [Fact]
    public void Empty_account_past_the_short_limit_is_removed()
    {
        var id = AddUser(daysAgo: 3, withProduct: false);
        Assert.Contains(id, SelectDoomed());
    }

    [Fact]
    public void Empty_account_inside_the_short_limit_is_kept()
    {
        var id = AddUser(daysAgo: 1, withProduct: false);
        Assert.DoesNotContain(id, SelectDoomed());
    }

    [Fact]
    public void Account_with_products_survives_the_short_limit()
    {
        // The whole point of two limits: three days idle deletes an empty account
        // but must not touch someone's actual watch list.
        var id = AddUser(daysAgo: 3, withProduct: true);
        Assert.DoesNotContain(id, SelectDoomed());
    }

    [Fact]
    public void Account_with_products_is_removed_past_the_long_limit()
    {
        var id = AddUser(daysAgo: 200, withProduct: true);
        Assert.Contains(id, SelectDoomed());
    }

    [Fact]
    public void Account_with_products_just_inside_the_long_limit_is_kept()
    {
        var id = AddUser(daysAgo: 181, withProduct: true);
        Assert.DoesNotContain(id, SelectDoomed());
    }

    [Fact]
    public void An_administrator_is_never_removed()
    {
        // An admin key that has been idle would otherwise be deleted by its own
        // cleanup, locking everyone out of the admin section.
        var admin = AddUser(daysAgo: 400, withProduct: false);
        Assert.DoesNotContain(admin, SelectDoomed(admin));
    }

    [Fact]
    public void Deleting_an_account_takes_its_lists_items_and_history_with_it()
    {
        var id = AddUser(daysAgo: 300, withProduct: true);

        var ids = new List<Guid> { id };
        _db.Users.Where(u => ids.Contains(u.Id)).ExecuteDelete();

        Assert.Empty(_db.Users.ToList());
        Assert.Empty(_db.WatchLists.ToList());
        Assert.Empty(_db.TrackedItems.ToList());

        // The product survives on purpose: it is shared, so deleting one account
        // must not remove something another account may still be watching — and it
        // is kept even once nobody watches it, so its price history is still there
        // for whoever tracks it next.
        Assert.Single(_db.Products.ToList());

        // Its history goes with the product, not with the account that had it.
        Assert.Single(_db.PriceSnapshots.ToList());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
