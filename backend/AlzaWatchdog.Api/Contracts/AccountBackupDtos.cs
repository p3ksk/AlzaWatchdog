namespace AlzaWatchdog.Api.Contracts;

/// <summary>
/// Whole-account backup, used by the admin section.
///
/// Distinct from <see cref="ProductExportBundle"/> on purpose: that one is a
/// person exporting their own products and is refused if it comes from another
/// account. This one carries accounts themselves — their keys, lists and history
/// — so an administrator can move or restore them.
/// </summary>
public record AccountBackupBundle(
    string Format,
    DateTimeOffset ExportedAt,
    IReadOnlyList<BackupAccount> Accounts)
{
    public const string CurrentFormat = "alza-watchdog/accounts/v1";
}

public record BackupAccount(
    Guid Id,
    bool HasAlzaPlus,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<BackupList> Lists);

public record BackupList(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    IReadOnlyList<BackupItem> Items);

public record BackupItem(
    Guid Id,
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
    DateTimeOffset CreatedAt,
    IReadOnlyList<PriceSnapshotDto> Snapshots);

public record AccountImportResultDto(
    int AccountsImported,
    int AccountsSkipped,
    int ListsImported,
    int ItemsImported,
    int SnapshotsImported,
    IReadOnlyList<string> Notes);
