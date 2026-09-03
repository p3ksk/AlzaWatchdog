using AlzaWatchdog.Api.Admin;
using AlzaWatchdog.Api.Auth;
using AlzaWatchdog.Api.Contracts;
using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // AdminFilter alone: admin rights come from configuration, so these routes
        // keep working when the caller has no account row — the case that matters
        // when restoring into an empty database.
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .AddEndpointFilter<AdminFilter>();

        group.MapGet("/stats", async (AppDbContext db, CancellationToken ct) =>
        {
            var items = await db.TrackedItems.AsNoTracking()
                .Select(i => new { i.ProductCode, i.IsActive })
                .ToListAsync(ct);

            return Results.Ok(new AdminStatsDto(
                Users: await db.Users.CountAsync(ct),
                Lists: await db.WatchLists.CountAsync(ct),
                Items: items.Count,
                DistinctProducts: items.Select(i => i.ProductCode).Distinct().Count(),
                Snapshots: await db.PriceSnapshots.CountAsync(ct),
                InactiveItems: items.Count(i => !i.IsActive)));
        })
        .WithName("AdminStats");

        group.MapGet("/users", async (
            AppDbContext db, IOptions<AdminOptions> options, CancellationToken ct) =>
        {
            var users = await db.Users
                .AsNoTracking()
                .OrderByDescending(u => u.LastSeenAt)
                .Select(u => new
                {
                    u.Id,
                    u.HasAlzaPlus,
                    u.CreatedAt,
                    u.LastSeenAt,
                    ListCount = u.Lists.Count,
                    ItemCount = u.Lists.Sum(l => l.Items.Count),
                    SnapshotCount = u.Lists.Sum(l => l.Items.Sum(i => i.Snapshots.Count)),
                })
                .ToListAsync(ct);

            return Results.Ok(users.Select(u => new AdminUserDto(
                u.Id, u.HasAlzaPlus, options.Value.IsAdmin(u.Id),
                u.ListCount, u.ItemCount, u.SnapshotCount, u.CreatedAt, u.LastSeenAt)));
        })
        .WithName("AdminUsers");

        group.MapGet("/items", async (
            AppDbContext db, IOptions<WatchdogOptions> watchdog, CancellationToken ct) =>
        {
            var items = await db.TrackedItems
                .AsNoTracking()
                .Include(i => i.WatchList)
                .OrderBy(i => i.ProductCode)
                .ToListAsync(ct);

            var snapshots = await LoadSnapshotsAsync(db, items.Select(i => i.Id).ToList(), ct);
            var interval = watchdog.Value.CheckInterval;

            return Results.Ok(items.Select(i => new AdminItemDto(
                i.Id,
                i.WatchList.UserId,
                i.WatchListId,
                i.WatchList.Name,
                i.ProductCode,
                i.CanonicalUrl,
                i.Name,
                i.Currency,
                i.LastPrice,
                i.LastPlusPrice,
                i.LastCouponPrice,
                i.LastAvailability,
                i.LastCheckedAt,
                i.IsActive && i.LastCheckedAt is { } last ? last + interval : null,
                i.LastError,
                i.ConsecutiveFailures,
                i.IsActive,
                i.SortOrder,
                i.CreatedAt,
                snapshots.GetValueOrDefault(i.Id) ?? [])));
        })
        .WithName("AdminItems");
    }

    private static async Task<Dictionary<Guid, List<PriceSnapshotDto>>> LoadSnapshotsAsync(
        AppDbContext db, List<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0)
            return [];

        var rows = await db.PriceSnapshots
            .AsNoTracking()
            .Where(s => itemIds.Contains(s.TrackedItemId))
            .OrderBy(s => s.CapturedAt)
            .ToListAsync(ct);

        return rows
            .GroupBy(s => s.TrackedItemId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => new PriceSnapshotDto(
                    s.Price, s.PlusPrice, s.CouponPrice, s.Availability, s.CapturedAt)).ToList());
    }
}
