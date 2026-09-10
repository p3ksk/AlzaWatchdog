import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { AccountService } from '../../core/account.service';
import { ClockService } from '../../core/clock.service';
import { TrackedItem } from '../../core/models';
import { formatExact, formatPrice, formatRelative } from '../../core/format';
import { priceStats } from '../../core/pricing';
import { PriceChartComponent } from './price-chart.component';
import { SparklineComponent } from './sparkline.component';

/**
 * One product as a ledger row: name and badges, the change since the last
 * reading, the price, and a sparkline. Selecting the row opens a detail panel
 * with the full chart. The row is the disclosure control — it has no nested
 * links, so the whole thing is one button.
 */
@Component({
  selector: 'app-item-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PriceChartComponent, SparklineComponent],
  templateUrl: './item-card.component.html',
  styleUrl: './item-card.component.scss',
})
export class ItemCardComponent {
  private readonly clock = inject(ClockService);
  private readonly account = inject(AccountService);

  readonly item = input.required<TrackedItem>();
  readonly busy = input(false);
  readonly expanded = input(false);
  /** Message from the last failed action on this row, e.g. the refresh cooldown. */
  readonly note = input<string | null>(null);

  readonly toggleRequested = output<void>();
  readonly resumeRequested = output<void>();
  readonly removeRequested = output<void>();

  protected readonly confirmingRemove = signal(false);

  protected readonly hasAlzaPlus = this.account.hasAlzaPlus;

  protected readonly checkedLabel = computed(() =>
    formatRelative(this.item().lastCheckedAt, this.clock.now()),
  );

  protected readonly stats = computed(() => priceStats(this.item(), this.hasAlzaPlus()));

  protected readonly priceLabel = computed(() =>
    formatPrice(this.stats().value, this.item().currency),
  );

  /** The shelf price the discount is measured against, struck through beside it. */
  protected readonly shelfLabel = computed(() => {
    const { value, shelf } = this.stats();
    return shelf !== null && value !== null && value < shelf
      ? formatPrice(shelf, this.item().currency)
      : null;
  });

  /** Shown under the price when there is no discount to strike through. */
  protected readonly lowLabel = computed(() => {
    const { lowest } = this.stats();
    return this.shelfLabel() === null && lowest !== null
      ? `low ${formatPrice(lowest, this.item().currency)}`
      : null;
  });

  /** Signed percentage against the previous reading, e.g. "−18%" / "+11%". */
  protected readonly changeLabel = computed(() => {
    const { change, previous } = this.stats();
    if (change === null || previous === null || previous === 0) {
      return null;
    }

    const pct = Math.round((change / previous) * 100);
    if (pct === 0) {
      return null;
    }

    return `${pct > 0 ? '+' : '−'}${Math.abs(pct)}%`;
  });

  protected readonly isDrop = computed(() => (this.stats().change ?? 0) < 0);

  /** The AlzaPlus+ price is what this member would actually pay, and it beats the shelf price. */
  protected readonly showPlusBadge = computed(() => this.stats().via === 'AlzaPlus+');

  protected readonly isOutOfStock = computed(() =>
    ['OutOfStock', 'SoldOut', 'Discontinued'].includes(this.item().availability ?? ''),
  );

  /** The row opens a chart only once there is a history to draw. */
  protected readonly canExpand = computed(() => this.item().history.length >= 1);

  /**
   * The dashed AlzaPlus+ reference line is only worth drawing for a non-member:
   * once the switch is on, the price line already *is* the members' price.
   */
  protected readonly plusForChart = computed(() =>
    !this.hasAlzaPlus() ? this.item().plusPrice : null,
  );

  protected exact(iso: string | null): string {
    return formatExact(iso);
  }

  protected toggle(): void {
    if (this.canExpand()) {
      this.toggleRequested.emit();
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.toggle();
    }
  }

  protected askRemove(): void {
    this.confirmingRemove.set(true);
  }

  protected cancelRemove(): void {
    this.confirmingRemove.set(false);
  }

  protected confirmRemove(): void {
    this.confirmingRemove.set(false);
    this.removeRequested.emit();
  }
}
