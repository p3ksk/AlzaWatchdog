import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { PriceSnapshot } from '../../core/models';
import { payableAt } from '../../core/pricing';
import { steppedPath } from './stepped-path';

/**
 * The at-a-glance signal at the end of each row: a small stepped price line,
 * accent-coloured when the product is at or near its low, quiet otherwise.
 * Selecting the row opens the full chart; this is just the shape.
 */
@Component({
  selector: 'app-sparkline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (path(); as d) {
      <svg
        viewBox="0 0 106 34"
        width="106"
        height="34"
        role="img"
        [attr.aria-label]="label()"
      >
        <path
          [attr.d]="d"
          fill="none"
          [attr.stroke]="stroke()"
          stroke-width="1.6"
          stroke-linejoin="round"
          stroke-linecap="round"
        />
        @if (lastPoint(); as p) {
          <circle [attr.cx]="p.x" [attr.cy]="p.y" r="2.6" [attr.fill]="stroke()" />
        }
      </svg>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
    }
  `,
})
export class SparklineComponent {
  readonly history = input.required<PriceSnapshot[]>();
  readonly hasAlzaPlus = input(false);
  readonly atLow = input(false);
  readonly label = input('Price trend');

  protected readonly stroke = computed(() => (this.atLow() ? 'var(--accent)' : 'var(--ink-dim)'));

  private readonly points = computed(() => {
    const readings = this.history()
      .map((s) => ({ t: new Date(s.capturedAt).getTime(), v: payableAt(s, this.hasAlzaPlus()) }))
      .filter((p): p is { t: number; v: number } => p.v !== null);

    if (readings.length < 2) {
      return [];
    }

    const pad = 3;
    const w = 106 - pad * 2;
    const h = 34 - pad * 2;

    const values = readings.map((p) => p.v);
    const min = Math.min(...values);
    const span = Math.max(...values) - min || 1;

    const t0 = readings[0].t;
    const tSpan = readings[readings.length - 1].t - t0 || 1;

    return readings.map((p) => ({
      x: pad + ((p.t - t0) / tSpan) * w,
      y: pad + h - ((p.v - min) / span) * h,
    }));
  });

  protected readonly path = computed(() => steppedPath(this.points()));
  protected readonly lastPoint = computed(() => this.points().at(-1) ?? null);
}
