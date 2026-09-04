using System.Text.Json;
using AlzaWatchdog.Api.Auth;
using AlzaWatchdog.Api.Contracts;
using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Api.Endpoints;

/// <summary>
/// Whole-account backup for administrators, kept in its own file and its own
/// bundle format so it stays clear of the per-account product backup in
/// <see cref="ExportEndpoints"/>. The two answer different questions: "give me
/// my products" versus "give me every account on this server".
/// </summary>
public static class AdminBackupEndpoints
{
    /// <summary>
    /// Matches how ASP.NET serialises every other response. Serialising by hand
    /// with default options would emit PascalCase, and the import endpoint — which
    /// binds through the framework — would not recognise its own export.
    /// </summary>
    private static readonly JsonSerializerOptions BackupJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static void MapAdminBackupEndpoints(this IEndpointRouteBuilder app)
    {
        // AdminFilter alone, with no UserTokenFilter: admin rights come from
        // configuration, so these keep working when the caller has no account row —
        // which is exactly the case when restoring into an empty database.
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin backup")
            .AddEndpointFilter<AdminFilter>();

        group.MapGet("/backup", async (Guid? userId, AppDbContext db, CancellationToken ct) =>
        {
            var bundle = await BuildAsync(db, userId, ct);

            var name = userId is null
                ? $"alza-watchdog-accounts-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"
                : $"alza-watchdog-account-{userId:N}.json";

            return Results.File(JsonSerializer.SerializeToUtf8Bytes(bundle, BackupJson), "application/json", name);
        })
        .WithName("AdminBackupExport")
        .WithSummary("Dumps every account, or one with ?userId=, as a JSON bundle.");

        group.MapPost("/backup", async (
            AccountBackupBundle bundle, AppDbContext db, CancellationToken ct) =>
        {
            if (bundle.Format != AccountBackupBundle.CurrentFormat)
            {
                return Results.Problem(
                    title: "Unrecognised bundle",
                    detail: $"Expected format \"{AccountBackupBundle.CurrentFormat}\".",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(await RestoreAsync(db, bundle, ct));
        })
        .WithName("AdminBackupImport")
        .WithSummary("Restores accounts from a bundle, keeping their original ids.");
    }

    private static async Task<AccountBackupBundle> BuildAsync(
        AppDbContext db, Guid? userId, CancellationToken ct)
    {
        var users = await db.Users
            .AsNoTracking()
            .Where(u => userId == null || u.Id == userId)
            .Include(u => u.Lists)
            .ThenInclude(l => l.Items)
            .ThenInclude(i => i.Product)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(ct);

        var itemIds = users.SelectMany(u => u.Lists).SelectMany(l => l.Items).Select(i => i.ProductId).Distinct().ToList();

        var snapshots = itemIds.Count == 0
            ? []
            : (await db.PriceSnapshots
                .AsNoTracking()
                .Where(s => itemIds.Contains(s.ProductId))
                .OrderBy(s => s.CapturedAt)
                .ToListAsync(ct))
              .GroupBy(s => s.ProductId)
              .ToDictionary(
                  g => g.Key,
                  g => (IReadOnlyList<PriceSnapshotDto>)g.Select(s => new PriceSnapshotDto(
                      s.Price, s.PlusPrice, s.CouponPrice, s.Availability, s.CapturedAt)).ToList());

        return new AccountBackupBundle(
            AccountBackupBundle.CurrentFormat,
            DateTimeOffset.UtcNow,
            users.Select(u => new BackupAccount(
                u.Id,
                u.HasAlzaPlus,
                u.CreatedAt,
                u.LastSeenAt,
                u.Lists.OrderBy(l => l.CreatedAt).Select(l => new BackupList(
                    l.Id,
                    l.Name,
                    l.CreatedAt,
                    l.Items.OrderBy(i => i.CreatedAt).Select(i => new BackupItem(
                        i.Id,
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
                        i.CreatedAt,
                        snapshots.GetValueOrDefault(i.ProductId) ?? [])).ToList())).ToList())).ToList());
    }

    private static async Task<AccountImportResultDto> RestoreAsync(
        AppDbContext db, AccountBackupBundle bundle, CancellationToken ct)
    {
        var existing = await db.Users.Select(u => u.Id).ToHashSetAsync(ct);
        var notes = new List<string>();
        var products = new Dictionary<string, Product>();
        int accounts = 0, skipped = 0, lists = 0, items = 0, snapshots = 0;

        foreach (var account in bundle.Accounts)
        {
            // Ids are preserved so an exported bookmark still works after a restore.
            // That makes overwriting a live account the only real hazard, so an id
            // already present is skipped rather than merged or clobbered.
            if (!existing.Add(account.Id))
            {
                skipped++;
                notes.Add($"{account.Id:N}: already exists, left untouched.");
                continue;
            }

            db.Users.Add(new User
            {
                Id = account.Id,
                HasAlzaPlus = account.HasAlzaPlus,
                CreatedAt = account.CreatedAt,
                LastSeenAt = account.LastSeenAt,
            });
            accounts++;

            foreach (var list in account.Lists)
            {
                db.WatchLists.Add(new WatchList
                {
                    Id = list.Id,
                    UserId = account.Id,
                    Name = list.Name,
                    CreatedAt = list.CreatedAt,
                });
                lists++;

                foreach (var item in list.Items)
                {
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
                        CreatedAt = item.CreatedAt,
                    });
                    items++;

                    // History belongs to the product. A bundle carries a copy per
                    // tracked item, so only the first entry for a product contributes
                    // it — importing the rest would rebuild the duplication.
                    if (!createdProduct)
                        continue;

                    foreach (var snapshot in item.Snapshots)
                    {
                        // Snapshot ids are database-assigned and carry no meaning,
                        // so they are regenerated rather than imported.
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
            }
        }

        await db.SaveChangesAsync(ct);
        return new AccountImportResultDto(accounts, skipped, lists, items, snapshots, notes);
    }
}
