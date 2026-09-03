/** Presentation helpers shared by the item card and the history panel. */

export function formatPrice(value: number | null | undefined, currency: string | null): string {
  if (value === null || value === undefined) {
    return '—';
  }

  return new Intl.NumberFormat('sk-SK', {
    style: 'currency',
    currency: currency ?? 'EUR',
    // Prices under a euro still read as money rather than "1 €".
    minimumFractionDigits: 2,
  }).format(value);
}

const RELATIVE = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });

const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 60 * 60_000],
  ['month', 30 * 24 * 60 * 60_000],
  ['day', 24 * 60 * 60_000],
  ['hour', 60 * 60_000],
  ['minute', 60_000],
];

/**
 * "2 hours ago" / "in 4 hours". A watchdog is about freshness, and an absolute
 * timestamp makes the reader do the arithmetic. The exact time stays available
 * as a tooltip wherever this is used.
 */
export function formatRelative(iso: string | null | undefined, now = Date.now()): string {
  if (!iso) {
    return 'never';
  }

  const delta = new Date(iso).getTime() - now;

  for (const [unit, ms] of UNITS) {
    if (Math.abs(delta) >= ms) {
      return RELATIVE.format(Math.round(delta / ms), unit);
    }
  }

  return delta < 0 ? 'just now' : 'any moment';
}

export function formatExact(iso: string | null | undefined): string {
  return iso ? new Date(iso).toLocaleString() : '';
}

/** Absolute timestamp plus a relative hint, for admin rows where both matter. */
export function formatWhen(iso: string | null | undefined, now = Date.now()): string {
  if (!iso) {
    return 'never';
  }

  return `${new Date(iso).toLocaleString()} (${formatRelative(iso, now)})`;
}

const AVAILABILITY: Record<string, string> = {
  InStock: 'In stock',
  OutOfStock: 'Out of stock',
  PreOrder: 'Pre-order',
  BackOrder: 'Back-order',
  Discontinued: 'Discontinued',
  LimitedAvailability: 'Limited',
  SoldOut: 'Sold out',
};

export function formatAvailability(value: string | null): string {
  if (!value) {
    return 'Unknown';
  }
  // Fall back to splitting the raw schema.org token, so an unmapped value like
  // "InStoreOnly" still reads as words rather than as jargon.
  return AVAILABILITY[value] ?? value.replace(/([a-z])([A-Z])/g, '$1 $2');
}
