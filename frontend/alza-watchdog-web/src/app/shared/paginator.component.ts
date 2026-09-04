import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/**
 * Page controls for a long admin list.
 *
 * The page number is owned by the caller rather than held here: filtering and
 * searching have to reset it, and a control that kept its own state would keep
 * showing page 7 of a list that is now two pages long.
 */
@Component({
  selector: 'app-paginator',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (pageCount() > 1) {
      <nav class="pager" [attr.aria-label]="label()">
        <button type="button" (click)="go(page() - 1)" [disabled]="page() <= 1">‹ Prev</button>
        <span class="range" aria-live="polite">
          {{ first() }}–{{ last() }} of {{ total() }}
          <span class="sep">·</span>
          page {{ page() }}/{{ pageCount() }}
        </span>
        <button type="button" (click)="go(page() + 1)" [disabled]="page() >= pageCount()">Next ›</button>
      </nav>
    }
  `,
  styleUrl: './paginator.component.scss',
})
export class PaginatorComponent {
  readonly total = input.required<number>();
  readonly page = input.required<number>();
  readonly pageSize = input(25);
  readonly label = input('Pagination');

  readonly pageChange = output<number>();

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  protected readonly first = computed(() => (this.page() - 1) * this.pageSize() + 1);
  protected readonly last = computed(() => Math.min(this.page() * this.pageSize(), this.total()));

  protected go(page: number): void {
    this.pageChange.emit(Math.min(Math.max(page, 1), this.pageCount()));
  }
}
