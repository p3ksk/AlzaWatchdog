import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { PriceSnapshot } from '../../core/models';
import { formatExact, formatPrice } from '../../core/format';
import { median, payableAt } from '../../core/pricing';
import { steppedAreaPath, steppedPath } from './stepped-path';

type Range = '30d' | '90d' | '1y' | 'all';
type Variant = 'watchlist' | 'admin';

interface DataPoint {
  t: number;
  price: number;
}

interface PlacedPoint extends DataPoint {
  x: number;
  y: number;
}

const RANGE_MS: Record<Exclude<Range, 'all'>, number> = {
  '30d': 30 * 864e5,
  '90d': 90 * 864e5,
  '1y': 365 * 864e5,
};

const GEO: Record<Variant, { height: number; plotBottom: number; tickTop: number; xLabelY: number }> = {
  watchlist: { height: 238, plotBottom: 190, tickTop: 198, xLabelY: 224 },
  admin: { height: 212, plotBottom: 146, tickTop: 172, xLabelY: 198 },
};

const PLOT_LEFT = 56;
const PLOT_RIGHT = 900;
const PLOT_TOP = 20;
const Y_LABEL_X = 46;
const VIEW_W = 920;

/**
 * The full price history of one product. Built once, mounted in two places: a
 * detail panel under a selected watchlist row, and inside an expanded admin row
 * above the snapshot table.
 *
 * A price is a step function, so the line is stepped (`H`/`V` only): every
 * vertical is a price change, every horizontal is how long that price held.
 * Every snapshot is a tick on the axis, so sparse coverage stays visible instead
 * of looking like a smooth trend.
 */
@Component({
  selector: 'app-price-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './price-chart.component.html',
  styleUrl: './price-chart.component.scss',
})
export class PriceChartComponent {
  readonly history = input.required<PriceSnapshot[]>();
  readonly currency = input<string | null>(null);
  readonly hasAlzaPlus = input(false);
  /** Current AlzaPlus+ price; draws the dashed reference line. Omit when there is none. */
  readonly plusPrice = input<number | null>(null);
  readonly variant = input<Variant>('watchlist');
  /** The visually-hidden data table. Off in admin, where a real table sits below. */
  readonly srTable = input(true);

  private readonly range = signal<Range>('90d');
  protected readonly hoveredIndex = signal<number | null>(null);

  protected readonly geo = computed(() => GEO[this.variant()]);
  protected readonly viewBox = computed(() => `0 0 ${VIEW_W} ${this.geo().height}`);

  /** Snapshots reduced to the payable price, oldest first, nulls dropped. */
  private readonly valid = computed<DataPoint[]>(() =>
    this.history()
      .map((s) => ({ t: new Date(s.capturedAt).getTime(), price: payableAt(s, this.hasAlzaPlus()) }))
      .filter((p): p is DataPoint => p.price !== null)
      .sort((a, b) => a.t - b.t),
  );

  protected readonly ranges = computed(() => {
    const keys: Range[] = this.variant() === 'admin' ? ['30d', '90d', 'all'] : ['30d', '90d', '1y', 'all'];
    const valid = this.valid();
    const now = Date.now();
    const span = valid.length > 0 ? valid[valid.length - 1].t - valid[0].t : 0;

    return keys.map((key) => ({
      key,
      label: key === 'all' ? 'ALL' : key === '1y' ? '1 Y' : key === '90d' ? '90 D' : '30 D',
      // A preset earns its place only if the history reaches past its window.
      enabled: key === 'all' || (valid.length > 1 && span > RANGE_MS[key] * 0.95),
    }));
  });

  protected readonly activeRange = computed(() => {
    const wanted = this.range();
    return this.ranges().find((r) => r.key === wanted)?.enabled ? wanted : 'all';
  });

