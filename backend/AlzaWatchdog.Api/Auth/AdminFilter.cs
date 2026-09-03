using AlzaWatchdog.Api.Admin;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Api.Auth;

/// <summary>
/// Allows through only the account keys listed under <c>Admin:Keys</c>.
///
/// Deliberately self-contained: it reads the header itself rather than sitting
/// behind <see cref="UserTokenFilter"/>, because admin rights are granted by
/// configuration, not by a database row. Requiring the row would lock an
/// administrator out of an empty database — which is precisely the situation the
/// import endpoint exists to fix.
/// </summary>
public class AdminFilter(IOptions<AdminOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!http.Request.Headers.TryGetValue(UserTokenFilter.HeaderName, out var raw)
            || !Guid.TryParse(raw.ToString(), out var userId))
        {
            return Results.Problem(
                title: "Missing or malformed account key",
                detail: $"Send your account key in the {UserTokenFilter.HeaderName} header.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!options.Value.IsAdmin(userId))
        {
            return Results.Problem(
                title: "Not an administrator",
                detail: "This account is not listed in Admin:Keys.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
