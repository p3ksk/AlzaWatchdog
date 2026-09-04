using AlzaWatchdog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Api.Data;

/// <summary>
/// The model, shared by every provider. Abstract on purpose: each provider gets
/// its own concrete context so it can own its own migrations.
///
/// The concrete types must be siblings rather than one deriving from the other.
/// EF resolves a context's model snapshot by assignability, so a subclass's
/// snapshot gets picked up when diffing the base — which silently produces empty
/// migrations for the base context, with no error to explain why.
/// </summary>
public abstract class AppDbContext : DbContext
{
    protected AppDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<WatchList> WatchLists => Set<WatchList>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<TrackedItem> TrackedItems => Set<TrackedItem>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Applied model-wide so no future timestamp column can reintroduce the
        // untranslatable-ORDER BY problem described on the converter.
        b.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetAsUnixMillisConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        b.Entity<WatchList>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(80);

            e.HasOne(x => x.User)
                .WithMany(u => u.Lists)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.UserId);
        });

        b.Entity<Product>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProductCode).HasMaxLength(32);
            e.Property(x => x.CanonicalUrl).HasMaxLength(1024);
            e.Property(x => x.Name).HasMaxLength(512);
            e.Property(x => x.ImageUrl).HasMaxLength(1024);
            e.Property(x => x.Currency).HasMaxLength(8);
            e.Property(x => x.LastAvailability).HasMaxLength(64);
            e.Property(x => x.LastError).HasMaxLength(512);

            // SQLite has no decimal type; EF's default maps it to REAL (a double),
            // which cannot represent money exactly. Store the invariant text instead.
            e.Property(x => x.LastPrice).HasConversion<DecimalAsTextConverter>();
            e.Property(x => x.LastPlusPrice).HasConversion<DecimalAsTextConverter>();
            e.Property(x => x.LastCouponPrice).HasConversion<DecimalAsTextConverter>();

            // The product code is the product's identity, so it can only appear once.
            e.HasIndex(x => x.ProductCode).IsUnique();

            // The sweep asks for active products that are due.
            e.HasIndex(x => new { x.IsActive, x.LastCheckedAt });
        });

        b.Entity<TrackedItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();

            e.HasOne(x => x.WatchList)
                .WithMany(l => l.Items)
                .HasForeignKey(x => x.WatchListId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Product)
                .WithMany(p => p.TrackedBy)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // The same product cannot appear twice on one list, though it may
            // appear on any number of different lists.
            e.HasIndex(x => new { x.WatchListId, x.ProductId }).IsUnique();
        });

        b.Entity<PriceSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Availability).HasMaxLength(64);
            e.Property(x => x.Price).HasConversion<DecimalAsTextConverter>();
            e.Property(x => x.PlusPrice).HasConversion<DecimalAsTextConverter>();
            e.Property(x => x.CouponPrice).HasConversion<DecimalAsTextConverter>();

            e.HasOne(x => x.Product)
                .WithMany(p => p.Snapshots)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.ProductId, x.CapturedAt });
        });
    }
}
