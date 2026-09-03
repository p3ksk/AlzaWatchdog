namespace AlzaWatchdog.Api.Workers;

public class WatchdogOptions
{
    public const string SectionName = "Watchdog";

    /// <summary>How long to wait between full sweeps of every tracked product.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Base pause between two product requests within a sweep; jitter is added on top.</summary>
    public TimeSpan DelayBetweenRequests { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Upper bound of the random jitter added to <see cref="DelayBetweenRequests"/>.</summary>
    public TimeSpan RequestJitter { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long to stand down after alza.sk blocks us mid-sweep.</summary>
    public TimeSpan BlockedBackoff { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Failures in a row before an item is deactivated. Blocks do not count.</summary>
    public int MaxConsecutiveFailures { get; set; } = 10;

    /// <summary>Set false to keep the background sweep from running (useful in tests).</summary>
    public bool Enabled { get; set; } = true;
}
