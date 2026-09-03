namespace AlzaWatchdog.Api.Domain;

/// <summary>
/// A point in the price history. Rows are appended only when the price or
/// availability actually changed (and on the first successful check), so the
/// history reads as a list of transitions rather than one row per poll.
/// </summary>
public class PriceSnapshot
{
    public long Id { get; set; }

    public Guid TrackedItemId { get; set; }
    public TrackedItem TrackedItem { get; set; } = null!;

    public decimal? Price { get; set; }
    public decimal? PlusPrice { get; set; }
    public decimal? CouponPrice { get; set; }
    public string? Availability { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}
