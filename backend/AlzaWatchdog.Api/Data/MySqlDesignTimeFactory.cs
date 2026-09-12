using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AlzaWatchdog.Api.Data;

/// <summary>
/// Lets `dotnet ef` build the MySQL context without a running server.
///
/// The tooling would otherwise have to go through Program.cs, which only ever
/// registers one provider — so generating MySQL migrations would depend on the
/// app being configured for MySQL at the time. The connection string here is
/// never connected to when scaffolding; only the provider's SQL generator is used.
/// </summary>
public class MySqlDesignTimeFactory : IDesignTimeDbContextFactory<MySqlAppDbContext>
{
    public MySqlAppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                               ?? "server=localhost;database=alzawatchdog;user=root;password=root";

        var options = new DbContextOptionsBuilder<MySqlAppDbContext>()
            .UseMySql(connectionString, new MariaDbServerVersion(new Version(10, 11, 0)))
            .Options;

        return new MySqlAppDbContext(options);
    }
}
