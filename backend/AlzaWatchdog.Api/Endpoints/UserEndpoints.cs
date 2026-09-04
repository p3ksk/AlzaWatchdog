using AlzaWatchdog.Api.Admin;
using AlzaWatchdog.Api.Scraping;
using AlzaWatchdog.Api.Auth;
using AlzaWatchdog.Api.Contracts;
using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Api.Endpoints;

public static class UserEndpoints
{
    public const string DefaultListName = "My watchlist";

    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Account");

        group.MapPost("/", async (AppDbContext db, IOptions<AdminOptions> admin, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var user = new User { Id = Guid.NewGuid(), CreatedAt = now, LastSeenAt = now };

            // Every account starts with one list, so the app always has somewhere
            // to send the browser rather than an empty "pick a list" state.
            var list = new WatchList
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Name = DefaultListName,
                CreatedAt = now,
            };

            db.Users.Add(user);
            db.WatchLists.Add(list);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new AccountDto(
                user.Id,
                user.HasAlzaPlus,
                admin.Value.IsAdmin(user.Id),
                [new WatchListDto(list.Id, list.Name, list.CreatedAt, 0)]));
        })
        .WithName("CreateAccount")
        .WithSummary("Issues a new account key and its first list.");

        group.MapPost("/start", async (
            AddItemRequest request,
            AppDbContext db,
            IAlzaScraper scraper,
            PriceUpdateService updater,
            IOptions<AdminOptions> admin,
            CancellationToken ct) =>
        {
            if (!AlzaUrl.TryParse(request.Url, out var parsed))
            {
                return Results.Problem(
                    title: "Not a valid alza.sk product URL",
                    detail: "Expected something like https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var now = DateTimeOffset.UtcNow;

            var product = await db.Products
                .FirstOrDefaultAsync(p => p.ProductCode == parsed.ProductCode, ct);

            if (product is null)
            {
                var result = await scraper.FetchAsync(parsed.CanonicalUrl, ct);

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

                product = new Product
                {
                    Id = Guid.NewGuid(),
                    ProductCode = parsed.ProductCode,
                    CanonicalUrl = parsed.CanonicalUrl,
                    CreatedAt = now,
                };

                db.Products.Add(product);
                updater.Apply(db, product, result, now);
            }

            // The account is only built once the product is known to be real.
            // Everything here lands in one SaveChanges, so a URL that turns out to
            // be a 404 — or a block — leaves no half-made account behind. That is
            // the whole point of not creating one when the page is merely opened.
            var user = new User { Id = Guid.NewGuid(), CreatedAt = now, LastSeenAt = now };
            var list = new WatchList
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Name = DefaultListName,
                CreatedAt = now,
            };

            db.Users.Add(user);
            db.WatchLists.Add(list);
            db.TrackedItems.Add(new TrackedItem
            {
                Id = Guid.NewGuid(),
                WatchListId = list.Id,
                ProductId = product.Id,
                SortOrder = 0,
                CreatedAt = now,
            });

            await db.SaveChangesAsync(ct);

            return Results.Ok(new AccountDto(
                user.Id,
                user.HasAlzaPlus,
                admin.Value.IsAdmin(user.Id),
                [new WatchListDto(list.Id, list.Name, list.CreatedAt, 1)]));
        })
        .WithName("StartWithFirstProduct")
        .WithSummary("Creates an account, its first list and its first product together.");

        // Lets someone re-enter a key from another device and get their lists back.
        group.MapGet("/{userId:guid}", async (
            Guid userId, AppDbContext db, IOptions<AdminOptions> admin, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                return Results.NotFound();

            // Presenting the key is access, and the cleanup worker deletes accounts
            // by how long they have gone untouched. Without this, simply opening a
            // bookmark would not count as using the account.
            user.LastSeenAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var lists = await ListEndpoints.LoadListsAsync(db, userId, ct);
            return Results.Ok(new AccountDto(userId, user.HasAlzaPlus, admin.Value.IsAdmin(userId), lists));
        })
        .WithName("GetAccount")
        .WithSummary("Validates an account key and returns its lists.");

        group.MapPatch("/", async (
            UpdateAccountRequest request,
            HttpContext http,
            AppDbContext db,
            IOptions<AdminOptions> admin,
            CancellationToken ct) =>
        {
            var userId = UserTokenFilter.GetUserId(http);

            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            user.HasAlzaPlus = request.HasAlzaPlus;
            await db.SaveChangesAsync(ct);

            var lists = await ListEndpoints.LoadListsAsync(db, userId, ct);
            return Results.Ok(new AccountDto(userId, user.HasAlzaPlus, admin.Value.IsAdmin(userId), lists));
        })
        .AddEndpointFilter<UserTokenFilter>()
        .WithName("UpdateAccount")
        .WithSummary("Records whether this account holds an AlzaPlus+ membership.");
    }
}
