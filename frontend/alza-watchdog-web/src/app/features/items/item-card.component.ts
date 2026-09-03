import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { AccountService } from '../../core/account.service';
import { ClockService } from '../../core/clock.service';
import { TrackedItem } from '../../core/models';
import { formatAvailability, formatExact, formatPrice, formatRelative } from '../../core/format';
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

  protected readonly priceLabel = computed(() =>
    formatPrice(this.item().currentPrice, this.item().currency),
  );

  /** Signed change against the previous reading, e.g. "−2,40 €". */
  protected readonly changeLabel = computed(() => {
    const { priceChange, currency } = this.item();
    if (priceChange === null || priceChange === 0) {
      return null;
    }

    return (priceChange > 0 ? '+' : '−') + formatPrice(Math.abs(priceChange), currency);
  });

  /**
   * The discounts worth showing under the headline price. Each is only listed
   * when it actually beats the normal price, so a card never carries a row that
   * says nothing.
   */
  protected readonly offers = computed(() => {
    const { currency, currentPrice, plusPrice, couponPrice } = this.item();

    const beatsNormalPrice = (value: number | null): value is number =>
      value !== null && (currentPrice === null || value < currentPrice);

    return [
      // The members' price is always stored; it is only shown to someone who
      // actually holds the membership, since nobody else can pay it.
      { label: 'AlzaPlus+', price: this.account.hasAlzaPlus() ? plusPrice : null },
      { label: 'With code', price: couponPrice },
    ]
      .filter((offer) => beatsNormalPrice(offer.price))
      .map((offer) => ({ label: offer.label, value: formatPrice(offer.price, currency) }));
  });

  protected readonly rangeLabel = computed(() => {
    const { lowestPrice, highestPrice, currency, history } = this.item();

    // A single reading makes a range meaningless — there is nothing to compare to.
    if (history.length < 2 || lowestPrice === null || highestPrice === null) {
      return null;
    }

    return `${formatPrice(lowestPrice, currency)} – ${formatPrice(highestPrice, currency)}`;
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
