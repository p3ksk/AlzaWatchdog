namespace AlzaWatchdog.Api.Domain;

/// <summary>
/// The account. Its id is the key a person keeps to recover every list they own
/// on another device; it is not what appears in a bookmarkable list URL.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// Whether this person actually holds an AlzaPlus+ membership. Off by default:
    /// a price most visitors cannot pay is worse than no price at all. The members'
    /// price is always scraped and stored regardless — this only decides whether it
    /// is shown and counted, so switching it on reveals the history already there.
    /// </summary>
    public bool HasAlzaPlus { get; set; }

    public List<WatchList> Lists { get; set; } = [];
}
