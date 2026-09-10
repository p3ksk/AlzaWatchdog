import { PriceSnapshot, TrackedItem } from './models';

/**
 * Alza can quote three prices for one product, and they are alternatives rather
 * than discounts that stack: the shelf price, the price with a discount code, and
 * the price for AlzaPlus+ members.
 */
export interface PriceQuote {
  price: number | null;
  plusPrice: number | null;
  couponPrice: number | null;
}

export type DiscountLabel = 'AlzaPlus+' | 'With code';

/** A price that can actually be paid, and what makes it cheaper than the shelf price. */
export interface PayablePrice {
  /** The least this can be had for. */
  value: number | null;
  /** The shelf price, kept so the card can show what the discount is measured against. */
  shelf: number | null;
  /** Which offer wins, or null when the shelf price is already the cheapest. */
  via: DiscountLabel | null;
}

/**
 * The lowest of the three prices, counting the members' price only for a member.
 * The one place this rule lives — it used to exist twice, and the two disagreed.
 */
export function payable(quote: PriceQuote, hasAlzaPlus: boolean): PayablePrice {
  const offers: { label: DiscountLabel; price: number }[] = [];

  if (quote.couponPrice !== null) offers.push({ label: 'With code', price: quote.couponPrice });
  if (hasAlzaPlus && quote.plusPrice !== null) offers.push({ label: 'AlzaPlus+', price: quote.plusPrice });

  const best = offers.reduce<{ label: DiscountLabel; price: number } | null>(
    (winner, offer) => (winner === null || offer.price < winner.price ? offer : winner),
    null,
  );

  // A discount that does not beat the shelf price is not a discount. Alza does
  // show those — a members' price equal to the normal one is common.
  if (best === null || (quote.price !== null && best.price >= quote.price)) {
    return { value: quote.price, shelf: quote.price, via: null };
  }

  return { value: best.price, shelf: quote.price, via: best.label };
}

/** The live prices on a tracked item, in the shape {@link payable} expects. */
export function quoteOf(item: TrackedItem): PriceQuote {
  return { price: item.currentPrice, plusPrice: item.plusPrice, couponPrice: item.couponPrice };
}

export interface PriceStats extends PayablePrice {
  /** The payable price before the most recent change. */
  previous: number | null;
  /** Positive when it rose, negative on a drop, null when there is nothing to compare. */
  change: number | null;
  lowest: number | null;
  highest: number | null;
  /** The payable price ties the cheapest ever recorded, and there is more than one reading. */
  isAtLowest: boolean;
}

/**
 * Everything the card states about a price, measured on what this person can pay.
 * "Now" comes from the item, not the last snapshot: snapshots record transitions,
 * the item carries the live reading.
 */
export function priceStats(item: TrackedItem, hasAlzaPlus: boolean): PriceStats {
  const now = payable(quoteOf(item), hasAlzaPlus);

  const series = item.history
    .map((snapshot) => payable(snapshot, hasAlzaPlus).value)
    .filter((value): value is number => value !== null);

  // The row before the newest one holds the price this item moved away from.
  const previous = item.history.length > 1
    ? payable(item.history[item.history.length - 2], hasAlzaPlus).value
    : null;

  const lowest = series.length > 0 ? Math.min(...series) : null;
  const highest = series.length > 0 ? Math.max(...series) : null;

  return {
    ...now,
    previous,
    change: now.value !== null && previous !== null ? now.value - previous : null,
    lowest,
    highest,
    isAtLowest: now.value !== null && lowest !== null && now.value === lowest && item.history.length > 1,
  };
}

/** The payable price of one recorded reading, for plotting a history. */
export function payableAt(snapshot: PriceSnapshot, hasAlzaPlus: boolean): number | null {
  return payable(snapshot, hasAlzaPlus).value;
}

/** The middle value, averaging the two middle ones for an even count. */
export function median(values: readonly number[]): number | null {
  if (values.length === 0) {
    return null;
  }

  const sorted = [...values].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);

  return sorted.length % 2 === 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
}