  /** Everything the SVG needs, derived in one pass so the geometry stays consistent. */
  protected readonly model = computed(() => {
    const valid = this.valid();
    const { plotBottom } = this.geo();

    if (valid.length === 0) {
      return null;
    }

    const now = Date.now();
    const range = this.activeRange();
    const last = valid[valid.length - 1];

    const domainStart =
      range === 'all' || valid.length === 1
        ? valid[0].t
        : Math.max(valid[0].t, now - RANGE_MS[range]);
    const domainEnd = last.t;
    const domainSpan = domainEnd - domainStart || 1;

    const windowValid = valid.filter((p) => p.t >= domainStart);
    const prior = [...valid].reverse().find((p) => p.t < domainStart) ?? null;

    const xOf = (t: number) =>
      PLOT_LEFT + Math.min(1, Math.max(0, (t - domainStart) / domainSpan)) * (PLOT_RIGHT - PLOT_LEFT);

    // Y scale over what is actually on screen, plus the AlzaPlus+ line if drawn.
    const plus = this.plusPrice();
    const priced = [
      ...windowValid.map((p) => p.price),
      ...(prior ? [prior.price] : []),
      ...(plus !== null ? [plus] : []),
    ];
    const [yMin, yMax, yStep] = niceScale(
      Math.min(...priced),
      Math.max(...priced),
      this.variant() === 'admin' ? 3 : 5,
    );
    const yOf = (price: number) =>
      plotBottom - ((price - yMin) / (yMax - yMin || 1)) * (plotBottom - PLOT_TOP);

    const place = (p: DataPoint): PlacedPoint => ({ ...p, x: xOf(p.t), y: yOf(p.price) });

    const snapshotPoints = windowValid.map(place);
    const linePoints = (prior ? [{ t: domainStart, price: prior.price }, ...windowValid] : windowValid).map(
      place,
    );

    const gridlines: { y: number; label: string }[] = [];
    for (let v = yMin; v <= yMax + 1e-6; v += yStep) {
      gridlines.push({ y: yOf(v), label: Number.isInteger(yStep) ? String(Math.round(v)) : v.toFixed(2) });
    }

    const lo = Math.min(...windowValid.map((p) => p.price));
    const hi = Math.max(...windowValid.map((p) => p.price));
    const lowPoint = snapshotPoints.find((p) => p.price === lo) ?? null;
    const lowAnchor: 'start' | 'middle' | 'end' = !lowPoint
      ? 'middle'
      : lowPoint.x > PLOT_RIGHT - 120
        ? 'end'
        : lowPoint.x < PLOT_LEFT + 90
          ? 'start'
          : 'middle';

    const gapBefore = !prior && snapshotPoints.length > 0 && snapshotPoints[0].t > domainStart + 864e5;

    // With a gap on the left, the "no data before …" note replaces the first date.
    const xTicks = buildXTicks(windowValid, xOf);

    return {
      linePath: steppedPath(linePoints),
      areaPath: steppedAreaPath(linePoints, plotBottom, linePoints.at(-1)?.x ?? PLOT_LEFT),
      snapshotPoints,
      latest: snapshotPoints.at(-1) ?? null,
      lowPoint,
      lowAnchor,
      gridlines,
      xTicks: gapBefore ? xTicks.slice(1) : xTicks,
      plusLine: plus !== null ? { y: yOf(plus), label: `AlzaPlus+ ${this.price(plus)}` } : null,
      gapLabel: gapBefore ? `no data before ${shortDate(snapshotPoints[0].t)}` : null,
      now: last.price,
      low: lo,
      high: hi,
      median: median(windowValid.map((p) => p.price)),
      count: windowValid.length,
    };
  });

  protected readonly summary = computed(() => {
    const m = this.model();
    if (!m) {
      return 'Price history: no readings yet';
    }
    const dir = m.now < m.high ? 'down from' : m.now > m.low ? 'up from' : 'flat at';
    return `Price history: ${m.count} readings, now ${this.price(m.now)}, ${dir} a high of ${this.price(
      m.high,
    )} and a low of ${this.price(m.low)}.`;
  });

