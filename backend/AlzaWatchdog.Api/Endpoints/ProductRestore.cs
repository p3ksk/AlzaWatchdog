using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Api.Endpoints;

/// <summary>The product-level fields a backup carries flattened onto each item.</summary>
internal readonly record struct ProductFacts(
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
    DateTimeOffset CreatedAt);

internal static class ProductRestore
{
    /// <summary>
    /// Finds the product with this code, or creates it from the backup's flattened
    /// fields.
    ///
    /// Bundles are written one entry per tracked item, so the same product appears
    /// once for every list that watched it. This is what folds those back onto the
    /// single shared row — and the <c>Created</c> flag tells the caller whether the
    /// entry's history is new or a duplicate copy that should be dropped.
    /// </summary>
    public static async Task<(Product Product, bool Created)> EnsureAsync(
        AppDbContext db,
        Dictionary<string, Product> seen,
        ProductFacts facts,
        CancellationToken ct)
    {
        if (seen.TryGetValue(facts.ProductCode, out var cached))
            return (cached, false);

        var existing = await db.Products
            .FirstOrDefaultAsync(p => p.ProductCode == facts.ProductCode, ct);

        if (existing is not null)
        {
            seen[facts.ProductCode] = existing;
            return (existing, false);
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            ProductCode = facts.ProductCode,
            CanonicalUrl = facts.CanonicalUrl,
            Name = facts.Name,
            ImageUrl = facts.ImageUrl,
            Currency = facts.Currency,
            LastPrice = facts.LastPrice,
            LastPlusPrice = facts.LastPlusPrice,
            LastCouponPrice = facts.LastCouponPrice,
            LastAvailability = facts.LastAvailability,
            LastCheckedAt = facts.LastCheckedAt,
            LastError = facts.LastError,
            ConsecutiveFailures = facts.ConsecutiveFailures,
            IsActive = facts.IsActive,
            CreatedAt = facts.CreatedAt,
        };

        db.Products.Add(product);
        seen[facts.ProductCode] = product;
        return (product, true);
    }
}
