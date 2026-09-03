namespace AlzaWatchdog.Api.Contracts;

public record AccountDto(Guid UserId, bool HasAlzaPlus, bool IsAdmin, IReadOnlyList<WatchListDto> Lists);

public record UpdateAccountRequest(bool HasAlzaPlus);

public record WatchListDto(Guid Id, string Name, DateTimeOffset CreatedAt, int ItemCount);

public record SaveListRequest(string Name);

public record AddItemRequest(string Url);

/// <summary>Every tracked item in the new manual order, oldest position first.</summary>
public record ReorderItemsRequest(IReadOnlyList<Guid> OrderedItemIds);

public record PriceSnapshotDto(
    decimal? Price,
    decimal? PlusPrice,
    decimal? CouponPrice,
    string? Availability,
    DateTimeOffset CapturedAt);

/// <summary>A list plus everything needed to render it in one request.</summary>
public record WatchListDetailDto(Guid Id, string Name, IReadOnlyList<TrackedItemDto> Items);

public record TrackedItemDto(
    Guid Id,
    string ProductCode,
    string Url,
    string? Name,
    /// <summary>Product image as an in-memory-cached data URI, so the browser never hits the CDN directly.</summary>
    string? ImageDataUri,
    decimal? CurrentPrice,
    decimal? PreviousPrice,
    /// <summary>Cheaper price for AlzaPlus+ members, when this product offers one.</summary>
    decimal? PlusPrice,
    /// <summary>Price with a discount code applied, when this product offers one.</summary>
    decimal? CouponPrice,
    /// <summary>Cheapest and dearest ever recorded, so the UI can answer "is now a good time?".</summary>
    decimal? LowestPrice,
    decimal? HighestPrice,
    string? Currency,
    string? Availability,
    DateTimeOffset? LastCheckedAt,
    string? LastError,
    bool IsActive,
    /// <summary>Position in the user's manual order. Zero-based; ties fall back to creation order.</summary>
    int SortOrder,
    DateTimeOffset CreatedAt,
    /// <summary>Full recorded history, oldest first — enough to draw the card's chart without a second call.</summary>
    IReadOnlyList<PriceSnapshotDto> History)
{
    /// <summary>Positive when the price went up, negative on a drop, null when unknown.</summary>
    public decimal? PriceChange => CurrentPrice is null || PreviousPrice is null
        ? null
        : CurrentPrice - PreviousPrice;

    /// <summary>True when the current price ties the cheapest ever seen, and we have seen more than one.</summary>
    public bool IsAtLowest => CurrentPrice is not null
                              && LowestPrice is not null
                              && CurrentPrice == LowestPrice
                              && History.Count > 1;

}
