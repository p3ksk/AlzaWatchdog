using AlzaWatchdog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// For provider-specific subclasses, which are handed their own
    /// DbContextOptions&lt;TSubclass&gt; by the DI container.
    /// </summary>
    protected AppDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<WatchList> WatchLists => Set<WatchList>();
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

        b.Entity<TrackedItem>(e =>
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

            // Manual drag-and-drop ordering is persisted on the item itself.
            e.Property(x => x.SortOrder);

            // SQLite has no decimal type; EF's default maps it to REAL (a double),
            // which cannot represent money exactly. Store the invariant text instead.
            e.Property(x => x.LastPrice).HasConversion<DecimalAsTextConverter>();
            e.Property(x => x.LastPlusPrice).HasConversion<DecimalAsTextConverter>();
            e.Property(x => x.LastCouponPrice).HasConversion<DecimalAsTextConverter>();

            e.HasOne(x => x.WatchList)
                .WithMany(l => l.Items)
                .HasForeignKey(x => x.WatchListId)
                .OnDelete(DeleteBehavior.Cascade);

            // The same product cannot appear twice on one list, though it may
            // appear on several lists.
            e.HasIndex(x => new { x.WatchListId, x.ProductCode }).IsUnique();

            // The sweep queries active items grouped by product code.
            e.HasIndex(x => new { x.IsActive, x.ProductCode });
        });

        b.Entity<PriceSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Availability).HasMaxLength(64);
            e.Property(x => x.Price).HasConversion<DecimalAsTextConverter>();
            e.Property(x => x.PlusPrice).HasConversion<DecimalAsTextConverter>();
            e.Property(x => x.CouponPrice).HasConversion<DecimalAsTextConverter>();

            e.HasOne(x => x.TrackedItem)
                .WithMany(i => i.Snapshots)
                .HasForeignKey(x => x.TrackedItemId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.TrackedItemId, x.CapturedAt });
        });
    }
}
