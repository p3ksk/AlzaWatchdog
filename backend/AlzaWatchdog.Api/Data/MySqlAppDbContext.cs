using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Api.Data;

/// <summary>
/// The same model as <see cref="AppDbContext"/>, existing only to own a separate
/// set of migrations.
///
/// Migrations are provider-specific — the SQL that creates a SQLite table is not
/// the SQL that creates a MySQL one — and EF matches a migration to a context by
/// type. Keeping <see cref="AppDbContext"/> as the SQLite context leaves the
/// already-applied SQLite migration and its history rows untouched, which matters
/// because there are live SQLite databases in the wild; MySQL gets its own set
/// under Data/Migrations/MySql.
/// </summary>
public class MySqlAppDbContext(DbContextOptions<MySqlAppDbContext> options) : AppDbContext(options);
