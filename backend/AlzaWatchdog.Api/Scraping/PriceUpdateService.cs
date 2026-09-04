using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Workers;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Api.Scraping;

/// <summary>
/// Applies a <see cref="ScrapeResult"/> to a product. Shared by the background
/// sweep and the add endpoint so both record history the same way. Does not call
/// SaveChanges — the caller decides the transaction boundary.
///
/// Everything here is per-product, not per-list: one scrape updates the single
/// row every watch list points at, so a product on ten lists is written once.
/// </summary>
public class PriceUpdateService(IOptions<WatchdogOptions> options)
{
    private readonly WatchdogOptions _options = options.Value;

    public void Apply(AppDbContext db, Product item, ScrapeResult result, DateTimeOffset now)
    {
        // A block means we never got to look at this product, so it must stay due.
        // Marking it checked would defer it by a whole CheckInterval and let it go
        // quietly stale: the sweep after a block would skip it entirely, which is
        // how a run of blocks silently drains the list of anything worth checking.
        if (result.Status != ScrapeStatus.Blocked)
            item.LastCheckedAt = now;

        if (!result.IsSuccess)
        {
            RecordFailure(item, result);
            return;
        }

        // A snapshot is worth storing on the first successful check, and thereafter
        // only when something actually moved. Polls that see no change just advance
        // LastCheckedAt, so the history stays a list of transitions.
        //
        // A members' or coupon price moving on its own counts: an unchanged shelf
        // price with a new discount behind it is exactly the event worth recording.
        var isFirstCheck = item.LastPrice is null && item.LastAvailability is null;
        var hasChanged = item.LastPrice != result.Price
                         || item.LastPlusPrice != result.PlusPrice
                         || item.LastCouponPrice != result.CouponPrice
                         || item.LastAvailability != result.Availability;

        if (isFirstCheck || hasChanged)
        {
            db.PriceSnapshots.Add(new PriceSnapshot
            {
                ProductId = item.Id,
                Price = result.Price,
                PlusPrice = result.PlusPrice,
                CouponPrice = result.CouponPrice,
                Availability = result.Availability,
                CapturedAt = now,
            });
        }

        item.Name = result.Name ?? item.Name;
        item.ImageUrl = result.ImageUrl ?? item.ImageUrl;
        item.Currency = result.Currency ?? item.Currency;
        item.LastPrice = result.Price;
        item.LastPlusPrice = result.PlusPrice;
        item.LastCouponPrice = result.CouponPrice;
        item.LastAvailability = result.Availability;
        item.LastError = null;
        item.ConsecutiveFailures = 0;
    }

    private void RecordFailure(Product item, ScrapeResult result)
    {
        item.LastError = result.Error ?? result.Status.ToString();

        // Being blocked says nothing about this particular product, so it must not
        // count towards deactivating it — otherwise one Cloudflare episode would
        // switch off every item in the list.
        if (result.Status == ScrapeStatus.Blocked)
            return;

        item.ConsecutiveFailures++;

        if (item.ConsecutiveFailures >= _options.MaxConsecutiveFailures)
            item.IsActive = false;
    }
}
