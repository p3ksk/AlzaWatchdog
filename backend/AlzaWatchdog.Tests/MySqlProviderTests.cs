using AlzaWatchdog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Tests;

/// <summary>
/// Guards the MySQL provider without needing a MySQL server.
///
/// The model is shared but the migrations are not, so the way this breaks is
/// quietly: someone changes an entity, regenerates only the SQLite migration, and
/// nobody notices until a MySQL deployment starts. Building the model through the
/// MySQL provider catches configuration that SQLite tolerates and MySQL does not.
/// </summary>
public class MySqlProviderTests
{
    private static MySqlAppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MySqlAppDbContext>()
            .UseMySQL("server=localhost;database=none;user=none;password=none")
            .Options);

    [Fact]
    public void Model_builds_for_mysql()
    {
        using var db = CreateContext();

        var model = db.Model;

        Assert.NotNull(model.FindEntityType(typeof(Api.Domain.User)));
        Assert.NotNull(model.FindEntityType(typeof(Api.Domain.WatchList)));
        Assert.NotNull(model.FindEntityType(typeof(Api.Domain.Product)));
        Assert.NotNull(model.FindEntityType(typeof(Api.Domain.TrackedItem)));
        Assert.NotNull(model.FindEntityType(typeof(Api.Domain.PriceSnapshot)));
    }

    [Fact]
    public void Mysql_has_its_own_migration_set()
    {
        using var db = CreateContext();

        // Migrations are matched to a context by type; an empty set here would mean
        // a MySQL deployment silently starts against an empty database.
        var migrations = db.Database.GetMigrations().ToList();

        Assert.NotEmpty(migrations);
    }

    [Fact]
    public void Prices_and_timestamps_use_the_same_storage_shape_as_sqlite()
    {
        using var db = CreateContext();
        var item = db.Model.FindEntityType(typeof(Api.Domain.Product))!;

        // Kept deliberately uniform across providers: prices as text and timestamps
        // as integers. Letting MySQL use its native DECIMAL and DATETIME would make
        // ordering and aggregation work there but not on SQLite, so the same query
        // would behave differently depending on deployment.
        Assert.Equal(typeof(string), ProviderTypeOf(item, "LastPrice"));
        Assert.Equal(typeof(string), ProviderTypeOf(item, "LastPlusPrice"));
        Assert.Equal(typeof(string), ProviderTypeOf(item, "LastCouponPrice"));
        Assert.Equal(typeof(long), ProviderTypeOf(item, "CreatedAt"));
    }

    /// <summary>
    /// The stored type comes from the value converter; GetProviderClrType only
    /// reports a type that was pinned explicitly, which these properties do not do.
    /// </summary>
    private static Type? ProviderTypeOf(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity, string property)
    {
        return entity.FindProperty(property)!.GetValueConverter()?.ProviderClrType;
    }
}