  protected readonly readout = computed(() => {
    const i = this.hoveredIndex();
    const points = this.model()?.snapshotPoints ?? [];
    const p = i === null ? null : points[i];
    if (!p) {
      return null;
    }
    return {
      x: p.x,
      y: p.y,
      leftPct: (p.x / VIEW_W) * 100,
      topPct: (p.y / this.geo().height) * 100,
      flip: p.x > VIEW_W * 0.62,
      below: p.y < this.geo().height * 0.4,
      price: this.price(p.price),
      when: formatExact(new Date(p.t).toISOString()),
    };
  });

  protected setRange(range: Range): void {
    this.range.set(range);
    this.hoveredIndex.set(null);
  }

  protected track(event: PointerEvent): void {
    const points = this.model()?.snapshotPoints ?? [];
    if (points.length === 0) {
      return;
    }

    const rect = (event.currentTarget as SVGElement).getBoundingClientRect();
    const x = ((event.clientX - rect.left) / rect.width) * VIEW_W;

    let nearest = 0;
    for (let i = 1; i < points.length; i++) {
      if (Math.abs(points[i].x - x) < Math.abs(points[nearest].x - x)) {
        nearest = i;
      }
    }
    this.hoveredIndex.set(nearest);
  }

  protected step(event: KeyboardEvent): void {
    const points = this.model()?.snapshotPoints ?? [];
    if (points.length === 0) {
      return;
    }

    const current = this.hoveredIndex() ?? points.length - 1;
    let next: number | null = current;

    switch (event.key) {
      case 'ArrowLeft':
        next = Math.max(0, current - 1);
        break;
      case 'ArrowRight':
        next = Math.min(points.length - 1, current + 1);
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = points.length - 1;
        break;
      case 'Escape':
        next = null;
        break;
      default:
        return;
    }

    event.preventDefault();
    this.hoveredIndex.set(next);
  }

  protected price(value: number | null): string {
    return formatPrice(value, this.currency());
  }

  protected shortDate(t: number): string {
    return shortDate(t);
  }
}

/** A rounded [min, max, step] covering the data with roughly `intervals` gridlines. */
function niceScale(dataMin: number, dataMax: number, intervals: number): [number, number, number] {
  if (!Number.isFinite(dataMin) || !Number.isFinite(dataMax)) {
    return [0, 1, 1];
  }
  if (dataMin === dataMax) {
    const pad = Math.abs(dataMin) * 0.05 || 1;
    dataMin -= pad;
    dataMax += pad;
  }

  const raw = (dataMax - dataMin) / intervals;
  const mag = 10 ** Math.floor(Math.log10(raw));
  const norm = raw / mag;
  const step = mag * (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10);

  return [Math.floor(dataMin / step) * step, Math.ceil(dataMax / step) * step, step];
}

/** First date, one or two interior dates, last date — anchored start / middle / end. */
function buildXTicks(
  points: readonly DataPoint[],
  xOf: (t: number) => number,
): { x: number; label: string; anchor: 'start' | 'middle' | 'end' }[] {
  if (points.length === 0) {
    return [];
  }

  const first = points[0].t;
  const last = points[points.length - 1].t;
  const ticks: { x: number; label: string; anchor: 'start' | 'middle' | 'end' }[] = [
    { x: xOf(first), label: shortDate(first), anchor: 'start' },
  ];

  const interior = last - first > 20 * 864e5 ? 2 : 1;
  for (let i = 1; i <= interior; i++) {
    const t = first + ((last - first) * i) / (interior + 1);
    ticks.push({ x: xOf(t), label: shortDate(t), anchor: 'middle' });
  }

  if (last !== first) {
    ticks.push({ x: xOf(last), label: shortDate(last), anchor: 'end' });
  }
  return ticks;
}

function shortDate(t: number): string {
  return new Date(t).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
}
