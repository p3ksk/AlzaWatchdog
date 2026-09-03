using AlzaWatchdog.Api.Auth;
using AlzaWatchdog.Api.Contracts;
using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Images;
using AlzaWatchdog.Api.Scraping;
using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Api.Endpoints;

public static class ListEndpoints
{
    private const int MaxListsPerUser = 20;

    public static void MapListEndpoints(this IEndpointRouteBuilder app)
    {
        // Managing the *set* of lists needs the account key.
        var owned = app.MapGroup("/api/lists")
            .WithTags("Lists")
            .AddEndpointFilter<UserTokenFilter>();

        owned.MapGet("/", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await LoadListsAsync(db, UserTokenFilter.GetUserId(http), ct)))
        .WithName("GetLists");

        owned.MapPost("/", async (
            SaveListRequest request, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var userId = UserTokenFilter.GetUserId(http);

            if (!TryCleanName(request.Name, out var name, out var problem))
                return problem;

            if (await db.WatchLists.CountAsync(l => l.UserId == userId, ct) >= MaxListsPerUser)
            {
                return Results.Problem(
                    title: "Too many lists",
                    detail: $"An account can hold at most {MaxListsPerUser} lists.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var list = new WatchList
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.WatchLists.Add(list);
            await db.SaveChangesAsync(ct);

            return Results.Created(
                $"/api/lists/{list.Id}",
                new WatchListDto(list.Id, list.Name, list.CreatedAt, 0));
        })
        .WithName("CreateList");

        owned.MapPatch("/{listId:guid}", async (
            Guid listId, SaveListRequest request, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var userId = UserTokenFilter.GetUserId(http);

            if (!TryCleanName(request.Name, out var name, out var problem))
                return problem;

            var list = await db.WatchLists.FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId, ct);
            if (list is null)
                return Results.NotFound();

            list.Name = name;
            await db.SaveChangesAsync(ct);

            var count = await db.TrackedItems.CountAsync(i => i.WatchListId == listId, ct);
            return Results.Ok(new WatchListDto(list.Id, list.Name, list.CreatedAt, count));
        })
        .WithName("RenameList");

        owned.MapDelete("/{listId:guid}", async (
            Guid listId, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var userId = UserTokenFilter.GetUserId(http);

            // Refusing to delete the last list keeps the app out of a state with
            // nowhere to navigate to.
            if (await db.WatchLists.CountAsync(l => l.UserId == userId, ct) <= 1)
            {
                return Results.Problem(
                    title: "Cannot delete your only list",
                    detail: "Create another list first, or just remove the items from this one.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var deleted = await db.WatchLists
                .Where(l => l.Id == listId && l.UserId == userId)
                .ExecuteDeleteAsync(ct);

            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteList");

        // There is exactly one credential in this app — the account key — and every
        // route below needs it. A list id on its own grants nothing.
        var byId = app.MapGroup("/api/lists/{listId:guid}")
            .WithTags("List contents")
            .AddEndpointFilter<UserTokenFilter>();

        byId.MapGet("/", async (
            Guid listId, HttpContext http, AppDbContext db, ProductImageCache images, CancellationToken ct) =>
        {
            var userId = UserTokenFilter.GetUserId(http);

            var list = await db.WatchLists
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId, ct);

            if (list is null)
                return Results.NotFound();

            var items = await db.TrackedItems
                .AsNoTracking()
                .Where(i => i.WatchListId == listId)
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.CreatedAt)
                .ToListAsync(ct);

            return Results.Ok(new WatchListDetailDto(list.Id, list.Name, await BuildDtosAsync(db, images, items, ct)));
        })
        .WithName("GetList");

        byId.MapPost("/items", async (
            Guid listId,
            AddItemRequest request,
            HttpContext http,
            AppDbContext db,
            IAlzaScraper scraper,
            PriceUpdateService updater,
            ProductImageCache images,
            CancellationToken ct) =>
        {
            if (!await OwnsListAsync(db, http, listId, ct))
                return Results.NotFound();

            if (!AlzaUrl.TryParse(request.Url, out var product))
            {
                return Results.Problem(
                    title: "Not a valid alza.sk product URL",
                    detail: "Expected something like https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (await db.TrackedItems.AnyAsync(i => i.WatchListId == listId && i.ProductCode == product.ProductCode, ct))
            {
                return Results.Problem(
                    title: "Already on this list",
                    detail: "This product is already being watched here.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            // The only scrape triggered by a person: without it a new card would
            // sit blank until the next sweep, hours later.
            var result = await scraper.FetchAsync(product.CanonicalUrl, ct);

            if (result.Status == ScrapeStatus.ProductNotFound)
            {
                return Results.Problem(
                    title: "No such product",
                    detail: "alza.sk returned 404 for this URL.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (result.Status == ScrapeStatus.Blocked)
            {
                return Results.Problem(
                    title: "alza.sk is not answering right now",
                    detail: "The request was blocked. Try again in a few minutes.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var now = DateTimeOffset.UtcNow;
            var item = new TrackedItem
            {
                Id = Guid.NewGuid(),
                WatchListId = listId,
                ProductCode = product.ProductCode,
                CanonicalUrl = product.CanonicalUrl,
                SortOrder = await db.TrackedItems
                    .Where(i => i.WatchListId == listId)
                    .MaxAsync(i => (int?)i.SortOrder, ct) + 1 ?? 0,
                CreatedAt = now,
            };

            db.TrackedItems.Add(item);
            updater.Apply(db, item, result, now);
            await db.SaveChangesAsync(ct);

            var dto = (await BuildDtosAsync(db, images, [item], ct))[0];
            return Results.Created($"/api/lists/{listId}/items/{item.Id}", dto);
        })
        .WithName("AddItem");

        byId.MapPut("/items/order", async (
            Guid listId,
            ReorderItemsRequest request,
            HttpContext http,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (!await OwnsListAsync(db, http, listId, ct))
                return Results.NotFound();

            if (request.OrderedItemIds is null || request.OrderedItemIds.Count == 0)
            {
                return Results.Problem(
                    title: "Empty order",
                    detail: "Provide every item in the list, oldest position first.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var items = await db.TrackedItems
                .Where(i => i.WatchListId == listId)
                .ToListAsync(ct);

            // The payload must contain exactly this list's items, no more and no fewer,
            // so a stale or foreign id cannot silently write a partial order.
            var byId = items.ToDictionary(i => i.Id);
            if (items.Count != request.OrderedItemIds.Count
                || request.OrderedItemIds.Any(id => !byId.ContainsKey(id)))
            {
                return Results.Problem(
                    title: "Order does not match the list",
                    detail: "The reorder payload must contain every item on this list and nothing else.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            for (var i = 0; i < request.OrderedItemIds.Count; i++)
                byId[request.OrderedItemIds[i]].SortOrder = i;

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("ReorderItems");

        byId.MapPost("/items/{itemId:guid}/resume", async (
            Guid listId, Guid itemId, HttpContext http, AppDbContext db, ProductImageCache images, CancellationToken ct) =>
        {
            if (!await OwnsListAsync(db, http, listId, ct))
                return Results.NotFound();

            var item = await db.TrackedItems
                .FirstOrDefaultAsync(i => i.Id == itemId && i.WatchListId == listId, ct);

            if (item is null)
                return Results.NotFound();

            // Deliberately does not scrape. Resuming only puts the item back into
            // the sweep, which will reach it in its own time — nothing a person
            // clicks should be able to generate traffic to alza.sk on demand.
            item.IsActive = true;
            item.ConsecutiveFailures = 0;
            item.LastError = null;
            await db.SaveChangesAsync(ct);

            var dto = (await BuildDtosAsync(db, images, [item], ct))[0];
            return Results.Ok(dto);
        })
        .WithName("ResumeItem");

        byId.MapDelete("/items/{itemId:guid}", async (
            Guid listId, Guid itemId, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            if (!await OwnsListAsync(db, http, listId, ct))
                return Results.NotFound();

            var deleted = await db.TrackedItems
                .Where(i => i.Id == itemId && i.WatchListId == listId)
                .ExecuteDeleteAsync(ct);

            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteItem");
    }

    /// <summary>
    /// Answers "is this list on the caller's account?". A list the caller does not
    /// own is reported as missing rather than forbidden, so the API never confirms
    /// that some other account's list id exists.
    /// </summary>
    private static Task<bool> OwnsListAsync(
        AppDbContext db, HttpContext http, Guid listId, CancellationToken ct)
    {
        var userId = UserTokenFilter.GetUserId(http);
        return db.WatchLists.AnyAsync(l => l.Id == listId && l.UserId == userId, ct);
    }

    internal static async Task<List<WatchListDto>> LoadListsAsync(
        AppDbContext db, Guid userId, CancellationToken ct) =>
        await db.WatchLists
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .OrderBy(l => l.CreatedAt)
            .Select(l => new WatchListDto(
                l.Id, l.Name, l.CreatedAt, l.Items.Count))
            .ToListAsync(ct);

    private static bool TryCleanName(string? raw, out string name, out IResult problem)
    {
        name = (raw ?? string.Empty).Trim();
        problem = Results.Empty;

        if (name.Length is 0 or > 80)
        {
            problem = Results.Problem(
                title: "Invalid list name",
                detail: "A list name must be between 1 and 80 characters.",
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Maps items to DTOs using two queries regardless of how many items there are.
    /// The history each card needs to draw its chart comes back with the list, so
    /// rendering the page costs one request rather than one per item.
    /// </summary>
    private static async Task<List<TrackedItemDto>> BuildDtosAsync(
        AppDbContext db, ProductImageCache images, IReadOnlyList<TrackedItem> items, CancellationToken ct)
    {
        if (items.Count == 0)
            return [];

        var ids = items.Select(i => i.Id).ToList();

        var snapshots = await db.PriceSnapshots
            .AsNoTracking()
            .Where(s => ids.Contains(s.TrackedItemId))
            .OrderBy(s => s.CapturedAt)
            .ToListAsync(ct);

        var byItem = snapshots
            .GroupBy(s => s.TrackedItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Resolve all images concurrently. The cache dedupes repeated URLs and
        // null results, so a page full of the same product costs one download.
        var imageTasks = items
            .Select(item => item.ImageUrl)
            .Distinct()
            .ToDictionary(url => url!, url => images.GetDataUriAsync(url));

        var itemsWithImages = new List<(TrackedItem Item, string? ImageDataUri)>(items.Count);
        foreach (var item in items)
        {
            var imageDataUri = item.ImageUrl is null
                ? null
                : await imageTasks[item.ImageUrl].ConfigureAwait(false);
            itemsWithImages.Add((item, imageDataUri));
        }

        return itemsWithImages.Select(entry =>
        {
            var item = entry.Item;
            var imageDataUri = entry.ImageDataUri;
            var history = byItem.GetValueOrDefault(item.Id) ?? [];
            var prices = history.Where(s => s.Price is not null).Select(s => s.Price!.Value).ToList();

            // Min/max are computed here rather than in SQL on purpose: prices are
            // stored as text, so a SQL MIN would compare them lexicographically and
            // decide that "9.90" is dearer than "18.90". See DecimalAsTextConverter.
            decimal? lowest = prices.Count > 0 ? prices.Min() : null;
            decimal? highest = prices.Count > 0 ? prices.Max() : null;

            // Snapshots are only written when something changed, so the row before
            // the newest one holds the price this item moved away from.
            var previous = history.Count > 1 ? history[^2].Price : null;

            return new TrackedItemDto(
                item.Id,
                item.ProductCode,
                item.CanonicalUrl,
                item.Name,
                imageDataUri,
                item.LastPrice,
                previous,
                item.LastPlusPrice,
                item.LastCouponPrice,
                lowest,
                highest,
                item.Currency,
                item.LastAvailability,
                item.LastCheckedAt,
                item.LastError,
                item.IsActive,
                item.SortOrder,
                item.CreatedAt,
                history
                    .Select(s => new PriceSnapshotDto(
                        s.Price, s.PlusPrice, s.CouponPrice, s.Availability, s.CapturedAt))
                    .ToList());
        }).ToList();
    }
}
