import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AccountService } from '../../core/account.service';
import { ItemsStore } from '../../core/items.store';
import { payable, priceStats, quoteOf } from '../../core/pricing';
import { TrackedItem } from '../../core/models';
import { describeError } from '../../core/watchdog-api.service';
import { compactGuid, expandGuid } from '../../core/guid';
import { ItemCardComponent } from '../items/item-card.component';

type SortKey = 'recent' | 'drop' | 'cheapest' | 'name' | 'custom';

@Component({
  selector: 'app-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, ItemCardComponent],
  templateUrl: './list-page.component.html',
  styleUrl: './list-page.component.scss',
})
export class ListPageComponent {
  private readonly store = inject(ItemsStore);
  private readonly account = inject(AccountService);
  private readonly router = inject(Router);

  /** The raw :listId route parameter — compact, no dashes. */
  readonly listIdParam = input<string | undefined>(undefined, { alias: 'listId' });

  /** The canonical dashed id, which is what the API and the list metadata use. */
  protected readonly listId = computed(() => {
    const raw = this.listIdParam();
    return raw ? expandGuid(raw) : undefined;
  });

  protected readonly loading = this.store.loading;
  protected readonly isEmpty = this.store.isEmpty;
  protected readonly loadError = this.store.error;
  protected readonly missing = this.store.missing;
  protected readonly watchedCount = this.store.watchedCount;
  protected readonly atLowestCount = this.store.atLowestCount;
  protected readonly unavailableCount = this.store.unavailableCount;
  protected readonly accountKey = this.account.key;
  protected readonly accountFailed = this.account.loadFailed;

  protected readonly sort = signal<SortKey>('recent');
  protected readonly expandedItemId = signal<string | null>(null);
  protected readonly notes = signal<Readonly<Record<string, string>>>({});
  protected readonly starting = signal(false);
  protected readonly draggingId = signal<string | null>(null);
  protected readonly dragOverId = signal<string | null>(null);

  protected readonly listName = computed(() => {
    const id = this.listId();
    return this.account.lists().find((l) => l.id === id)?.name ?? this.store.listName();
  });

  protected readonly renaming = signal(false);
  protected readonly renameValue = signal('');
  protected readonly confirmingDelete = signal(false);
  protected readonly listError = signal<string | null>(null);
  protected readonly working = signal(false);

  protected readonly canDelete = computed(() => this.account.lists().length > 1);
  /** No account resolved at all — a bad or lost URL. */
  protected readonly lost = computed(() => this.accountKey() === null || this.accountFailed());

  protected readonly sortOptions: { key: SortKey; label: string }[] = [
    { key: 'recent', label: 'Recently added' },
    { key: 'drop', label: 'Biggest drop' },
    { key: 'cheapest', label: 'Cheapest first' },
    { key: 'name', label: 'Name' },
    { key: 'custom', label: 'My order' },
  ];

  protected readonly items = computed(() => {
    const items = [...this.store.items()];

    switch (this.sort()) {
      case 'recent':
        return items.sort((a, b) => b.createdAt.localeCompare(a.createdAt));
      case 'drop': {
        // Rises and unchanged prices sink below every drop, so the reason you
        // opened the page is at the top.
        const plus = this.account.hasAlzaPlus();
        return items.sort((a, b) => rank(a, plus) - rank(b, plus));
      }
      case 'cheapest': {
        // Sorts by what you could actually pay, so a product that is only cheap
        // behind a discount you cannot use does not jump the queue.
        const plus = this.account.hasAlzaPlus();
        return items.sort(
          (a, b) => nullsLast(payable(quoteOf(a), plus).value) - nullsLast(payable(quoteOf(b), plus).value),
        );
      }
      case 'name':
        return items.sort((a, b) => (a.name ?? a.url).localeCompare(b.name ?? b.url));
      case 'custom':
        return items.sort((a, b) => a.sortOrder - b.sortOrder || a.createdAt.localeCompare(b.createdAt));
      default:
        return items;
    }
  });

  constructor() {
    // Re-runs whenever the route parameter changes, which is what makes switching
    // lists a plain navigation rather than bespoke state juggling.
    effect(() => {
      const id = this.listId();
      if (!id) {
        return;
      }

      this.listError.set(null);
      this.expandedItemId.set(null);
      void this.store.open(id);
      void this.account.loadLists();
    });
  }

