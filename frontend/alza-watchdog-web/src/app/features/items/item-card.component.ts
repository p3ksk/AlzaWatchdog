import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { AccountService } from '../../core/account.service';
import { ClockService } from '../../core/clock.service';
import { TrackedItem } from '../../core/models';
import { formatAvailability, formatExact, formatPrice, formatRelative } from '../../core/format';
import { priceStats } from '../../core/pricing';
import { PriceChartComponent } from './price-chart.component';

@Component({
  selector: 'app-item-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PriceChartComponent],
  templateUrl: './item-card.component.html',
  styleUrl: './item-card.component.scss',
})
export class ItemCardComponent {
  private readonly clock = inject(ClockService);
  private readonly account = inject(AccountService);

  readonly item = input.required<TrackedItem>();
  readonly busy = input(false);
  /** Message from the last failed action on this card, e.g. the refresh cooldown. */
  readonly note = input<string | null>(null);

  readonly resumeRequested = output<void>();
  readonly removeRequested = output<void>();

  protected readonly expanded = signal(false);
  protected readonly confirmingRemove = signal(false);

  protected readonly checkedLabel = computed(() =>
    formatRelative(this.item().lastCheckedAt, this.clock.now()),
  );

  /**
   * Every price on this card, measured on what this person could actually pay —
   * the members' price only counts for a member. Recomputed when the AlzaPlus+
   * switch moves, so the card answers the new question immediately rather than
   * waiting for the next load.
   */
  protected readonly hasAlzaPlus = this.account.hasAlzaPlus;

  protected readonly stats = computed(() => priceStats(this.item(), this.hasAlzaPlus()));

  protected readonly priceLabel = computed(() =>
    formatPrice(this.stats().value, this.item().currency),
  );

  /** The price the discount is measured against, shown struck through beside it. */
  protected readonly shelfLabel = computed(() => {
    const { value, shelf } = this.stats();
    return shelf !== null && value !== null && value < shelf
      ? formatPrice(shelf, this.item().currency)
      : null;
  });

  /** Signed change against the previous reading, e.g. "−2,40 €". */
  protected readonly changeLabel = computed(() => {
    const { change } = this.stats();
    if (change === null || change === 0) {
      return null;
    }

    return (change > 0 ? '+' : '−') + formatPrice(Math.abs(change), this.item().currency);
  });

  /**
   * The discount that is not already leading the card. Showing the winner again
   * underneath would just repeat the headline; showing the loser tells you what
   * else this product offers.
   */
  protected readonly offers = computed(() => {
    const { currency, currentPrice, plusPrice, couponPrice } = this.item();
    const winner = this.stats().via;

    const beatsShelfPrice = (value: number | null): value is number =>
      value !== null && (currentPrice === null || value < currentPrice);

    return [
      // The members' price is always stored; it is only shown to someone who
      // actually holds the membership, since nobody else can pay it.
      { label: 'AlzaPlus+', price: this.account.hasAlzaPlus() ? plusPrice : null },
      { label: 'With code', price: couponPrice },
    ]
      .filter((offer) => offer.label !== winner && beatsShelfPrice(offer.price))
      .map((offer) => ({ label: offer.label, value: formatPrice(offer.price, currency) }));
  });

  protected readonly rangeLabel = computed(() => {
    const { lowest, highest } = this.stats();

    // A single reading makes a range meaningless — there is nothing to compare to.
    if (this.item().history.length < 2 || lowest === null || highest === null) {
      return null;
    }

    return `${formatPrice(lowest, this.item().currency)} – ${formatPrice(highest, this.item().currency)}`;
  });

  protected readonly availabilityLabel = computed(() => formatAvailability(this.item().availability));

  protected readonly isOutOfStock = computed(() =>
    ['OutOfStock', 'SoldOut', 'Discontinued'].includes(this.item().availability ?? ''),
  );

  protected exact(iso: string | null): string {
    return formatExact(iso);
  }

  protected toggle(): void {
    this.expanded.update((open) => !open);
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
