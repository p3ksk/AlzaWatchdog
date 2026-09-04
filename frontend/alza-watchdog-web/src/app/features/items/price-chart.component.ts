import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  signal,
  viewChild,
  afterNextRender,
} from '@angular/core';
import { PriceSnapshot } from '../../core/models';
import { formatExact, formatPrice, formatRelative } from '../../core/format';
import { monotoneCubicPath } from './smooth-path';
import { payableAt } from '../../core/pricing';

interface Plotted {
  x: number;
  y: number;
  price: number;
  snapshot: PriceSnapshot;
}

@Component({
  selector: 'app-price-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let points = plotted();

    <div class="frame" #frame [class.detailed]="detailed()">
      @if (points.length < 2) {
        <p class="single">Awaiting a second reading&hellip;</p>
      } @else {
        <!--
          Rendered in real pixel coordinates rather than a stretched 0–100 viewBox.
          Uniform scaling is what keeps the curve, the glow and the dots smooth and
          round instead of squashed, which is the whole point of measuring first.
        -->
        <svg
          [attr.width]="size().width"
          [attr.height]="size().height"
          [attr.viewBox]="'0 0 ' + size().width + ' ' + size().height"
          (pointermove)="detailed() && track($event)"
          (pointerleave)="hovered.set(null)"
          role="img"
          [attr.aria-label]="summary()"
        >
          <defs>
            <linearGradient [attr.id]="gradientId" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" [attr.stop-color]="'currentColor'" stop-opacity="0.28" />
              <stop offset="100%" [attr.stop-color]="'currentColor'" stop-opacity="0" />
            </linearGradient>
            <filter [attr.id]="glowId" x="-20%" y="-40%" width="140%" height="180%">
              <feGaussianBlur stdDeviation="3" result="blur" />
              <feMerge>
                <feMergeNode in="blur" />
                <feMergeNode in="SourceGraphic" />
              </feMerge>
            </filter>
          </defs>

          <path class="area" [attr.d]="areaPath()" [attr.fill]="'url(#' + gradientId + ')'" />
          <path class="line" [attr.d]="linePath()" [attr.filter]="'url(#' + glowId + ')'" />

          @if (detailed()) {
            @for (point of points; track point.snapshot.capturedAt) {
              <circle class="dot" [attr.cx]="point.x" [attr.cy]="point.y" r="3" />
            }
          }

          @let last = points[points.length - 1];
          <circle class="dot current" [attr.cx]="last.x" [attr.cy]="last.y" r="4.5" />

          @let active = hoveredPoint();
          @if (active) {
            <line
              class="guide"
              [attr.x1]="active.x"
              [attr.x2]="active.x"
              y1="0"
              [attr.y2]="size().height"
            />
            <circle class="dot active" [attr.cx]="active.x" [attr.cy]="active.y" r="5.5" />
          }
        </svg>

        @if (active; as point) {
          <div
            class="tip"
            [style.left.px]="point.x"
            [style.top.px]="point.y"
            [class.flip]="point.x > size().width * 0.65"
            [class.below]="point.y < size().height * 0.45"
          >
            <strong>{{ price(point.price) }}</strong>
            <span [title]="exact(point.snapshot.capturedAt)">
              {{ relative(point.snapshot.capturedAt) }}
            </span>
          </div>
        }
      }
    </div>

    @if (detailed() && points.length > 1) {
      <div class="bounds">
        <span>LO {{ price(low()) }}</span>
        <span>{{ points.length }} readings</span>
        <span>HI {{ price(high()) }}</span>
      </div>
    }
  `,
  styleUrl: './price-chart.component.scss',
})
export class PriceChartComponent {
  private static nextId = 0;

  readonly history = input.required<PriceSnapshot[]>();
  readonly currency = input<string | null>(null);
  /** Adds every data point, hover readout, the low/high footer and a taller plot. */
  readonly detailed = input(false);
  /**
   * Whether the AlzaPlus+ price counts as payable. Passed in rather than read
   * from the account, because the admin section draws the same chart for products
   * belonging to other people, where no single membership applies.
   */
  readonly hasAlzaPlus = input(false);

  private readonly frame = viewChild.required<ElementRef<HTMLElement>>('frame');
  // Captured as a field: inject() is only legal here, not inside the
  // afterNextRender callback, which runs outside the injection context.
  private readonly destroyRef = inject(DestroyRef);

  /** SVG ids must be unique per instance or defs collide across cards. */
  private readonly uid = PriceChartComponent.nextId++;
  protected readonly gradientId = `chart-fill-${this.uid}`;
  protected readonly glowId = `chart-glow-${this.uid}`;

  protected readonly size = signal({ width: 0, height: 0 });
  protected readonly hovered = signal<number | null>(null);

  constructor() {
    // The plot geometry depends on the element's real size, so it can only be
    // computed once the element exists, and must follow it as it resizes.
    afterNextRender(() => {
      const element = this.frame().nativeElement;
      const observer = new ResizeObserver(([entry]) => {
        const { width, height } = entry.contentRect;
        this.size.set({ width: Math.round(width), height: Math.round(height) });
      });

      observer.observe(element);
      this.destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  /**
   * Each reading reduced to the price that could actually have been paid at the
   * time, so the line matches the number on the card instead of tracking a shelf
   * price nobody pays.
   */
  private readonly priced = computed(() =>
    this.history()
      .map((snapshot) => ({ snapshot, price: payableAt(snapshot, this.hasAlzaPlus()) }))
      .filter((reading): reading is { snapshot: PriceSnapshot; price: number } => reading.price !== null),
  );

  protected readonly low = computed(() => {
    const prices = this.priced().map((s) => s.price);
    return prices.length ? Math.min(...prices) : null;
  });

  protected readonly high = computed(() => {
    const prices = this.priced().map((s) => s.price);
    return prices.length ? Math.max(...prices) : null;
  });

  protected readonly plotted = computed<Plotted[]>(() => {
    const snapshots = this.priced();
    const { width, height } = this.size();

    if (snapshots.length < 2 || width === 0 || height === 0) {
      return [];
    }

    const min = this.low()!;
    const max = this.high()!;
    const span = max - min;

    const times = snapshots.map((s) => new Date(s.snapshot.capturedAt).getTime());
    const firstTime = times[0];
    const timeSpan = times[times.length - 1] - firstTime;

    // Inset so the stroke, its glow and the end dots are never clipped.
    const padX = 6;
    const padY = 10;
    const plotWidth = Math.max(1, width - padX * 2);
    const plotHeight = Math.max(1, height - padY * 2);

    return snapshots.map(({ snapshot, price }, index) => ({
      snapshot,
      price,
      // Spaced by when the reading happened, not by its index: snapshots are only
      // written when the price moves, so the gaps between them are uneven and
      // evenly spacing them would misrepresent how fast a price actually fell.
      x: padX + (timeSpan === 0 ? (index / (snapshots.length - 1)) * plotWidth
                                : ((times[index] - firstTime) / timeSpan) * plotWidth),
      y: span === 0
        ? padY + plotHeight / 2
        : padY + plotHeight - ((price - min) / span) * plotHeight,
    }));
  });

  protected readonly linePath = computed(() => monotoneCubicPath(this.plotted()));

  /** The curve closed down to the baseline, for the tinted fill underneath it. */
  protected readonly areaPath = computed(() => {
    const points = this.plotted();
    const line = this.linePath();

    if (points.length < 2 || !line) {
      return '';
    }

    const bottom = this.size().height;
    const first = points[0];
    const last = points[points.length - 1];

    return `${line} L${last.x.toFixed(2)},${bottom} L${first.x.toFixed(2)},${bottom} Z`;
  });

  protected readonly hoveredPoint = computed(() => {
    const index = this.hovered();
    return index === null ? null : (this.plotted()[index] ?? null);
  });

  protected readonly summary = computed(() => {
    const points = this.plotted();
    if (points.length < 2) {
      return 'Price history';
    }

    const first = points[0].price;
    const last = points[points.length - 1].price;
    const direction = last < first ? 'down from' : last > first ? 'up from' : 'unchanged from';

    return `Price history: ${points.length} readings, now ${this.price(last)}, ${direction} ${this.price(first)}. Lowest ${this.price(this.low())}, highest ${this.price(this.high())}.`;
  });

  /** Snaps to the nearest reading rather than interpolating between them. */
  protected track(event: PointerEvent): void {
    const points = this.plotted();
    if (points.length === 0) {
      return;
    }

    const bounds = (event.currentTarget as SVGElement).getBoundingClientRect();
    const x = event.clientX - bounds.left;

    let nearest = 0;
    let best = Number.POSITIVE_INFINITY;

    for (let i = 0; i < points.length; i++) {
      const distance = Math.abs(points[i].x - x);
      if (distance < best) {
        best = distance;
        nearest = i;
      }
    }

    this.hovered.set(nearest);
  }

  protected price(value: number | null): string {
    return formatPrice(value, this.currency());
  }

  protected relative(iso: string): string {
    return formatRelative(iso);
  }

  protected exact(iso: string): string {
    return formatExact(iso);
  }
}
