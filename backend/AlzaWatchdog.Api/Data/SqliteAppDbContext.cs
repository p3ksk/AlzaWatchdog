using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Api.Data;

/// <summary>
/// The SQLite context. Owns <c>Data/Migrations</c>, the original migration set,
/// so existing SQLite databases keep matching their recorded history.
/// </summary>
public class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : AppDbContext(options);
