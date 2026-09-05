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
    string ProductCode,
    string Url,
    string? Name,
    string? Currency,
    decimal? LastPrice,
    decimal? LastPlusPrice,
    decimal? LastCouponPrice,
    DateTimeOffset? LastCheckedAt,
    string? LastError,
    int ConsecutiveFailures,
    bool IsActive,
    IReadOnlyList<PriceSnapshotDto> Snapshots);

public record AdminWorkerSettingDto(string Label, string Value, string? Hint);

/// <summary>One background worker, as shown in the admin section.</summary>
public record AdminWorkerDto(
    string Name,
    string Description,
    bool Enabled,
    /// <summary>True while the worker has never reported finishing a pass.</summary>
    bool Idle,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    string? LastOutcome,
    int Runs,
    IReadOnlyList<AdminWorkerSettingDto> Settings,
    /// <summary>Live context the worker itself cannot know, e.g. how much is queued.</summary>
    IReadOnlyList<AdminWorkerSettingDto> Now);

public record AdminStatsDto(
    int Users,
    int Lists,
    int Items,
    int DistinctProducts,
    int Snapshots,
    int InactiveItems);

// ---------------------------------------------------------------------------
// Export / import. Watched products, flat, with their full snapshot history.
//
// Deliberately carries no ids — not the account it came from, and not the tracked
// item rows. A bundle describes *what* was watched, so it can be imported into any
// account, including a fresh one on another machine. Carrying the account id only
// ever refused imports that would have worked, and reusing tracked-item ids made a
// second import collide on the primary key.
//
// Removing those fields keeps the format readable: a bundle written before this
// change still imports, because the extra properties are simply ignored.
// ---------------------------------------------------------------------------

public record ProductExportBundle(
    string Format,
    DateTimeOffset ExportedAt,
    IReadOnlyList<ExportItem> Items)
{
    public const string CurrentFormat = "alza-watchdog/products/v1";
}

public record ExportItem(
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
