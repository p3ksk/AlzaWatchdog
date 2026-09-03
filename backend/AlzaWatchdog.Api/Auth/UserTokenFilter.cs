using AlzaWatchdog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Api.Auth;

/// <summary>
/// Resolves the account from the <c>X-User-Token</c> header. This guards only the
/// endpoints that manage the *set* of lists; reaching into a single list is
/// authorised by knowing that list's id, which is what the URL carries.
/// </summary>
public class UserTokenFilter(AppDbContext db) : IEndpointFilter
{
    public const string HeaderName = "X-User-Token";
    private const string ContextKey = "UserId";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!http.Request.Headers.TryGetValue(HeaderName, out var raw)
            || !Guid.TryParse(raw.ToString(), out var userId))
        {
            return Results.Problem(
                title: "Missing or malformed account key",
                detail: $"Send your account key in the {HeaderName} header.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, http.RequestAborted);
        if (user is null)
        {
            return Results.Problem(
                title: "Unknown account key",
                detail: "This key does not belong to any account.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        user.LastSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(http.RequestAborted);

        http.Items[ContextKey] = userId;
        return await next(context);
    }

    /// <summary>The caller's account id. Only valid on endpoints guarded by this filter.</summary>
    public static Guid GetUserId(HttpContext http) => (Guid)http.Items[ContextKey]!;
}
