namespace AlzaWatchdog.Api.Domain;

/// <summary>
/// An alza.sk product, stored once however many people watch it.
///
/// Everything the scraper learns lives here — the name, the prices, the failure
/// state and the whole price history. Watch lists point at it through
/// <see cref="TrackedItem"/> rather than each keeping their own copy, so a product
/// on ten lists is one row with one history instead of ten of each.
/// </summary>
public class Product
{
    public Guid Id { get; set; }

    /// <summary>The numeric part of a "-d10818009.htm" URL suffix. Unique: it is the product's identity.</summary>
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

    public DateTimeOffset CreatedAt { get; set; }

    public List<PriceSnapshot> Snapshots { get; set; } = [];
    public List<TrackedItem> TrackedBy { get; set; } = [];
}
