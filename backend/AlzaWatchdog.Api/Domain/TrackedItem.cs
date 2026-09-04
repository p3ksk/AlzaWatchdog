namespace AlzaWatchdog.Api.Domain;

/// <summary>
/// A product's place on one watch list — the join between the two, plus the
/// things that are genuinely per-list: when it was added and where the owner
/// dragged it. Prices and history belong to the <see cref="Product"/>.
/// </summary>
public class TrackedItem
{
    public Guid Id { get; set; }

    public Guid WatchListId { get; set; }
    public WatchList WatchList { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Position within its list when the user chooses a manual order. Ties fall back to creation order.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
