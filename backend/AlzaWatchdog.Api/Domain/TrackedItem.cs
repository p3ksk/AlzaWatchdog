namespace AlzaWatchdog.Api.Domain;

/// <summary>
/// One alza.sk product on one watch list. The same product on several lists
/// produces several rows, but only ever one HTTP request per sweep — the worker
/// groups by <see cref="ProductCode"/>.
/// </summary>
public class TrackedItem
{
    public Guid Id { get; set; }

    public Guid WatchListId { get; set; }
    public WatchList WatchList { get; set; } = null!;

    /// <summary>The numeric part of a "-d10818009.htm" URL suffix.</summary>
    public required string ProductCode { get; set; }

    /// <summary>Query string and fragment stripped, so tracking params dedupe away.</summary>
    public required string CanonicalUrl { get; set; }

    public string? Name { get; set; }
    public string? ImageUrl { get; set; }
    public string? Currency { get; set; }

    public decimal? LastPrice { get; set; }

    /// <summary>Cheaper price for AlzaPlus+ members, when the product offers one.</summary>
    public decimal? LastPlusPrice { get; set; }

    /// <summary>Price with a discount code applied, when the product offers one.</summary>
    public decimal? LastCouponPrice { get; set; }
    public string? LastAvailability { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>Last failure reason, surfaced to the UI. Null once a check succeeds.</summary>
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }

    /// <summary>Cleared after too many consecutive failures so we stop hammering a dead URL.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Position within its list when the user chooses a manual order. Ties fall back to creation order.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<PriceSnapshot> Snapshots { get; set; } = [];
}
