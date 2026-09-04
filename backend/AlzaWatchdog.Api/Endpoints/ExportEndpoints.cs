using System.Text.Json;
using AlzaWatchdog.Api.Auth;
using AlzaWatchdog.Api.Contracts;
using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Api.Endpoints;

public static class ExportEndpoints
{
    /// <summary>
    /// Matches how ASP.NET serialises every other response. Serialising the bundle
    /// by hand with default options would emit PascalCase, and the import endpoint
    /// — which binds through the framework — would not recognise its own export.
    /// </summary>
    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static void MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .WithTags("Backup")
            .AddEndpointFilter<UserTokenFilter>();

        group.MapGet("/export", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var userId = UserTokenFilter.GetUserId(http);
            var bundle = await BuildExportAsync(db, userId, ct);

            return Results.File(
                JsonSerializer.SerializeToUtf8Bytes(bundle, ExportJson),
                "application/json",
                $"alza-watchdog-user-{userId:N}.json");
        })
        .WithName("ExportProducts")
        .WithSummary("Downloads this account's watched products as a JSON bundle.");

        group.MapPost("/import", async (
            ProductExportBundle bundle, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            if (bundle.Format != ProductExportBundle.CurrentFormat)
            {
                return Results.Problem(
                    title: "Unrecognised bundle",
                    detail: $"Expected format \"{ProductExportBundle.CurrentFormat}\".",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var userId = UserTokenFilter.GetUserId(http);
            if (bundle.UserId != userId)
            {
                return Results.Problem(
                    title: "Not your bundle",
                    detail: "This backup belongs to a different account.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(await ImportAsync(db, bundle, ct));
        })
        .WithName("ImportProducts")
        .WithSummary("Restores this account's products from a backup bundle.");
    }

    private static async Task<Dictionary<Guid, List<PriceSnapshotDto>>> LoadSnapshotsAsync(
        AppDbContext db, List<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0)
            return [];

        var rows = await db.PriceSnapshots
            .AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId))
            .OrderBy(s => s.CapturedAt)
            .ToListAsync(ct);

        return rows
            .GroupBy(s => s.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => new PriceSnapshotDto(
                    s.Price, s.PlusPrice, s.CouponPrice, s.Availability, s.CapturedAt)).ToList());
    }

    private static async Task<ProductExportBundle> BuildExportAsync(
        AppDbContext db, Guid userId, CancellationToken ct)
    {
        var items = await db.TrackedItems
            .AsNoTracking()
            .Include(i => i.WatchList)
            .Include(i => i.Product)
            .Where(i => i.WatchList.UserId == userId)
            .OrderBy(i => i.WatchListId)
            .ThenBy(i => i.SortOrder)
            .ThenBy(i => i.CreatedAt)
            .ToListAsync(ct);

        var snapshots = await LoadSnapshotsAsync(db, items.Select(i => i.ProductId).Distinct().ToList(), ct);

        return new ProductExportBundle(
            ProductExportBundle.CurrentFormat,
            DateTimeOffset.UtcNow,
            userId,
            items.Select(i => new ExportItem(
                i.Id,
                i.WatchList.Name,
                i.Product.ProductCode,
                i.Product.CanonicalUrl,
                i.Product.Name,
                i.Product.ImageUrl,
                i.Product.Currency,
                i.Product.LastPrice,
                i.Product.LastPlusPrice,
                i.Product.LastCouponPrice,
                i.Product.LastAvailability,
                i.Product.LastCheckedAt,
                i.Product.LastError,
                i.Product.ConsecutiveFailures,
                i.Product.IsActive,
                i.SortOrder,
                i.CreatedAt,
                snapshots.GetValueOrDefault(i.ProductId) ?? [])).ToList());
    }

    private static async Task<ImportResultDto> ImportAsync(
        AppDbContext db, ProductExportBundle bundle, CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.Lists)
            .FirstAsync(u => u.Id == bundle.UserId, ct);

        var notes = new List<string>();
        var products = new Dictionary<string, Product>();
        int imported = 0, skipped = 0, snapshots = 0;

        foreach (var item in bundle.Items)
        {
            // Restore into the same account, reusing existing lists by name.
            var list = user.Lists.FirstOrDefault(l => l.Name == item.ListName)
                       ?? await db.WatchLists.FirstOrDefaultAsync(
                           l => l.UserId == user.Id && l.Name == item.ListName, ct);

            if (list is null)
            {
                list = new WatchList
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Name = item.ListName,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                user.Lists.Add(list);
                notes.Add($"Created list \"{item.ListName}\".");
            }

            // The same product cannot appear twice on one list, so re-importing
            // into a live account leaves its existing items untouched.
            var duplicate = await db.TrackedItems.AnyAsync(
                i => i.WatchListId == list.Id && i.Product.ProductCode == item.ProductCode, ct);
            if (duplicate)
            {
                skipped++;
                notes.Add($"{item.ProductCode} in \"{list.Name}\": already present, left untouched.");
                continue;
            }

            var (product, createdProduct) = await ProductRestore.EnsureAsync(db, products, new ProductFacts(
                item.ProductCode, item.CanonicalUrl, item.Name, item.ImageUrl, item.Currency,
                item.LastPrice, item.LastPlusPrice, item.LastCouponPrice, item.LastAvailability,
                item.LastCheckedAt, item.LastError, item.ConsecutiveFailures, item.IsActive,
                item.CreatedAt), ct);

            db.TrackedItems.Add(new TrackedItem
            {
                Id = item.Id,
                WatchListId = list.Id,
                ProductId = product.Id,
                SortOrder = item.SortOrder,
                CreatedAt = item.CreatedAt,
            });
            imported++;

            // History belongs to the product now. A bundle carries a copy per
            // tracked item, so only the first entry for a product contributes it —
            // importing the rest would recreate the duplication this removed.
            if (!createdProduct)
                continue;

            foreach (var snapshot in item.Snapshots)
            {
                // Snapshot ids are database-assigned and carry no meaning, so
                // they are regenerated rather than imported.
                db.PriceSnapshots.Add(new PriceSnapshot
                {
                    ProductId = product.Id,
                    Price = snapshot.Price,
                    PlusPrice = snapshot.PlusPrice,
                    CouponPrice = snapshot.CouponPrice,
                    Availability = snapshot.Availability,
                    CapturedAt = snapshot.CapturedAt,
                });
                snapshots++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped, snapshots, notes);
    }
}
