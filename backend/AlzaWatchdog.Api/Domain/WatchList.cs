namespace AlzaWatchdog.Api.Domain;

/// <summary>
/// A named group of tracked products, e.g. "Home office".
///
/// Its id is the credential *and* the address: it appears in the URL as
/// /l/{id}, so bookmarking a list is all it takes to come back to it, and
/// anyone holding that link has access to that list.
/// </summary>
public class WatchList
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<TrackedItem> Items { get; set; } = [];
}
