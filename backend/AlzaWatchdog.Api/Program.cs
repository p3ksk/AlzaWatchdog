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
        o.UseMySQL(connectionString
                   ?? throw new InvalidOperationException(
                       "Database:Provider is MySql but ConnectionStrings:Default is not set.")));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(o =>
        o.UseSqlite(connectionString ?? "Data Source=alzawatchdog.db"));
}

builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.Configure<WatchdogOptions>(
    builder.Configuration.GetSection(WatchdogOptions.SectionName));

builder.Services.Configure<AdminOptions>(
    builder.Configuration.GetSection(AdminOptions.SectionName));

builder.Services.AddScoped<UserTokenFilter>();
builder.Services.AddScoped<AdminFilter>();
builder.Services.AddScoped<PriceUpdateService>();
builder.Services.AddSingleton<ProductImageCache>();

// ---------------------------------------------------------------------------
// alza.sk sits behind Cloudflare bot management, which fingerprints the TLS
// ClientHello. Two things about this client are load-bearing — both verified
// against the live site — and changing either turns every scrape into a 403:
//
//   1. A single pinned TLS version. A client that offers the usual wide range
//      of versions produces a ClientHello that Cloudflare recognises as a bot
//      and rejects, reproducibly and regardless of headers. Advertising only
//      TLS 1.3 is accepted just as reproducibly. The HTTP version is NOT the
//      factor here — HTTP/1.1 and HTTP/2 both work once TLS is pinned.
//   2. A complete, realistic browser header set. A bare "Mozilla/5.0" gets
//      blocked; the full Chrome User-Agent plus the Accept/Sec-Fetch headers a
//      real navigation sends does not.
//
// The cookie container matters too: Cloudflare hands back a __cf_bm cookie, and
// replaying it across polls keeps us from being re-challenged every time.
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient<IAlzaScraper, AlzaScraper>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);

    var headers = client.DefaultRequestHeaders;
    headers.Add("User-Agent",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    headers.Add("Accept",
        "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
    headers.Add("Accept-Language", "sk-SK,sk;q=0.9,en;q=0.8");
    headers.Add("sec-ch-ua", "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
    headers.Add("sec-ch-ua-mobile", "?0");
    headers.Add("sec-ch-ua-platform", "\"Linux\"");
    headers.Add("Sec-Fetch-Dest", "document");
    headers.Add("Sec-Fetch-Mode", "navigate");
    headers.Add("Sec-Fetch-Site", "none");
    headers.Add("Sec-Fetch-User", "?1");
    headers.Add("Upgrade-Insecure-Requests", "1");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    CookieContainer = new CookieContainer(),
    UseCookies = true,
    SslOptions = new SslClientAuthenticationOptions
    {
        // See the note above: this single line is the difference between every
        // scrape succeeding and every scrape returning 403.
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

// Product images live on a separate CDN and are only fetched when a list is
// rendered. A short timeout keeps a slow image from stalling a page of cards.
builder.Services.AddHttpClient(ProductImageCache.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHostedService<PriceCheckWorker>();

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
