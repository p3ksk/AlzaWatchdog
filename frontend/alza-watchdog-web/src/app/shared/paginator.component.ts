import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/**
 * Page controls for a long admin list. The page number belongs to the caller:
 * filtering and searching reset it, and a control holding its own state would sit
 * on page 7 of a list that is now two pages long.
 */
@Component({
  selector: 'app-paginator',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (pageCount() > 1) {
      <nav class="pager" [attr.aria-label]="label()">
        <span class="range" aria-live="polite">
          {{ first() }}–{{ last() }} of {{ total() }}
        </span>
        <span class="controls">
          <button type="button" (click)="go(page() - 1)" [disabled]="page() <= 1">Prev</button>
          <button type="button" class="next" (click)="go(page() + 1)" [disabled]="page() >= pageCount()">
            Next
          </button>
        </span>
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
