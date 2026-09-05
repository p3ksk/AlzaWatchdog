namespace AlzaWatchdog.Api.Scraping;

public enum ScrapeStatus
{
    /// <summary>Product data was read successfully.</summary>
    Success,

    /// <summary>The URL is well-formed but alza.sk has no such product (404).</summary>
    ProductNotFound,

    /// <summary>Cloudflare turned us away (403/429 or an interstitial body). Never retry — back off.</summary>
    Blocked,

    /// <summary>We got a page but could not find the product JSON-LD in it.</summary>
    ParseFailed,

    /// <summary>Network error, timeout or 5xx. Safe to retry.</summary>
    TransientError,
}

public record ScrapeResult(
    ScrapeStatus Status,
    string? Name = null,
    decimal? Price = null,
    /// <summary>The cheaper price offered to AlzaPlus+ members, when the page shows one.</summary>
    decimal? PlusPrice = null,
    /// <summary>The price with a discount code applied ("s kódom"), when the page offers one.</summary>
    decimal? CouponPrice = null,
    string? Currency = null,
    string? Availability = null,
    string? ImageUrl = null,
    string? Error = null)
{
    public bool IsSuccess => Status == ScrapeStatus.Success;

    public static ScrapeResult Failure(ScrapeStatus status, string error) => new(status, Error: error);
}
