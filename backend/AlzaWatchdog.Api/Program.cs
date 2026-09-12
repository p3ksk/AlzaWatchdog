using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using AlzaWatchdog.Api.Admin;
using AlzaWatchdog.Api.Auth;
using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Endpoints;
using AlzaWatchdog.Api.Images;
using AlzaWatchdog.Api.Scraping;
using AlzaWatchdog.Api.Workers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>() ?? new DatabaseOptions();

var connectionString = builder.Configuration.GetConnectionString("Default");

// Two providers, two migration sets. AppDbContext is the SQLite context so its
// already-applied migration history keeps matching; MySQL runs through a subclass
// that owns its own migrations. See MySqlAppDbContext for why they cannot share.
if (databaseOptions.Provider == DatabaseProvider.MySql)
{
    builder.Services.AddDbContext<AppDbContext, MySqlAppDbContext>(o =>
        o.UseMySql(connectionString
                   ?? throw new InvalidOperationException(
                       "Database:Provider is MySql but ConnectionStrings:Default is not set."),
            new MariaDbServerVersion(new Version(10, 11, 0))));
}
else
{
    builder.Services.AddDbContext<AppDbContext, SqliteAppDbContext>(o =>
        o.UseSqlite(connectionString ?? "Data Source=alzawatchdog.db"));
}

builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.Configure<WatchdogOptions>(
    builder.Configuration.GetSection(WatchdogOptions.SectionName));

builder.Services.Configure<CleanupOptions>(
    builder.Configuration.GetSection(CleanupOptions.SectionName));

builder.Services.Configure<AdminOptions>(
    builder.Configuration.GetSection(AdminOptions.SectionName));

builder.Services.AddScoped<UserTokenFilter>();
builder.Services.AddScoped<AdminFilter>();
builder.Services.AddScoped<PriceUpdateService>();
builder.Services.AddSingleton<ProductImageCache>();

// alza.sk is behind Cloudflare bot management. Four things here are load-bearing,
// all measured against the live site (see README and scripts/cf-probe*.sh):
//
//   1. TLS 1.3 pinned. A wider version range is refused whatever the headers say.
//      The HTTP version is not a factor.
//   2. The sec-ch-ua and Sec-Fetch-* headers. Without them: 403, challenge.
//   3. No Accept-Encoding at all. From a datacenter IP, 0 of 10 requests carrying
//      it got through — Chrome's own value included — against 5 of 5 without.
//   4. TryAddWithoutValidation, never Add: Add re-serialises parsed headers and
//      inserts spaces Chrome does not send.
//
// The cookie jar lives out here because HttpClientFactory rotates the handler
// every two minutes, and a jar created in the factory would be empty again long
// before the next sweep.
var alzaCookies = new CookieContainer();

// In a variable so the request log can report it rather than claim it.
var alzaDecompression = DecompressionMethods.None;

builder.Services.AddHttpClient<IAlzaScraper, AlzaScraper>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);

    // Cloudflare also advertises arch, bitness and model in Critical-CH; sending
    // those made no difference, so they are not sent.
    var headers = client.DefaultRequestHeaders;
    headers.TryAddWithoutValidation("User-Agent",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    headers.TryAddWithoutValidation("Accept",
        "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
    headers.TryAddWithoutValidation("Accept-Language", "sk-SK,sk;q=0.9,en;q=0.8");
    headers.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
    headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
    headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Linux\"");
    headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
    headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
    headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
    headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
    headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    // None, not All: All would add Accept-Encoding. See point 3 above.
    AutomaticDecompression = alzaDecompression,
    CookieContainer = alzaCookies,
    UseCookies = true,
    SslOptions = new SslClientAuthenticationOptions
    {
        // Point 1 above: this line alone decides 200 versus 403.
        EnabledSslProtocols = SslProtocols.Tls13,
    },
})
// Retries cover network blips and 5xx only. Both 403 and 429 mean Cloudflare
// turned us away, and retrying those just deepens the hole — the scraper reports
// them as Blocked and the worker backs off instead.
.AddStandardResilienceHandler(o =>
{
    o.Retry.MaxRetryAttempts = 2;
    o.Retry.UseJitter = true;
    o.Retry.ShouldHandle = args => ValueTask.FromResult(
        args.Outcome.Exception is HttpRequestException or TimeoutException
        || args.Outcome.Result is { StatusCode: >= HttpStatusCode.InternalServerError }
        || args.Outcome.Result is { StatusCode: HttpStatusCode.RequestTimeout });
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(70);
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(40);
});

// Placed after the resilience handler so it logs each attempt separately rather
// than only the one that finally came back.
if (builder.Configuration.GetValue($"{WatchdogOptions.SectionName}:LogRequests", false))
{
    builder.Services.AddHttpClient<IAlzaScraper, AlzaScraper>()
        .AddHttpMessageHandler(sp => new HttpTraceHandler(
            alzaCookies, alzaDecompression, sp.GetRequiredService<ILogger<HttpTraceHandler>>()));
}

// Product images live on a separate CDN and are only fetched when a list is
// rendered. A short timeout keeps a slow image from stalling a page of cards.
builder.Services.AddHttpClient(ProductImageCache.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);

    // image.alza.cz sits behind the same Cloudflare tenancy as the product pages,
    // so this client presents itself the same way. It is only fetching images and
    // is not blocked today, but a default .NET handshake is precisely the
    // fingerprint that gets refused, and there is no reason to look like a bot on
    // one Alza host while carefully not looking like one on another.
    var headers = client.DefaultRequestHeaders;
    headers.Add("User-Agent",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    headers.Add("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
    headers.Add("Accept-Language", "sk-SK,sk;q=0.9,en;q=0.8");
    headers.Add("Referer", "https://www.alza.sk/");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    SslOptions = new SslClientAuthenticationOptions
    {
        EnabledSslProtocols = SslProtocols.Tls13,
    },
});

builder.Services.AddSingleton<WorkerStatusRegistry>();
builder.Services.AddHostedService<PriceCheckWorker>();
builder.Services.AddHostedService<AccountCleanupWorker>();

const string CorsPolicy = "frontend";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                 ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    app.Logger.LogInformation(
        "Using {Provider} database ({Context}).",
        databaseOptions.Provider,
        db.GetType().Name);

    await MigrateWithRetryAsync(db, app.Logger);
}

/// <summary>
/// Applies migrations, waiting for the database to accept connections first.
///
/// A file-based SQLite database is always there, but a separate server is not:
/// it may still be starting, or restarting under us. Without this the API simply
/// crashes on boot and relies on the container runtime to keep restarting it,
/// which works but buries a normal startup race in a stack trace.
/// </summary>
static async Task MigrateWithRetryAsync(AppDbContext db, ILogger logger)
{
    var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
    var delay = TimeSpan.FromSeconds(1);

    while (true)
    {
        try
        {
            await db.Database.MigrateAsync();
            return;
        }
        catch (Exception ex) when (DateTimeOffset.UtcNow < deadline)
        {
            logger.LogWarning(
                "Database not ready ({Message}); retrying in {Delay}.", ex.Message, delay);

            await Task.Delay(delay);
            delay = delay < TimeSpan.FromSeconds(8) ? delay * 2 : delay;
        }
    }
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseExceptionHandler();
app.UseCors(CorsPolicy);

app.MapUserEndpoints();
app.MapListEndpoints();
app.MapExportEndpoints();
app.MapAdminEndpoints();
app.MapAdminBackupEndpoints();

app.Run();

/// <summary>Exposed so integration tests can spin the API up with WebApplicationFactory.</summary>
public partial class Program;
