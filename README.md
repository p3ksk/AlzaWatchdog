# Alza Watchdog

Watches the price of [alza.sk](https://www.alza.sk) products and remembers every
change, so you can tell a real discount from a fake one.

Paste a product link, put it on a list, and a background worker re-checks it on a
schedule. There are no passwords: your account lives at a URL, and bookmarking it
is how you come back.

- **Backend** — ASP.NET Core 10 minimal API, EF Core, SQLite or MySQL
- **Frontend** — Angular 21, standalone and zoneless, styled as an amber CRT terminal

## Running it

```bash
dotnet run --project backend/AlzaWatchdog.Api      # API on :5080
cd frontend/alza-watchdog-web && npm start         # UI on :4200, proxies /api
```

The database is created and migrated on startup. Or run the whole thing in
containers, served on <http://localhost:8081>:

```bash
podman-compose up -d --build                       # or: docker compose ...
```

## How it works

**Adding an item.** The URL is reduced to its product code (`…-d10818009.htm`),
which is what the app tracks — the query string and any tracking parameters are
discarded, so the same product pasted twice lands once. The page is fetched
immediately, so the card is never blank.

**Reading a price.** Every product page carries a schema.org JSON-LD block with the
name, image, availability and price. That is what gets parsed; CSS selectors against
the rendered markup would break on the next redesign. Three prices are read, because
the shelf price is often not what you would pay:

| | Where it comes from |
|---|---|
| Normal price | `offers.price` |
| Discount code (*"s kódom od …"*) | the `priceSpecification` entry typed `SalePrice`, when it is lower |
| AlzaPlus+ members' price | the rendered price box — schema.org cannot express "cheaper if you subscribe" |

The members' price is hidden unless you turn on **AlzaPlus+** in the menu, since a
price you cannot pay is worse than none.

**Getting past Cloudflare.** alza.sk fingerprints the TLS handshake. A client
offering the usual spread of TLS versions is refused with a 403 no matter what
headers it sends; advertising a single version is accepted. The HTTP version has
nothing to do with it. That one line, plus a complete browser header set, is the
whole trick — see the comment on the typed client in `Program.cs`.

**Checking on a schedule.** Requests are grouped by product code, so a product on
ten lists costs one request per sweep, spaced with jitter. A block aborts the sweep
and backs off. The worker sleeps until the earliest item is actually *due*, not on a
fixed timer — those are different clocks, and treating them as one makes a six-hour
interval behave like twelve.

There is deliberately **no "check now" button**. Adding an item is the only scrape a
person can trigger, so nothing you click repeatedly can generate traffic to alza.sk.

**Recording history.** A snapshot is written only when a price or availability
actually *changes*. Checks that see no change just advance the "last checked" time.
The history is therefore a list of transitions rather than one row per poll, which
is what makes the chart worth looking at.

## Accounts and lists

The account key is in the address bar and nowhere else — nothing is stored in the
browser. So:

- **Losing the URL loses the account.** There is no recovery. The app says so until
  you acknowledge it.
- **Anyone with the link has full access** to every list on it, and the key travels
  in browser history and proxy logs like any URL. Treat it as a clickable password.

Lists live at `/user/{key}/list/{listId}`. Ids appear without dashes; the API takes
either form.

Adding an account key to `Admin:Keys` in configuration unlocks an admin section
listing every account, product and snapshot, with whole-server backup and restore.
Admin rights come from configuration rather than the database, so an administrator
can still restore into an empty one.

## Database

SQLite by default — a file, no server. For MySQL:

```jsonc
"Database":          { "Provider": "MySql" },
"ConnectionStrings": { "Default": "server=db;database=alzawatchdog;user=…;CharSet=utf8mb4" }
```

or with containers, `podman-compose -f docker-compose.yml -f docker-compose.mysql.yml up -d`.
The API logs which engine it started on.

Two things worth knowing before changing an entity:

- **Migrations are per-provider.** `AppDbContext` is the SQLite context;
  `MySqlAppDbContext` subclasses it only to own `Data/Migrations/MySql`. Change the
  model and you must regenerate both, or MySQL deployments drift.
- **Prices are stored as text and timestamps as integers on both engines.** SQLite
  has no decimal type and cannot sort a `DateTimeOffset`. Keeping the shape uniform
  means a query cannot behave differently depending on where it is deployed — so
  never sort or aggregate a price in SQL; do it in memory.

The admin backup is provider-agnostic, so it doubles as the way to move between
engines: export from SQLite, switch provider, import. Ids survive, so bookmarks keep
working.

## Tests

```bash
dotnet test
```

Parser tests run against real pages captured from alza.sk in
`backend/AlzaWatchdog.Tests/Fixtures/`, so they need no network. `LiveScrapeTests`
does hit the real site and is skipped by default — run it after touching anything
about how the scraper talks to the network, because it is the only thing that
catches Cloudflare changing its mind:

```bash
dotnet test --filter FullyQualifiedName~LiveScrapeTests
```

There is no frontend test suite.
