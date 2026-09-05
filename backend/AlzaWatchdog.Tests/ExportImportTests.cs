using AlzaWatchdog.Api.Contracts;
using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Endpoints;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Tests;

/// <summary>
/// A bundle describes what was watched, not who watched it, so it has to import
/// into any account and survive being imported twice.
/// </summary>
public class ExportImportTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteAppDbContext _db;

    public ExportImportTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();
    }

    private Guid AddAccount(string listName = "My watchlist")
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        _db.Users.Add(new User { Id = userId, CreatedAt = now, LastSeenAt = now });
        _db.WatchLists.Add(new WatchList
        {
            Id = Guid.NewGuid(), UserId = userId, Name = listName, CreatedAt = now,
        });
        _db.SaveChanges();

        return userId;
    }

    private void AddWatchedProduct(Guid userId, string code)
    {
        var now = DateTimeOffset.UtcNow;
        var list = _db.WatchLists.First(l => l.UserId == userId);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            ProductCode = code,
            CanonicalUrl = $"https://www.alza.sk/x-d{code}.htm",
            Name = $"Product {code}",
            Currency = "EUR",
            LastPrice = 10m,
            IsActive = true,
            CreatedAt = now,
        };

        _db.Products.Add(product);
        _db.TrackedItems.Add(new TrackedItem
        {
            Id = Guid.NewGuid(), WatchListId = list.Id, ProductId = product.Id, CreatedAt = now,
        });
        _db.PriceSnapshots.Add(new PriceSnapshot
        {
            ProductId = product.Id, Price = 10m, Availability = "InStock", CapturedAt = now,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Imports_a_bundle_exported_by_a_different_account()
    {
        var source = AddAccount();
        AddWatchedProduct(source, "111");
        var bundle = await ExportEndpoints.BuildExportAsync(_db, source, default);

        var target = AddAccount("Somewhere else");
        var result = await ExportEndpoints.ImportAsync(_db, target, bundle, default);

        Assert.Equal(1, result.ItemsImported);

        // The product is shared, so the second account watches the same row.
        Assert.Single(_db.Products.ToList());
        Assert.Equal(2, _db.TrackedItems.Count());
    }

    [Fact]
    public async Task Importing_the_same_bundle_twice_skips_instead_of_colliding()
    {
        var source = AddAccount();
        AddWatchedProduct(source, "111");
        var bundle = await ExportEndpoints.BuildExportAsync(_db, source, default);

        var target = AddAccount("Elsewhere");
        await ExportEndpoints.ImportAsync(_db, target, bundle, default);

        // Reusing the exported tracked-item id made this throw on the primary key.
        var second = await ExportEndpoints.ImportAsync(_db, target, bundle, default);

        Assert.Equal(0, second.ItemsImported);
        Assert.Equal(1, second.ItemsSkipped);
        Assert.Equal(2, _db.TrackedItems.Count());
    }

    [Fact]
    public async Task Reuses_a_product_that_is_already_in_the_database()
    {
        var source = AddAccount();
        AddWatchedProduct(source, "111");
        var bundle = await ExportEndpoints.BuildExportAsync(_db, source, default);

        var target = AddAccount("Elsewhere");
        var result = await ExportEndpoints.ImportAsync(_db, target, bundle, default);

        // One product row, and its history is not copied a second time.
        Assert.Single(_db.Products.ToList());
        Assert.Single(_db.PriceSnapshots.ToList());
        Assert.Equal(0, result.SnapshotsImported);
    }

    [Fact]
    public async Task Creates_a_missing_list_and_brings_the_history_with_it()
    {
        var source = AddAccount("Kitchen");
        AddWatchedProduct(source, "111");
        var bundle = await ExportEndpoints.BuildExportAsync(_db, source, default);

        // A clean database: nothing to reuse, so the list, the product and its
        // history all have to be recreated.
        _db.Users.RemoveRange(_db.Users);
        _db.Products.RemoveRange(_db.Products);
        _db.SaveChanges();

        var target = AddAccount("Untouched");
        var result = await ExportEndpoints.ImportAsync(_db, target, bundle, default);

        Assert.Equal(1, result.ItemsImported);
        Assert.Equal(1, result.SnapshotsImported);
        Assert.Contains(_db.WatchLists, l => l.UserId == target && l.Name == "Kitchen");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
