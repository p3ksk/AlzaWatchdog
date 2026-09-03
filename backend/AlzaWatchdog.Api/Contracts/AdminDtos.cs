namespace AlzaWatchdog.Api.Contracts;

/// <summary>One row of the admin users table.</summary>
public record AdminUserDto(
    Guid Id,
    bool HasAlzaPlus,
    bool IsAdmin,
    int ListCount,
    int ItemCount,
    int SnapshotCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

/// <summary>One tracked product, with the account and list it belongs to.</summary>
public record AdminItemDto(
    Guid Id,
    Guid UserId,
    Guid ListId,
    string ListName,
    string ProductCode,
    string Url,
    string? Name,
    string? Currency,
    decimal? LastPrice,
    decimal? LastPlusPrice,
    decimal? LastCouponPrice,
    string? LastAvailability,
    DateTimeOffset? LastCheckedAt,
    /// <summary>Estimated next sweep time, derived from the last check plus the configured interval. Null when the item is paused.</summary>
    DateTimeOffset? NextCheckAt,
    string? LastError,
    int ConsecutiveFailures,
    bool IsActive,
    int SortOrder,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PriceSnapshotDto> Snapshots);

public record AdminStatsDto(
    int Users,
    int Lists,
    int Items,
    int DistinctProducts,
    int Snapshots,
    int InactiveItems);

// ---------------------------------------------------------------------------
// Export / import. One user's watched products, flat, with their full snapshot
// history. Exporting and importing products — not whole accounts — is the only
// restore operation an admin needs, and it never touches account rows or lists.
// ---------------------------------------------------------------------------

public record ProductExportBundle(
    string Format,
    DateTimeOffset ExportedAt,
    Guid UserId,
    IReadOnlyList<ExportItem> Items)
{
    public const string CurrentFormat = "alza-watchdog/products/v1";
}

public record ExportItem(
    Guid Id,
    string ListName,
    string ProductCode,
    string CanonicalUrl,
    string? Name,
    string? ImageUrl,
    string? Currency,
    decimal? LastPrice,
    decimal? LastPlusPrice,
    decimal? LastCouponPrice,
    string? LastAvailability,
    DateTimeOffset? LastCheckedAt,
    string? LastError,
    int ConsecutiveFailures,
    bool IsActive,
    int SortOrder,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PriceSnapshotDto> Snapshots);

public record ImportResultDto(
    int ItemsImported,
    int ItemsSkipped,
    int SnapshotsImported,
    IReadOnlyList<string> Notes);
