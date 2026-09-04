using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AlzaWatchdog.Api.Data;

/// <summary>
/// Lets `dotnet ef` build the SQLite context without going through Program.cs,
/// which only ever registers whichever provider is configured at the time.
/// </summary>
public class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options);
}
