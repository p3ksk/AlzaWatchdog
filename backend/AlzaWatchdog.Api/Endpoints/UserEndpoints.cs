using AlzaWatchdog.Api.Admin;
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

        // Lets someone re-enter a key from another device and get their lists back.
        group.MapGet("/{userId:guid}", async (
            Guid userId, AppDbContext db, IOptions<AdminOptions> admin, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                return Results.NotFound();

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