  protected isBusy(id: string): boolean {
    return this.store.isBusy(id);
  }

  protected toggleExpand(id: string): void {
    this.expandedItemId.update((current) => (current === id ? null : id));
  }

  protected noteFor(id: string): string | null {
    return this.notes()[id] ?? null;
  }

  protected canReorder(): boolean {
    return this.sort() === 'custom';
  }

  protected onDragStart(event: DragEvent, id: string): void {
    this.draggingId.set(id);
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', id);
    }
  }

  protected onDragOver(event: DragEvent, id: string): void {
    if (!this.canReorder()) {
      return;
    }

    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    this.dragOverId.set(id);
  }

  protected onDragLeave(event: DragEvent, id: string): void {
    if (this.dragOverId() === id) {
      this.dragOverId.set(null);
    }
  }

  protected onDrop(event: DragEvent, targetId: string): void {
    event.preventDefault();
    if (!this.canReorder()) {
      return;
    }

    const draggingId = this.draggingId() ?? event.dataTransfer?.getData('text/plain');
    this.draggingId.set(null);
    this.dragOverId.set(null);

    if (!draggingId || draggingId === targetId) {
      return;
    }

    const ordered = this.items().map((item) => item.id);
    const from = ordered.indexOf(draggingId);
    const to = ordered.indexOf(targetId);
    if (from === -1 || to === -1) {
      return;
    }

    ordered.splice(from, 1);
    ordered.splice(to, 0, draggingId);

    void this.store.reorder(ordered).then((message) => {
      if (message) {
        this.listError.set(message);
      }
    });
  }

  protected onDragEnd(): void {
    this.draggingId.set(null);
    this.dragOverId.set(null);
  }

  protected async resume(item: TrackedItem): Promise<void> {
    this.setNote(item.id, await this.store.resume(item.id));
  }

  protected async remove(item: TrackedItem): Promise<void> {
    this.setNote(item.id, await this.store.remove(item.id));
  }

  protected dismissLoadError(): void {
    this.store.dismissError();
  }

  protected startRename(): void {
    this.listError.set(null);
    this.renameValue.set(this.listName());
    this.renaming.set(true);
  }

  protected cancelRename(): void {
    this.renaming.set(false);
  }

  protected async rename(): Promise<void> {
    const name = this.renameValue().trim();
    const id = this.listId();
    if (!name || !id) {
      return;
    }

    this.working.set(true);
    this.listError.set(null);

    try {
      await this.account.renameList(id, name);
      this.store.setListName(name);
      this.renaming.set(false);
    } catch (error) {
      this.listError.set(describeError(error, 'Could not rename that list.'));
    } finally {
      this.working.set(false);
    }
  }

  protected askDelete(): void {
    this.listError.set(null);
    this.confirmingDelete.set(true);
  }

  protected cancelDelete(): void {
    this.confirmingDelete.set(false);
  }

  protected async deleteList(): Promise<void> {
    const id = this.listId();
    if (!id) {
      return;
    }

    this.working.set(true);
    this.listError.set(null);

    try {
      await this.account.deleteList(id);
      this.confirmingDelete.set(false);

      const next = this.account.lists()[0];
      if (next) {
        await this.router.navigate([
          '/user', compactGuid(this.accountKey()), 'list', compactGuid(next.id),
        ]);
      }
    } catch (error) {
      this.listError.set(describeError(error, 'Could not delete that list.'));
    } finally {
      this.working.set(false);
    }
  }

  /** Last resort for someone who arrived with a URL that resolves to nothing. */
  protected async startFresh(): Promise<void> {
    this.starting.set(true);
    await this.router.navigate(['/']);
    this.starting.set(false);
  }

  private setNote(id: string, message: string | null): void {
    this.notes.update((notes) => {
      const next = { ...notes };
      if (message) {
        next[id] = message;
      } else {
        delete next[id];
      }
      return next;
    });
  }
}

/** Sorts real drops first, then unchanged, then rises, then unknowns. */
function rank(item: TrackedItem, hasAlzaPlus: boolean): number {
  return priceStats(item, hasAlzaPlus).change ?? Number.POSITIVE_INFINITY;
}

function nullsLast(value: number | null): number {
  return value ?? Number.POSITIVE_INFINITY;
}
