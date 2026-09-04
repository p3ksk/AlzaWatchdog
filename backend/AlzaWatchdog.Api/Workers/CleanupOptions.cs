namespace AlzaWatchdog.Api.Workers;

public class CleanupOptions
{
    public const string SectionName = "Cleanup";

    /// <summary>Deletion is irreversible, so it can be switched off entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Log what would be deleted without deleting it. Useful before trusting new limits.</summary>
    public bool DryRun { get; set; }

    /// <summary>How often to sweep for abandoned accounts.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// How long an account with no products may sit untouched. These are the
    /// throwaways: someone opened the site, got a key, and never came back.
    /// </summary>
    public TimeSpan EmptyAccountAge { get; set; } = TimeSpan.FromDays(2);

    /// <summary>
    /// How long an account *with* products may sit untouched. Much longer, because
    /// this is someone's actual watch list and the only way back is a bookmark
    /// they may not have opened in months.
    /// </summary>
    public TimeSpan InactiveAccountAge { get; set; } = TimeSpan.FromDays(182);
}
