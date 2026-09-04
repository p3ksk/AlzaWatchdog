namespace AlzaWatchdog.Api.Domain;

/// <summary>
/// A point in a product's price history, shared by everyone watching it. Rows are
/// appended only when a price or availability actually changed (and on the first
/// successful check), so the history reads as a list of transitions rather than
/// one row per poll.
/// </summary>
public class PriceSnapshot
{
    public long Id { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal? Price { get; set; }
    public decimal? PlusPrice { get; set; }
    public decimal? CouponPrice { get; set; }
    public string? Availability { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}
