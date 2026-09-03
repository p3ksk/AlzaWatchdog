using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Workers;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Api.Scraping;

/// <summary>
/// Applies a <see cref="ScrapeResult"/> to tracked items. Shared by the background
/// sweep and the manual refresh endpoint so both record history the same way.
/// Does not call SaveChanges — the caller decides the transaction boundary.
/// </summary>
public class PriceUpdateService(IOptions<WatchdogOptions> options)
{
    private readonly WatchdogOptions _options = options.Value;

    public void Apply(AppDbContext db, TrackedItem item, ScrapeResult result, DateTimeOffset now)
    {
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
                TrackedItemId = item.Id,
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

    private void RecordFailure(TrackedItem item, ScrapeResult result)
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
