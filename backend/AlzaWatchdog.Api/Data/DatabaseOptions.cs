namespace AlzaWatchdog.Api.Data;

public enum DatabaseProvider
{
    Sqlite,
    MySql,
}

public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Which engine to run against. SQLite needs no server and stays the default.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;
}
