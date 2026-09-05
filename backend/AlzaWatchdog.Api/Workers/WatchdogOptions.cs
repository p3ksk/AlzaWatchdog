namespace AlzaWatchdog.Api.Workers;

public class WatchdogOptions
{
    public const string SectionName = "Watchdog";

    /// <summary>How long to wait between full sweeps of every tracked product.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Base pause between two product requests within a sweep; jitter is added on top.</summary>
    public TimeSpan DelayBetweenRequests { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How far ahead of its due time a product joins the sweep that is running
    /// anyway. Without it each product drifts onto its own schedule, every sweep
    /// carries one product, and the jitter — which only applies between products
    /// in one sweep — never happens.
    /// </summary>
    public TimeSpan SweepWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Upper bound of the random jitter added to <see cref="DelayBetweenRequests"/>.</summary>
    public TimeSpan RequestJitter { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long to stand down after alza.sk blocks us mid-sweep.</summary>
    public TimeSpan BlockedBackoff { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Pause before the one retry a challenged opening request gets. Zero disables it.</summary>
    public TimeSpan ChallengeRetryDelay { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>The same for an added product, shorter because someone is waiting on it.</summary>
    public TimeSpan InteractiveChallengeRetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Failures in a row before an item is deactivated. Blocks do not count.</summary>
    public int MaxConsecutiveFailures { get; set; } = 10;

    /// <summary>Set false to keep the background sweep from running (useful in tests).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Logs every alza.sk request in full. Noisy, and prints cookies.</summary>
    public bool LogRequests { get; set; }
}
